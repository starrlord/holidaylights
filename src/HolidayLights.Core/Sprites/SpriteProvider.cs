using System.Collections.Concurrent;

namespace HolidayLights.Core.Sprites;

/// <summary>
/// Scaled premultiplied sprites and glow halos with a memory and disk cache (see <see cref="ISpriteProvider"/>).
/// Owner: core-sprites.
/// </summary>
/// <remarks>
/// <para>Frames are scaled with <see cref="PixelArtScaler"/>; halos are baked by <see cref="GlowBaker"/> from the Smooth lit
/// and unlit frames at the same scale, with sigma = 0.22 x the shorter side of the scaled cell, at most 24 art pixels
/// (<see cref="GlowBaker.SigmaFor"/>). Keys are normalized the 5.4 way: a side's flavor modulo its flavor count (0 for
/// corners and the preview) and the frame modulo the animation's frame count, so equivalent requests share one image.</para>
/// <para>Memory: a least-recently-used cache within <see cref="SpriteProviderOptions.MemoryBudgetBytes"/>; concurrent
/// requests for the same sprite compute it once. Disk: <c>Cache\Sprites\v2</c> under <see cref="DataPaths.CacheFolder"/>.
/// Every request reads the disk cache on a memory miss, but only the desktop's sprites are written to it: the presenter
/// prefetches them (start-up, arrangement and scale changes), which is what makes the next start fast, while previews at
/// arbitrary zooms never fill the disk.</para>
/// <para>Halos are baked only when asked for, because nothing asks while glow is off (Classic 2003, Use Less Power, Stop
/// Flashing): <see cref="PrefetchAsync"/> bakes none, it only marks the halos of the light-bulb animations it is given as
/// the desktop's, and the first <see cref="GetGlow"/> of such a halo stores it on disk with the frames. Thread-safe.</para>
/// </remarks>
public sealed class SpriteProvider : ISpriteProvider
{
    /// <summary>The most halos remembered as the desktop's before the list starts over (the desktop shows far fewer).</summary>
    private const int MaxDesktopHalos = 4096;

    private readonly SpriteMemoryCache memory;
    private readonly SpriteDiskStore disk;
    private readonly ConcurrentDictionary<SpriteCacheKey, Lazy<SpriteCacheEntry>> pending = new();
    private readonly ConcurrentDictionary<SpriteCacheKey, byte> desktopHalos = new();
    private readonly int prefetchParallelism = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Creates the provider.</summary>
    /// <param name="paths">The cache folder (<see cref="DataPaths.CacheFolder"/>).</param>
    /// <param name="log">The log.</param>
    public SpriteProvider(DataPaths paths, IAppLog log)
        : this(paths, log, SpriteProviderOptions.Default)
    {
    }

    /// <summary>Creates the provider with explicit cache budgets.</summary>
    /// <param name="paths">The cache folder (<see cref="DataPaths.CacheFolder"/>).</param>
    /// <param name="log">The log.</param>
    /// <param name="options">The memory and disk budgets.</param>
    public SpriteProvider(DataPaths paths, IAppLog log, SpriteProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(options);
        memory = new SpriteMemoryCache(options.MemoryBudgetBytes);
        disk = new SpriteDiskStore(paths.CacheFolder, options.DiskBudgetBytes, log);
    }

    /// <summary>The memory cache (tests).</summary>
    internal SpriteMemoryCache Memory => memory;

    /// <summary>The disk cache (tests).</summary>
    internal SpriteDiskStore Disk => disk;

    /// <inheritdoc />
    public PremultipliedImage GetSprite(IBulb bulb, CellSlot slot, int flavor, int frame, double scale, SpriteStyle style) =>
        GetFrame(bulb, slot, flavor, frame, scale, style, persist: false);

    /// <inheritdoc />
    public GlowSprite? GetGlow(IBulb bulb, CellSlot slot, int flavor, double scale) => GetHalo(bulb, slot, flavor, scale);

    /// <inheritdoc />
    public Task PrefetchAsync(IEnumerable<SpriteRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        SpriteRequest[] work = [.. requests];
        foreach (SpriteRequest request in work)
        {
            ArgumentNullException.ThrowIfNull(request, nameof(requests));
        }

        var options = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = prefetchParallelism };
        return Task.Run(() => Parallel.ForEach(work, options, Prepare), cancellationToken);
    }

    /// <summary>
    /// Empties the memory cache (the Settings window closed; review r1 #19). The desktop lights keep their own surfaces,
    /// and their sprites are on disk, so a later rebuild reads them back instead of scaling again.
    /// </summary>
    public void TrimMemory() => memory.Clear();

    /// <inheritdoc />
    public void Evict(string contentKey)
    {
        ArgumentNullException.ThrowIfNull(contentKey);
        memory.RemoveContent(contentKey);
        disk.DeleteContent(contentKey);
    }

    private void Prepare(SpriteRequest request)
    {
        GetFrame(request.Bulb, request.Slot, request.Flavor, request.Frame, request.Scale, request.Style, persist: true);
        // No halo is baked here (product-owner decision 1): while glow is off nothing ever asks for one.
        SpriteCacheKey glowKey = GlowKey(request.Bulb, request.Slot, request.Flavor, request.Scale);
        if (request.Bulb.GetAnimation(request.Slot, request.Flavor).Kind == BulbAnimationKind.LightBulb && !desktopHalos.ContainsKey(glowKey))
        {
            if (desktopHalos.Count >= MaxDesktopHalos)
            {
                desktopHalos.Clear();
            }

            desktopHalos.TryAdd(glowKey, 0);
        }
    }

    private PremultipliedImage GetFrame(IBulb bulb, CellSlot slot, int flavor, int frame, double scale, SpriteStyle style, bool persist)
    {
        Validate(bulb, slot, flavor, scale);
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        if (style is not (SpriteStyle.Smooth or SpriteStyle.Crisp))
        {
            throw new ArgumentOutOfRangeException(nameof(style), style, null);
        }

        int frameIndex = frame % bulb.GetAnimation(slot, flavor).FrameCount;
        var key = SpriteCacheKey.ForFrame(new SpriteKey(bulb.ContentKey, slot, NormalizeFlavor(bulb, slot, flavor), frameIndex, scale, style));
        SpriteCacheEntry entry = GetOrCreate(key, persist, (bulb, slot, flavor, frameIndex, scale, style), static request =>
            SpriteCacheEntry.ForFrame(PixelArtScaler.Scale(
                request.bulb.GetCell(request.slot, request.flavor, request.frameIndex).Image, request.scale, request.style)));
        return entry.Frame!;
    }

    /// <summary>Returns a halo, baking it on a miss; the halo of a prefetched (desktop) light bulb is also stored on disk.</summary>
    private GlowSprite? GetHalo(IBulb bulb, CellSlot slot, int flavor, double scale)
    {
        Validate(bulb, slot, flavor, scale);
        BulbAnimationInfo animation = bulb.GetAnimation(slot, flavor);
        if (animation.Kind != BulbAnimationKind.LightBulb)
        {
            return null;
        }

        SpriteCacheKey key = GlowKey(bulb, slot, flavor, scale);
        bool persist = desktopHalos.ContainsKey(key);
        SpriteCacheEntry entry = GetOrCreate(key, persist, (provider: this, bulb, slot, flavor, animation, scale), static request =>
        {
            PremultipliedImage lit = request.provider.GetFrame(
                request.bulb, request.slot, request.flavor, request.animation.LitFrame, request.scale, SpriteStyle.Smooth, persist: false);
            PremultipliedImage unlit = request.provider.GetFrame(
                request.bulb, request.slot, request.flavor, request.animation.UnlitFrame, request.scale, SpriteStyle.Smooth, persist: false);
            bool bakeable = lit.Size == unlit.Size && lit.Width > 0 && lit.Height > 0;
            return SpriteCacheEntry.ForGlow(bakeable ? GlowBaker.Bake(lit, unlit, GlowBaker.SigmaFor(lit.Size, request.scale)) : null);
        });
        return entry.Glow;
    }

    /// <summary>
    /// Returns the cached entry, else produces it (see <see cref="Produce"/>). With <paramref name="persist"/> the entry
    /// is also written to disk unless it is there already, by one thread however many ask at once. A hit allocates nothing.
    /// </summary>
    private SpriteCacheEntry GetOrCreate<TState>(SpriteCacheKey key, bool persist, TState state, Func<TState, SpriteCacheEntry> produce)
    {
        if (!memory.TryGet(key, out SpriteCacheEntry? entry))
        {
            entry = Produce(key, state, produce);
        }

        if (persist && !entry.IsPersisted && entry.TryBeginWrite())
        {
            try
            {
                if (!entry.IsPersisted)
                {
                    disk.TryWrite(key, entry);
                }
            }
            finally
            {
                entry.EndWrite();
            }
        }

        return entry;
    }

    /// <summary>Reads the entry from disk or produces it, once however many threads ask at the same time, and caches it.</summary>
    private SpriteCacheEntry Produce<TState>(SpriteCacheKey key, TState state, Func<TState, SpriteCacheEntry> produce)
    {
        Lazy<SpriteCacheEntry> work = pending.GetOrAdd(
            key,
            static (k, args) => new Lazy<SpriteCacheEntry>(
                () => args.Provider.memory.TryGet(k, out SpriteCacheEntry? cached) ? cached
                    : args.Provider.disk.TryRead(k, out SpriteCacheEntry? stored) ? stored
                    : args.Produce(args.State),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (Provider: this, State: state, Produce: produce));
        try
        {
            SpriteCacheEntry entry = work.Value;
            memory.Add(key, entry);
            return entry;
        }
        finally
        {
            pending.TryRemove(new KeyValuePair<SpriteCacheKey, Lazy<SpriteCacheEntry>>(key, work));
        }
    }

    private static void Validate(IBulb bulb, CellSlot slot, int flavor, double scale)
    {
        ArgumentNullException.ThrowIfNull(bulb);
        if (slot is < CellSlot.Top or > CellSlot.Preview)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(flavor);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale must be a positive finite number.");
        }
    }

    private static SpriteCacheKey GlowKey(IBulb bulb, CellSlot slot, int flavor, double scale) =>
        SpriteCacheKey.ForGlow(bulb.ContentKey, slot, NormalizeFlavor(bulb, slot, flavor), scale);

    /// <summary>The 5.4 flavor rule: sides take the flavor modulo their flavor count; corners and the preview have one.</summary>
    private static int NormalizeFlavor(IBulb bulb, CellSlot slot, int flavor) =>
        slot.IsSide() ? flavor % Math.Max(1, bulb.GetFlavorCount(slot.ToSide())) : 0;
}
