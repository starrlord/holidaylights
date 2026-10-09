namespace HolidayLights.Core.Sprites;

/// <summary>
/// A thread-safe least-recently-used cache of sprites and halos with a byte budget.
/// </summary>
/// <remarks>
/// When the entries exceed the budget, the least recently used ones are dropped (the most recent entry always stays, even
/// when it alone exceeds the budget). Dropping only forgets the reference: callers that still hold an image keep it.
/// </remarks>
internal sealed class SpriteMemoryCache
{
    private readonly object gate = new();
    private readonly Dictionary<SpriteCacheKey, LinkedListNode<(SpriteCacheKey Key, SpriteCacheEntry Entry)>> map = [];
    private readonly LinkedList<(SpriteCacheKey Key, SpriteCacheEntry Entry)> recency = new();
    private long bytes;

    /// <summary>Creates the cache.</summary>
    /// <param name="budgetBytes">The most memory the entries may account for.</param>
    public SpriteMemoryCache(long budgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetBytes);
        BudgetBytes = budgetBytes;
    }

    /// <summary>The budget.</summary>
    public long BudgetBytes { get; }

    /// <summary>The memory the entries account for now.</summary>
    public long Bytes
    {
        get
        {
            lock (gate)
            {
                return bytes;
            }
        }
    }

    /// <summary>The number of entries.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return map.Count;
            }
        }
    }

    /// <summary>Looks up an entry and marks it most recently used.</summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry when found.</param>
    /// <returns>True when the key is cached.</returns>
    public bool TryGet(SpriteCacheKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SpriteCacheEntry? entry)
    {
        lock (gate)
        {
            if (map.TryGetValue(key, out var node))
            {
                recency.Remove(node);
                recency.AddFirst(node);
                entry = node.Value.Entry;
                return true;
            }
        }

        entry = null;
        return false;
    }

    /// <summary>Adds or replaces an entry as the most recently used one, then trims to the budget.</summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry.</param>
    public void Add(SpriteCacheKey key, SpriteCacheEntry entry)
    {
        lock (gate)
        {
            if (map.TryGetValue(key, out var existing))
            {
                RemoveNode(existing);
            }

            map[key] = recency.AddFirst((key, entry));
            bytes += entry.SizeInBytes;
            while (bytes > BudgetBytes && recency.Count > 1)
            {
                RemoveNode(recency.Last!);
            }
        }
    }

    /// <summary>Drops every entry.</summary>
    public void Clear()
    {
        lock (gate)
        {
            map.Clear();
            recency.Clear();
            bytes = 0;
        }
    }

    /// <summary>Drops every entry of a bulb content key.</summary>
    /// <param name="contentKey">The content key (compared ordinally).</param>
    /// <returns>The number of entries dropped.</returns>
    public int RemoveContent(string contentKey)
    {
        lock (gate)
        {
            var doomed = new List<LinkedListNode<(SpriteCacheKey Key, SpriteCacheEntry Entry)>>();
            for (var node = recency.First; node is not null; node = node.Next)
            {
                if (string.Equals(node.Value.Key.ContentKey, contentKey, StringComparison.Ordinal))
                {
                    doomed.Add(node);
                }
            }

            doomed.ForEach(RemoveNode);
            return doomed.Count;
        }
    }

    private void RemoveNode(LinkedListNode<(SpriteCacheKey Key, SpriteCacheEntry Entry)> node)
    {
        recency.Remove(node);
        map.Remove(node.Value.Key);
        bytes -= node.Value.Entry.SizeInBytes;
    }
}
