namespace HolidayLights.Core.Bulbs.Catalog;

/// <summary>
/// Watches a folder for changes to files of one extension and reports them once things settle. The folder may not exist
/// yet ("created on first use", PRODUCT-SPEC 6.10): until it does, the nearest existing parent is watched for it to
/// appear, and the folder being deleted or renamed is noticed the same way.
/// </summary>
/// <remarks>
/// The callback runs on a thread-pool thread, never twice at the same time. Also used for My Music and My Pictures, so that
/// none of the "created on first use" folders is created just to be watched (review r1 #55).
/// </remarks>
public sealed class FolderWatcher : IDisposable
{
    private readonly string folder;
    private readonly Func<string, bool> isWatchedFile;
    private readonly Action changed;
    private readonly Timer debounce;
    private readonly TimeSpan delay;
    private readonly object gate = new();
    private readonly SemaphoreSlim running = new(1, 1);
    private FileSystemWatcher? contents;
    private FileSystemWatcher? ancestor;
    private bool disposed;

    /// <summary>Starts watching.</summary>
    /// <param name="folder">The folder to watch.</param>
    /// <param name="extension">The file extension, with its dot (e.g. ".bul").</param>
    /// <param name="delay">Quiet time before <paramref name="changed"/> runs.</param>
    /// <param name="changed">Called after changes (and when the folder appears or disappears).</param>
    public FolderWatcher(string folder, string extension, TimeSpan delay, Action changed)
        : this(folder, path => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase), delay, changed)
    {
    }

    /// <summary>Starts watching for changes to the files a predicate selects.</summary>
    /// <param name="folder">The folder to watch (it need not exist).</param>
    /// <param name="isWatchedFile">Tells whether a changed path concerns the owner (every file: <c>_ =&gt; true</c>).</param>
    /// <param name="delay">Quiet time before <paramref name="changed"/> runs.</param>
    /// <param name="changed">Called after changes (and when the folder appears or disappears).</param>
    public FolderWatcher(string folder, Func<string, bool> isWatchedFile, TimeSpan delay, Action changed)
    {
        ArgumentNullException.ThrowIfNull(isWatchedFile);
        this.folder = Path.GetFullPath(folder);
        this.isWatchedFile = isWatchedFile;
        this.delay = delay;
        this.changed = changed;
        debounce = new Timer(_ => Fire());
        Arm();
    }

    /// <summary>Stops watching.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            contents?.Dispose();
            ancestor?.Dispose();
            contents = null;
            ancestor = null;
        }

        debounce.Dispose();
    }

    /// <summary>(Re)creates the watchers for the current state of the folder and its parents.</summary>
    /// <remarks>
    /// The folder can appear (or vanish) between looking for it and the new watchers starting, and its creation event is
    /// then lost; arming repeats until what it saw still holds, a few times at most.
    /// </remarks>
    private void Arm()
    {
        const int MaxAttempts = 5;
        lock (gate)
        {
            for (int attempt = 0; attempt < MaxAttempts && !disposed; attempt++)
            {
                contents?.Dispose();
                ancestor?.Dispose();
                bool exists = Directory.Exists(folder);
                contents = exists
                    ? TryWatch(folder, NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime, OnContentsChanged)
                    : null;
                string? parent = Path.GetDirectoryName(folder);
                while (parent is not null && !Directory.Exists(parent))
                {
                    parent = Path.GetDirectoryName(parent);
                }

                // Directory names only: the folder (or a missing parent of it) being created, deleted or renamed.
                ancestor = parent is null ? null : TryWatch(parent, NotifyFilters.DirectoryName, OnAncestorChanged);
                if ((contents is not null) == Directory.Exists(folder))
                {
                    return;
                }
            }
        }
    }

    /// <summary>Starts a watcher, or returns null when the folder is gone again.</summary>
    private FileSystemWatcher? TryWatch(string path, NotifyFilters filter, FileSystemEventHandler handler)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(path) { NotifyFilter = filter };
            watcher.Created += handler;
            watcher.Changed += handler;
            watcher.Deleted += handler;
            watcher.Renamed += (sender, e) => handler(sender, e);
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            watcher?.Dispose();
            return null;
        }
    }

    private void OnContentsChanged(object sender, FileSystemEventArgs e)
    {
        bool relevant = HasExtension(e.FullPath) || (e is RenamedEventArgs renamed && HasExtension(renamed.OldFullPath));
        if (relevant)
        {
            Schedule();
        }
    }

    private void OnAncestorChanged(object sender, FileSystemEventArgs e)
    {
        bool onPath = IsOnPath(e.FullPath) || (e is RenamedEventArgs renamed && IsOnPath(renamed.OldFullPath));
        if (onPath)
        {
            Arm();
            Schedule();
        }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow or the watched folder went away: start over and let the owner rescan.
        Arm();
        Schedule();
    }

    private void Schedule()
    {
        lock (gate)
        {
            if (!disposed)
            {
                debounce.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Fire()
    {
        if (!running.Wait(0))
        {
            Schedule();
            return;
        }

        try
        {
            changed();
        }
        finally
        {
            running.Release();
        }
    }

    private bool HasExtension(string path) => isWatchedFile(path);

    /// <summary>True when a directory event concerns the watched folder or one of its parents.</summary>
    private bool IsOnPath(string path) =>
        folder.Equals(path, StringComparison.OrdinalIgnoreCase)
        || folder.StartsWith(path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
