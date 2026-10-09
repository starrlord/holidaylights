using HolidayLights.Core.Bulbs.Catalog;

namespace HolidayLights.Core.Bulbs;

/// <summary>The bulb catalog: built-ins, bundled add-ons and My Bulbs (see <see cref="IBulbCatalog"/>). Owner: core-bulbs.</summary>
/// <remarks>
/// <para>State lives in one lock; every change publishes an immutable <see cref="CatalogSnapshot"/> that queries read
/// without locking. <see cref="Changed"/> events are delivered in order on a thread-pool thread.</para>
/// <para>Add-on bulbs are created on demand from their files (the arrangement can use a bulb before indexing reaches
/// it); the facts of their animations come from the index when it has them.</para>
/// </remarks>
public sealed partial class BulbCatalog : IBulbCatalog, IDisposable
{
    private const string LogSource = "Bulbs.Catalog";
    private const string BulExtension = ".bul";

    private readonly DataPaths paths;
    private readonly ISettingsStore settings;
    private readonly IHoldingFolder holding;
    private readonly IAppLog log;
    private readonly Func<string> authorName;
    private readonly TimeProvider time;
    private readonly IGifBulbWriter gifWriter;
    private readonly DecodedArtCache artCache;
    private readonly BulbIndexStore indexStore;
    private readonly CatalogEventQueue events;
    private readonly CancellationTokenSource lifetime = new();

    private readonly object gate = new();
    private readonly Dictionary<string, CatalogEntry> entries = new(BulbIds.Comparer);
    private readonly Dictionary<string, DamagedFile> damaged = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IBulb> instances = new(BulbIds.Comparer);
    private CatalogSnapshot snapshot = CatalogSnapshot.Empty;
    private bool disposed;

    /// <summary>Creates the catalog; call <see cref="StartAsync"/> to load.</summary>
    /// <param name="paths">Bundled and user folders, the index cache file.</param>
    /// <param name="settings">Favorites, hidden bulbs and category overrides (read; updated by the mutating members).</param>
    /// <param name="holding">The holding folder for removed My Bulbs files.</param>
    /// <param name="log">The log.</param>
    public BulbCatalog(DataPaths paths, ISettingsStore settings, IHoldingFolder holding, IAppLog log)
        : this(paths, settings, holding, log, () => Environment.UserName)
    {
    }

    /// <summary>Creates the catalog with the name a bulb made from a GIF is credited to.</summary>
    /// <param name="paths">Bundled and user folders, the index cache file.</param>
    /// <param name="settings">Favorites, hidden bulbs and category overrides.</param>
    /// <param name="holding">The holding folder for removed My Bulbs files.</param>
    /// <param name="log">The log.</param>
    /// <param name="authorName">The Windows account display name (<see cref="ISystemInfo.UserDisplayName"/>), asked when a GIF is added.</param>
    public BulbCatalog(DataPaths paths, ISettingsStore settings, IHoldingFolder holding, IAppLog log, Func<string> authorName)
        : this(paths, settings, holding, log, authorName, TimeProvider.System, new BulbFactoryGifWriter(), DecodedArtCache.Shared)
    {
    }

    /// <summary>Creates the catalog with every collaborator explicit (tests).</summary>
    internal BulbCatalog(
        DataPaths paths,
        ISettingsStore settings,
        IHoldingFolder holding,
        IAppLog log,
        Func<string> authorName,
        TimeProvider time,
        IGifBulbWriter gifWriter,
        DecodedArtCache artCache)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(holding);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(authorName);
        this.paths = paths;
        this.settings = settings;
        this.holding = holding;
        this.log = log;
        this.authorName = authorName;
        this.time = time;
        this.gifWriter = gifWriter;
        this.artCache = artCache;
        indexStore = new BulbIndexStore(paths.BulbIndexFile, log);
        events = new CatalogEventQueue(args => Changed?.Invoke(this, args), log);
        saveTimer = new Timer(_ => SaveIndex());
        settings.Changed += OnSettingsChanged;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delivered in order on a thread-pool thread. Besides the documented changes, <see cref="BulbCatalogChange.Updated"/>
    /// with no ids means that only <see cref="DamagedFiles"/> changed.
    /// </remarks>
    public event EventHandler<BulbCatalogChangedEventArgs>? Changed;

    /// <inheritdoc />
    public BulbCatalogStatus Status => Current.Status;

    /// <inheritdoc />
    public IReadOnlyList<BulbInfo> All => Current.Listed;

    /// <inheritdoc />
    public IReadOnlyList<DamagedBulbFile> DamagedFiles => Current.Damaged;

    private CatalogSnapshot Current => Volatile.Read(ref snapshot);

    /// <inheritdoc />
    public bool TryGetBulb(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBulb? bulb)
    {
        bulb = null;
        if (!BulbIds.TryGetOrigin(id, out BulbOrigin origin))
        {
            return false;
        }

        if (origin == BulbOrigin.BuiltIn)
        {
            bool found = BuiltInBulbs.TryGet(id, out BuiltInBulb? builtIn);
            bulb = builtIn;
            return found;
        }

        CatalogEntry? entry;
        IBulb? cached;
        lock (gate)
        {
            entries.TryGetValue(id, out entry);
            instances.TryGetValue(id, out cached);
        }

        if (cached is not null && entry is not null && cached.ContentKey == entry.ContentKey)
        {
            bulb = cached;
            return true;
        }

        string? path = entry?.FilePath ?? ConventionalPath(origin, id);
        if (path is null || (entry is null && IsKnownDamaged(path)) || CreateBulb(entry?.Id ?? id, origin, path, entry, cached) is not { } created)
        {
            return false;
        }

        lock (gate)
        {
            if (instances.TryGetValue(id, out IBulb? other) && other.ContentKey == created.ContentKey)
            {
                created = other;
            }
            else
            {
                instances[id] = created;
            }
        }

        bulb = created;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetInfo(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BulbInfo? info) =>
        Current.TryGetInfo(id, out info);

    /// <summary>
    /// Gives back what browsing the bulbs cached (PRODUCT-SPEC 5.14 row 2; review r1 #19): the bulb objects, each with its
    /// whole file, and the decoded art. Whoever still uses a bulb keeps it; anything asked for later is read again.
    /// </summary>
    public void TrimMemory()
    {
        lock (gate)
        {
            instances.Clear();
        }

        artCache.Clear();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Removed (hidden) bulbs appear only under <see cref="BulbFilter.Removed"/>; every other filter, "In Use" and
    /// "Favorites" included, lists bulbs that are not removed (PRODUCT-SPEC 3.2.5: the Bulb List holds every bulb that
    /// is not removed).
    /// </remarks>
    public IReadOnlyList<BulbInfo> Query(BulbQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Current.Query(query);
    }

    /// <inheritdoc />
    public IReadOnlyList<BulbCategoryCount> GetCategoryCounts() => Current.CategoryCounts;

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllCategoryNames() => Current.CategoryNames;

    /// <summary>Stops indexing and the folder watcher.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        settings.Changed -= OnSettingsChanged;
        lifetime.Cancel();
        StopWatcher();
        WaitForIndexingToStop();
        saveTimer.Dispose();
        SaveIndex();
        events.Dispose();
        lifetime.Dispose();
    }

    /// <summary>Publishes a new snapshot of the current state (call inside the lock).</summary>
    private void PublishSnapshot()
    {
        BulbCatalogStatus status = indexComplete ? CompletedStatus() : progress;
        DamagedBulbFile[] damagedFiles = [.. damaged.Values
            .OrderBy(d => d.Stamp.Path, StringComparer.OrdinalIgnoreCase)
            .Select(d => new DamagedBulbFile(d.Stamp.Path, d.Reason))];
        Volatile.Write(ref snapshot, CatalogSnapshot.Build([.. entries.Values], settings.Current.Bulbs, damagedFiles, status));
    }

    private BulbCatalogStatus CompletedStatus()
    {
        int addOns = entries.Values.Count(e => e.Origin != BulbOrigin.BuiltIn) + damaged.Count;
        return new BulbCatalogStatus(addOns, addOns, true);
    }

    /// <summary>Replaces what is known about one file (call inside the lock).</summary>
    private void Apply(IndexedFile file)
    {
        if (file.Entry is { } entry)
        {
            entries[file.Id] = entry;
            damaged.Remove(file.Stamp.Path);
        }
        else
        {
            entries.Remove(file.Id);
            damaged[file.Stamp.Path] = new DamagedFile(file.Origin, file.Stamp, file.Reason ?? "");
        }

        if (instances.TryGetValue(file.Id, out IBulb? instance) && instance.ContentKey != file.Entry?.ContentKey)
        {
            instances.Remove(file.Id);
        }
    }

    private bool IsKnownDamaged(string path)
    {
        lock (gate)
        {
            return damaged.ContainsKey(path);
        }
    }

    /// <summary>Loads an add-on bulb from its file, reusing the index facts when they describe the same file.</summary>
    private IBulb? CreateBulb(string id, BulbOrigin origin, string path, CatalogEntry? entry, IBulb? cached)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            string key = CatalogEntry.ContentKeyFor(id, info.Length, info.LastWriteTimeUtc);
            if (cached?.ContentKey == key)
            {
                return cached;
            }

            BulFile file = BulFile.Read(path);
            if (file.IsDamaged)
            {
                return null;
            }

            IReadOnlyDictionary<int, AnimationFacts>? facts = entry?.ContentKey == key ? entry.Facts : null;
            return new AddOnBulb(file, id, origin, key, facts, artCache);
        }
        catch (IOException e)
        {
            log.Warn(LogSource, $"Bulb file {Path.GetFileName(path)} could not be read.", e);
            return null;
        }
    }

    /// <summary>The file an add-on id names by convention (<c>addon:</c> in the bundled folder, <c>user:</c> in My Bulbs).</summary>
    private string? ConventionalPath(BulbOrigin origin, string id)
    {
        string stem = BulbIds.GetKey(id);
        if (stem is "." or ".." || stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        string folder = origin == BulbOrigin.BundledAddOn ? paths.BundledBulbsFolder : paths.MyBulbsFolder;
        return Path.Combine(folder, stem + BulExtension);
    }

    private static string IdFor(BulbOrigin origin, string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        return origin == BulbOrigin.BundledAddOn ? BulbIds.AddOn(stem) : BulbIds.User(stem);
    }

    /// <summary>A file that cannot be loaded.</summary>
    private sealed record DamagedFile(BulbOrigin Origin, FileStamp Stamp, string Reason);

    /// <summary>The outcome of reading one file: its entry, or why it is damaged.</summary>
    private sealed record IndexedFile(BulbOrigin Origin, FileStamp Stamp, string Id, CatalogEntry? Entry, string? Reason, bool FromCache);
}
