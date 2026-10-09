using System.Diagnostics;

namespace HolidayLights.Audio.Library;

/// <summary>
/// The bundled songs and My Music (watched), with the 6.1.4 credits and category chips, <c>.lnk</c> shortcuts, hidden
/// bundled songs and the 5.4 sort order (see <see cref="ISongLibrary"/>). Owner: audio.
/// </summary>
/// <remarks>
/// Queries are thread-safe (immutable snapshots). Mutating members run on the UI thread. Lengths, playability and
/// artist tags are read on a background thread after each scan; <see cref="Changed"/> follows when they arrive.
/// </remarks>
public sealed class MusicLibrary : ISongLibrary, IDisposable
{
    private const string LogSource = "Audio.Library";

    /// <summary>Folder events are collected for this long before My Music is scanned again.</summary>
    private static readonly TimeSpan RescanDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>While probing many files, the list is refreshed at most this often.</summary>
    private static readonly TimeSpan ProbePublishInterval = TimeSpan.FromMilliseconds(500);

    private static readonly IReadOnlyList<SongCategory> NoCategories = [];

    private readonly DataPaths paths;
    private readonly ISettingsStore settings;
    private readonly IHoldingFolder holding;
    private readonly IShellOperations shell;
    private readonly IAppLog log;
    private readonly SongProbe probe = new();
    private readonly Lock gate = new();
    private readonly Timer rescanTimer;
    private IReadOnlyList<SongFile> bundledFiles = [];
    private IReadOnlyList<SongFile> userFiles = [];
    private Catalog catalog = Catalog.Empty;
    private HolidayLights.Core.Bulbs.Catalog.FolderWatcher? watcher;
    private int probeRunning;
    private int probeRequested;
    private bool started;
    private volatile bool disposed;

    /// <summary>Creates the library; call <see cref="Start"/>.</summary>
    /// <param name="paths">Bundled and My Music folders.</param>
    /// <param name="settings">Hidden bundled songs (<c>songs.hidden</c>; read and updated).</param>
    /// <param name="holding">Where removed songs go.</param>
    /// <param name="shell">Resolves <c>.lnk</c> shortcuts.</param>
    /// <param name="log">The log.</param>
    public MusicLibrary(DataPaths paths, ISettingsStore settings, IHoldingFolder holding, IShellOperations shell, IAppLog log)
    {
        this.paths = paths;
        this.settings = settings;
        this.holding = holding;
        this.shell = shell;
        this.log = log;
        rescanTimer = new Timer(_ => RescanMyMusic(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<SongInfo> Songs => Volatile.Read(ref catalog).Songs;

    /// <inheritdoc />
    public IReadOnlyList<SongInfo> HiddenSongs => Volatile.Read(ref catalog).Hidden;

    /// <inheritdoc />
    public void Start()
    {
        lock (gate)
        {
            if (started || disposed)
            {
                return;
            }

            started = true;
        }

        IReadOnlyList<SongFile> bundled = MusicFolderScanner.Scan(paths.BundledMusicFolder, MediaOrigin.Bundled, shell, log);
        // My Music is created on first use (PRODUCT-SPEC 6.10): a missing folder simply has no songs and is watched for.
        IReadOnlyList<SongFile> user = MusicFolderScanner.Scan(paths.MyMusicFolder, MediaOrigin.User, shell, log);
        lock (gate)
        {
            bundledFiles = bundled;
            userFiles = user;
        }

        settings.Changed += OnSettingsChanged;
        StartWatching();
        Publish();
        log.Info(LogSource, $"Music Box: {bundled.Count} bundled songs, {user.Count} songs in My Music.");
        ScheduleProbe();
    }

    /// <inheritdoc />
    public bool TryGetSong(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SongInfo? song) =>
        Volatile.Read(ref catalog).ById.TryGetValue(id, out song);

    /// <inheritdoc />
    public IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string folder = DataPaths.EnsureFolder(this.paths.MyMusicFolder);
        var importer = new SongImporter(folder, Songs, shell);
        var results = paths.Select(importer.Add).ToList();
        var added = results.Where(r => (r.Outcome is MediaImportOutcome.Added or MediaImportOutcome.Renamed) && r.Id is not null)
            .Select(r => r.Id!)
            .ToHashSet(MediaIds.Comparer);
        if (added.Count > 0)
        {
            RescanMyMusic();
            MarkChecked(added);
            log.Info(LogSource, $"Added {added.Count} song(s) to My Music.");
        }

        return results;
    }

    /// <inheritdoc />
    public HeldItem? Remove(string id)
    {
        if (!TryGetSong(id, out SongInfo? song))
        {
            log.Warn(LogSource, "Remove Song asked for a song that is not known.");
            return null;
        }

        if (song.Origin == MediaOrigin.Bundled)
        {
            settings.Update(
                s => s.Songs.Hidden.Contains(song.Id, MediaIds.Comparer) ? s : s with { Songs = s.Songs with { Hidden = [.. s.Songs.Hidden, song.Id] } },
                SettingsChange.Edit($"Remove {song.Title}"));
            Publish();
            return null;
        }

        HeldItem held = holding.Hold(song.FilePath);
        RescanMyMusic();
        return held;
    }

    /// <inheritdoc />
    public void Restore(HeldItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        holding.Restore(item);
        RescanMyMusic();
    }

    /// <inheritdoc />
    public void RestoreHiddenSongs()
    {
        settings.Update(
            s => s.Songs.Hidden.Count == 0 ? s : s with { Songs = s.Songs with { Hidden = [] } },
            SettingsChange.Edit("Restore Removed Songs"));
        Publish();
    }

    /// <summary>Stops the folder watcher.</summary>
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
        watcher?.Dispose();
        rescanTimer.Dispose();
    }

    /// <summary>
    /// Watches My Music, or while it does not exist yet its nearest parent for it to appear, so the folder is never created
    /// just to be watched (review r1 #55).
    /// </summary>
    private void StartWatching()
    {
        try
        {
            watcher = new HolidayLights.Core.Bulbs.Catalog.FolderWatcher(paths.MyMusicFolder, _ => true, TimeSpan.Zero, ScheduleRescan);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            log.Warn(LogSource, "My Music can't be watched; songs copied in by hand appear after a restart.", ex);
        }
    }

    private void ScheduleRescan()
    {
        if (!disposed)
        {
            try
            {
                rescanTimer.Change(RescanDelay, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Disposed meanwhile.
            }
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!e.OldSettings.Songs.Hidden.SequenceEqual(e.NewSettings.Songs.Hidden, MediaIds.Comparer))
        {
            Publish();
        }
    }

    /// <summary>Scans My Music again (watcher, or after a change made here).</summary>
    private void RescanMyMusic()
    {
        if (disposed)
        {
            return;
        }

        IReadOnlyList<SongFile> user = MusicFolderScanner.Scan(paths.MyMusicFolder, MediaOrigin.User, shell, log);
        lock (gate)
        {
            userFiles = user;
        }

        Publish();
        ScheduleProbe();
    }

    /// <summary>New songs start checked (5.4 "Disabled Music"): a re-added song loses a stale unchecked entry.</summary>
    private void MarkChecked(IReadOnlySet<string> ids)
    {
        if (settings.Current.Current.Music.DisabledSongs.Any(ids.Contains))
        {
            settings.Update(
                s => s with
                {
                    Current = s.Current with
                    {
                        Music = s.Current.Music with { DisabledSongs = s.Current.Music.DisabledSongs.Where(d => !ids.Contains(d)).ToList() },
                    },
                },
                SettingsChange.Internal);
        }
    }

    /// <summary>Rebuilds the lists and raises <see cref="Changed"/> when they differ.</summary>
    private void Publish()
    {
        lock (gate)
        {
            var hidden = new HashSet<string>(settings.Current.Songs.Hidden, MediaIds.Comparer);
            Catalog next = Catalog.Build(bundledFiles, userFiles, hidden, probe);
            if (next.SameAs(catalog))
            {
                return;
            }

            Volatile.Write(ref catalog, next);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleProbe()
    {
        Volatile.Write(ref probeRequested, 1);
        if (Interlocked.CompareExchange(ref probeRunning, 1, 0) == 0)
        {
            _ = Task.Run(ProbeLoop);
        }
    }

    /// <summary>Probes every file not probed in its current state, refreshing the lists as results arrive.</summary>
    private void ProbeLoop()
    {
        try
        {
            while (!disposed && Interlocked.Exchange(ref probeRequested, 0) == 1)
            {
                SongFile[] files;
                lock (gate)
                {
                    files = [.. bundledFiles, .. userFiles];
                }

                var sincePublish = Stopwatch.StartNew();
                bool pending = false;
                foreach (SongFile file in files)
                {
                    if (disposed)
                    {
                        return;
                    }

                    if (!probe.TryGetCached(file, out _))
                    {
                        probe.Probe(file);
                        pending = true;
                    }

                    if (pending && sincePublish.Elapsed >= ProbePublishInterval)
                    {
                        Publish();
                        pending = false;
                        sincePublish.Restart();
                    }
                }

                if (pending)
                {
                    Publish();
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.Error(LogSource, "Reading song details failed.", ex);
        }
        finally
        {
            Volatile.Write(ref probeRunning, 0);
        }

        if (!disposed && Volatile.Read(ref probeRequested) == 1)
        {
            ScheduleProbe();
        }
    }

    private static SongInfo Describe(SongFile file, SongProbe probe)
    {
        string title = Path.GetFileNameWithoutExtension(file.FileName);
        probe.TryGetCached(file, out SongDetails? details);
        bool bundled = file.Origin == MediaOrigin.Bundled;
        SongCredit? credit = bundled && SongCredits.TryGet(title, out SongCredit found) ? found : null;
        return new SongInfo
        {
            Id = file.Id,
            Title = title,
            FilePath = file.FilePath,
            ShortcutTarget = file.ShortcutTarget,
            Origin = file.Origin,
            Kind = file.Kind,
            Length = details?.Length,
            Arranger = bundled ? credit?.Arranger : details?.Artist,
            Categories = bundled ? credit?.Categories ?? NoCategories : SongCredits.MySongs,
            IsPlayable = details?.IsPlayable ?? true,
            SortKey = SongFileTypes.SortKey(file.FileName),
        };
    }

    /// <summary>One consistent view of the songs.</summary>
    private sealed record Catalog(IReadOnlyList<SongInfo> Songs, IReadOnlyList<SongInfo> Hidden, IReadOnlyDictionary<string, SongInfo> ById)
    {
        public static Catalog Empty { get; } = new([], [], new Dictionary<string, SongInfo>(MediaIds.Comparer));

        /// <summary>Listed and hidden songs in 5.4 order (sort key, then bundled before yours).</summary>
        public static Catalog Build(IEnumerable<SongFile> bundled, IEnumerable<SongFile> user, HashSet<string> hiddenIds, SongProbe probe)
        {
            var byId = new Dictionary<string, SongInfo>(MediaIds.Comparer);
            var listed = new List<SongInfo>();
            var hidden = new List<SongInfo>();
            foreach (SongFile file in bundled.Concat(user))
            {
                SongInfo song = Describe(file, probe);
                if (byId.TryAdd(song.Id, song))
                {
                    (song.Origin == MediaOrigin.Bundled && hiddenIds.Contains(song.Id) ? hidden : listed).Add(song);
                }
            }

            Comparison<SongInfo> order = (a, b) =>
            {
                int byKey = string.CompareOrdinal(a.SortKey, b.SortKey);
                return byKey != 0 ? byKey : a.Origin != b.Origin ? a.Origin.CompareTo(b.Origin) : string.CompareOrdinal(a.FilePath, b.FilePath);
            };
            listed.Sort(order);
            hidden.Sort(order);
            return new Catalog(listed, hidden, byId);
        }

        public bool SameAs(Catalog other) => Songs.SequenceEqual(other.Songs) && Hidden.SequenceEqual(other.Hidden);
    }
}
