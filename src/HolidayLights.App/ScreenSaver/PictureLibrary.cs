using System.IO;
using HolidayLights.App.ScreenSaver.Pictures;

namespace HolidayLights.App.ScreenSaver;

/// <summary>
/// The 11 bundled pictures and My Pictures (watched), hidden bundled pictures, picture decoding (see
/// <see cref="IPictureLibrary"/>). Owner: screensaver.
/// </summary>
/// <remarks>
/// Queries are thread-safe (immutable snapshots); mutating members run on the UI thread. <see cref="Changed"/> is raised on
/// the thread that changed the list (a thread-pool thread for changes found by the folder watcher).
/// </remarks>
public sealed class PictureLibrary : IPictureLibrary, IDisposable
{
    private const string LogSource = "ScreenSaver.Pictures";

    /// <summary>Folder events are collected for this long before My Pictures is scanned again.</summary>
    private static readonly TimeSpan RescanDelay = TimeSpan.FromMilliseconds(300);

    private readonly DataPaths paths;
    private readonly ISettingsStore settings;
    private readonly IHoldingFolder holding;
    private readonly IAppLog log;
    private readonly Lock gate = new();
    private readonly Timer rescanTimer;
    private IReadOnlyList<PictureInfo> bundled = [];
    private IReadOnlyList<PictureInfo> user = [];
    private Catalog catalog = Catalog.Empty;
    private HolidayLights.Core.Bulbs.Catalog.FolderWatcher? watcher;
    private bool started;
    private volatile bool disposed;

    /// <summary>Creates the library; call <see cref="Start"/>.</summary>
    /// <param name="paths">Bundled and My Pictures folders.</param>
    /// <param name="settings">Hidden bundled pictures (<c>pictures.hidden</c>; read and updated).</param>
    /// <param name="holding">Where removed pictures go.</param>
    /// <param name="log">The log.</param>
    public PictureLibrary(DataPaths paths, ISettingsStore settings, IHoldingFolder holding, IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(holding);
        ArgumentNullException.ThrowIfNull(log);
        this.paths = paths;
        this.settings = settings;
        this.holding = holding;
        this.log = log;
        rescanTimer = new Timer(_ => RescanMyPictures(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<PictureInfo> Pictures => Volatile.Read(ref catalog).Listed;

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
            bundled = PictureFiles.Scan(paths.BundledPicturesFolder, MediaOrigin.Bundled);
            user = PictureFiles.Scan(paths.MyPicturesFolder, MediaOrigin.User);
        }

        settings.Changed += OnSettingsChanged;
        StartWatching();
        Publish();
        log.Info(LogSource, $"Screen saver pictures: {bundled.Count} bundled, {user.Count} in My Pictures.");
    }

    /// <inheritdoc />
    public bool TryGetPicture(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PictureInfo? picture)
    {
        if (Volatile.Read(ref catalog).ById.TryGetValue(id, out picture) && File.Exists(picture.FilePath))
        {
            return true;
        }

        picture = null;
        return false;
    }

    /// <inheritdoc />
    public Rgba32Image? LoadImage(string id)
    {
        if (!TryGetPicture(id, out PictureInfo? picture))
        {
            return null;
        }

        try
        {
            return PictureDecoder.Decode(File.ReadAllBytes(picture.FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log.Warn(LogSource, $"The picture {picture.Title} can't be read; the screen saver runs without it.", ex);
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string folder;
        try
        {
            folder = DataPaths.EnsureFolder(this.paths.MyPicturesFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [.. paths.Select(p => new MediaImportResult(p, MediaImportOutcome.Failed, null, ex.Message))];
        }

        var importer = new PictureImporter(folder, Pictures);
        List<MediaImportResult> results = [.. paths.Select(importer.Add)];
        int added = results.Count(r => r.Outcome is MediaImportOutcome.Added or MediaImportOutcome.Renamed);
        if (added > 0)
        {
            RescanMyPictures();
            log.Info(LogSource, $"Added {added} picture(s) to My Pictures.");
        }

        return results;
    }

    /// <inheritdoc />
    public HeldItem? Remove(string id)
    {
        if (!Volatile.Read(ref catalog).ById.TryGetValue(id, out PictureInfo? picture))
        {
            log.Warn(LogSource, "Remove Picture asked for a picture that is not known.");
            return null;
        }

        if (picture.Origin == MediaOrigin.Bundled)
        {
            settings.Update(
                s => s.Pictures.Hidden.Contains(picture.Id, MediaIds.Comparer)
                    ? s
                    : s with { Pictures = s.Pictures with { Hidden = [.. s.Pictures.Hidden, picture.Id] } },
                SettingsChange.Edit($"Remove {picture.Title}"));
            Publish();
            return null;
        }

        HeldItem held = holding.Hold(picture.FilePath);
        RescanMyPictures();
        return held;
    }

    /// <inheritdoc />
    public void Restore(HeldItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        holding.Restore(item);
        RescanMyPictures();
    }

    /// <inheritdoc />
    public void RestoreHiddenPictures()
    {
        settings.Update(
            s => s.Pictures.Hidden.Count == 0 ? s : s with { Pictures = s.Pictures with { Hidden = [] } },
            SettingsChange.Edit("Restore Removed Pictures"));
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
    /// Watches My Pictures, or while it does not exist yet its nearest parent for it to appear: the folder is created on
    /// first use (PRODUCT-SPEC 6.10), never just to be watched (review r1 #55).
    /// </summary>
    private void StartWatching()
    {
        try
        {
            watcher = new HolidayLights.Core.Bulbs.Catalog.FolderWatcher(paths.MyPicturesFolder, _ => true, TimeSpan.Zero, ScheduleRescan);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            log.Warn(LogSource, "My Pictures can't be watched; pictures copied in by hand appear after a restart.", ex);
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
        if (!e.OldSettings.Pictures.Hidden.SequenceEqual(e.NewSettings.Pictures.Hidden, MediaIds.Comparer))
        {
            Publish();
        }
    }

    private void RescanMyPictures()
    {
        if (disposed)
        {
            return;
        }

        IReadOnlyList<PictureInfo> scanned = PictureFiles.Scan(paths.MyPicturesFolder, MediaOrigin.User);
        lock (gate)
        {
            user = scanned;
        }

        Publish();
    }

    /// <summary>Rebuilds the list and raises <see cref="Changed"/> when it differs.</summary>
    private void Publish()
    {
        lock (gate)
        {
            var hidden = new HashSet<string>(settings.Current.Pictures.Hidden, MediaIds.Comparer);
            Catalog next = Catalog.Build(bundled, user, hidden);
            if (next.Listed.SequenceEqual(catalog.Listed) && next.ById.Count == catalog.ById.Count)
            {
                return;
            }

            Volatile.Write(ref catalog, next);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>One consistent view of the pictures: the listed ones in order, and every known one (hidden included) by id.</summary>
    private sealed record Catalog(IReadOnlyList<PictureInfo> Listed, IReadOnlyDictionary<string, PictureInfo> ById)
    {
        public static Catalog Empty { get; } = new([], new Dictionary<string, PictureInfo>(MediaIds.Comparer));

        /// <summary>Bundled A-Z (hidden ones left out), then My Pictures A-Z.</summary>
        public static Catalog Build(IReadOnlyList<PictureInfo> bundled, IReadOnlyList<PictureInfo> user, HashSet<string> hidden)
        {
            var byId = new Dictionary<string, PictureInfo>(MediaIds.Comparer);
            var listed = new List<PictureInfo>(bundled.Count + user.Count);
            foreach (PictureInfo picture in bundled.Concat(user))
            {
                if (byId.TryAdd(picture.Id, picture) && !(picture.Origin == MediaOrigin.Bundled && hidden.Contains(picture.Id)))
                {
                    listed.Add(picture);
                }
            }

            return new Catalog(listed, byId);
        }
    }
}
