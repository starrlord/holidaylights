namespace HolidayLights.Core.Bulbs;

/// <summary>
/// A least-recently-used cache of decoded add-on animations, bounded by pixel memory, shared by every add-on bulb.
/// Keys combine a bulb's <see cref="IBulb.ContentKey"/> with an entry index, so a changed file never hits stale frames.
/// </summary>
/// <remarks>Thread-safe. Two threads asking for the same missing key may both decode it; the first result is kept.</remarks>
internal sealed class DecodedArtCache
{
    /// <summary>The cache add-on bulbs use: 96 MB of frames (the whole bundled set decodes to about 320 MB).</summary>
    public static DecodedArtCache Shared { get; } = new(96L * 1024 * 1024);

    private readonly long capacityBytes;
    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, Rgba32Image[] Frames, long Bytes)>> map = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, Rgba32Image[] Frames, long Bytes)> recency = new();
    private long sizeBytes;

    /// <summary>Creates a cache.</summary>
    /// <param name="capacityBytes">Pixel memory to keep (4 bytes per pixel); the most recent entry is always kept.</param>
    public DecodedArtCache(long capacityBytes) => this.capacityBytes = capacityBytes;

    /// <summary>Bytes of pixels currently cached.</summary>
    public long SizeBytes
    {
        get
        {
            lock (gate)
            {
                return sizeBytes;
            }
        }
    }

    /// <summary>Drops every cached animation (the Settings window closed; review r1 #19).</summary>
    public void Clear()
    {
        lock (gate)
        {
            map.Clear();
            recency.Clear();
            sizeBytes = 0;
        }
    }

    /// <summary>Returns cached frames or decodes and caches them.</summary>
    /// <param name="key">The key.</param>
    /// <param name="decode">Produces the frames when they are not cached (called outside the lock).</param>
    /// <returns>The frames.</returns>
    public Rgba32Image[] GetOrAdd(string key, Func<Rgba32Image[]> decode)
    {
        if (TryGet(key, out Rgba32Image[]? frames))
        {
            return frames;
        }

        return Add(key, decode());
    }

    /// <summary>Stores frames (keeps the existing ones when the key is already cached).</summary>
    /// <param name="key">The key.</param>
    /// <param name="frames">The frames.</param>
    /// <returns>The cached frames.</returns>
    public Rgba32Image[] Add(string key, Rgba32Image[] frames)
    {
        lock (gate)
        {
            if (map.TryGetValue(key, out var existing))
            {
                Touch(existing);
                return existing.Value.Frames;
            }

            long bytes = frames.Sum(f => (long)f.Pixels.Length * 4);
            map[key] = recency.AddFirst((key, frames, bytes));
            sizeBytes += bytes;
            while (sizeBytes > capacityBytes && recency.Count > 1)
            {
                var oldest = recency.Last!;
                recency.RemoveLast();
                map.Remove(oldest.Value.Key);
                sizeBytes -= oldest.Value.Bytes;
            }

            return frames;
        }
    }

    private bool TryGet(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Rgba32Image[]? frames)
    {
        lock (gate)
        {
            if (map.TryGetValue(key, out var node))
            {
                Touch(node);
                frames = node.Value.Frames;
                return true;
            }
        }

        frames = null;
        return false;
    }

    private void Touch(LinkedListNode<(string Key, Rgba32Image[] Frames, long Bytes)> node)
    {
        if (node != recency.First)
        {
            recency.Remove(node);
            recency.AddFirst(node);
        }
    }
}
