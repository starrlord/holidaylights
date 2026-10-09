using System.Collections.Concurrent;
using System.Diagnostics;
using HolidayLights.Core.Bulbs.Catalog;

namespace HolidayLights.Core.Bulbs;

/// <summary>Indexing of the add-on folders, the index cache and the My Bulbs watcher.</summary>
public sealed partial class BulbCatalog
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan WatcherDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    private readonly HashSet<string> touchedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object saveGate = new();
    private readonly object rescanGate = new();
    private readonly Timer saveTimer;
    private BulbCatalogStatus progress = new(0, 0, false);
    private Task? indexing;
    private bool indexComplete;
    private bool indexDirty;
    private FolderWatcher? watcher;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (indexing is not null)
            {
                return indexing;
            }

            foreach (BuiltInBulb bulb in BuiltInBulbs.All)
            {
                entries[bulb.Id] = CatalogEntry.FromBuiltIn(bulb);
            }

            PublishSnapshot();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            indexing = Task.Run(() => Index(cancellation), CancellationToken.None);
            return indexing;
        }
    }

    /// <summary>Indexes both add-on folders: cached files from the index, the others by reading and decoding them.</summary>
    private void Index(CancellationTokenSource cancellation)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            List<(BulbOrigin Origin, FileStamp Stamp)> sources =
            [
                .. EnumerateFolder(paths.BundledBulbsFolder, BulbOrigin.BundledAddOn) ?? [],
                .. EnumerateFolder(paths.MyBulbsFolder, BulbOrigin.UserAddOn) ?? [],
            ];
            Dictionary<string, BulbIndexRecord> cache = indexStore.Load();
            UpdateProgress(0, sources.Count, final: false, new ConcurrentQueue<IndexedFile>());

            var results = new ConcurrentQueue<IndexedFile>();
            var flushing = new object();
            int processed = 0;
            int reused = 0;
            long lastFlush = 0;
            var options = new ParallelOptions
            {
                CancellationToken = cancellation.Token,
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
            };
            Parallel.ForEach(sources, options, source =>
            {
                IndexedFile result = IndexFile(source.Origin, source.Stamp, cache);
                if (result.FromCache)
                {
                    Interlocked.Increment(ref reused);
                }

                results.Enqueue(result);
                int done = Interlocked.Increment(ref processed);
                if (clock.ElapsedMilliseconds - Interlocked.Read(ref lastFlush) >= ProgressInterval.TotalMilliseconds && Monitor.TryEnter(flushing))
                {
                    try
                    {
                        Interlocked.Exchange(ref lastFlush, clock.ElapsedMilliseconds);
                        UpdateProgress(done, sources.Count, final: false, results);
                    }
                    finally
                    {
                        Monitor.Exit(flushing);
                    }
                }
            });

            lock (flushing)
            {
                bool cacheMatches = reused == sources.Count && cache.Count == sources.Count;
                UpdateProgress(sources.Count, sources.Count, final: true, results, cacheChanged: !cacheMatches);
            }

            log.Info(LogSource, $"Indexed {sources.Count} add-on bulb files in {clock.ElapsedMilliseconds} ms ({reused} from the cache).");
            StartWatcher();
            RescanMyBulbs();
        }
        catch (OperationCanceledException)
        {
            log.Info(LogSource, "Indexing add-on bulbs was cancelled.");
        }
        catch (Exception e)
        {
            log.Error(LogSource, "Indexing add-on bulbs failed.", e);
            UpdateProgress(0, 0, final: true, new ConcurrentQueue<IndexedFile>());
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    /// <summary>Merges finished files into the state and reports progress.</summary>
    private void UpdateProgress(int done, int total, bool final, IProducerConsumerCollection<IndexedFile> results, bool cacheChanged = false)
    {
        var damagedNow = new List<IndexedFile>();
        lock (gate)
        {
            while (results.TryTake(out IndexedFile? file))
            {
                if (touchedPaths.Contains(file.Stamp.Path))
                {
                    continue;
                }

                Apply(file);
                if (file.Entry is null && !file.FromCache)
                {
                    damagedNow.Add(file);
                }
            }

            progress = new BulbCatalogStatus(done, total, final);
            if (final)
            {
                indexComplete = true;
                touchedPaths.Clear();
                indexDirty |= cacheChanged;
            }

            PublishSnapshot();
        }

        foreach (IndexedFile file in damagedNow)
        {
            log.Warn(LogSource, $"Bulb file {Path.GetFileName(file.Stamp.Path)} is damaged: {file.Reason}");
        }

        events.Post(BulbCatalogChange.IndexProgress, []);
        if (final)
        {
            SaveIndex();
        }
    }

    /// <summary>Uses the cache record when it describes the file as it is; otherwise reads and analyzes the file.</summary>
    private static IndexedFile IndexFile(BulbOrigin origin, FileStamp stamp, Dictionary<string, BulbIndexRecord> cache)
    {
        string id = IdFor(origin, stamp.Path);
        if (cache.TryGetValue(BulbIndexRecord.KeyFor(origin, Path.GetFileName(stamp.Path)), out BulbIndexRecord? record) && record.Describes(stamp))
        {
            return record.Damaged is { } reason
                ? new IndexedFile(origin, stamp, id, null, reason, FromCache: true)
                : new IndexedFile(origin, stamp, id, record.ToEntry(id, stamp), null, FromCache: true);
        }

        return LoadFile(origin, stamp);
    }

    /// <summary>Reads a file and decodes its animations.</summary>
    private static IndexedFile LoadFile(BulbOrigin origin, FileStamp stamp)
    {
        string id = IdFor(origin, stamp.Path);
        try
        {
            BulFile file = BulFile.Read(stamp.Path);
            return file.IsDamaged
                ? new IndexedFile(origin, stamp, id, null, string.Join(" ", file.Problems), FromCache: false)
                : new IndexedFile(origin, stamp, id, CatalogEntry.Analyze(file, id, origin, stamp), null, FromCache: false);
        }
        catch (IOException e)
        {
            return new IndexedFile(origin, stamp, id, null, $"The file could not be read: {e.Message}", FromCache: false);
        }
    }

    /// <summary>The <c>.bul</c> files of a folder (hidden and temporary files skipped, as 5.4 did).</summary>
    /// <returns>The files (none for a missing folder), or null when the folder could not be read.</returns>
    private List<(BulbOrigin Origin, FileStamp Stamp)>? EnumerateFolder(string folder, BulbOrigin origin)
    {
        var files = new List<(BulbOrigin, FileStamp)>();
        try
        {
            if (!Directory.Exists(folder))
            {
                return files;
            }

            foreach (FileInfo info in new DirectoryInfo(folder).EnumerateFiles("*" + BulExtension))
            {
                if (info.Extension.Equals(BulExtension, StringComparison.OrdinalIgnoreCase)
                    && (info.Attributes & (FileAttributes.Hidden | FileAttributes.Temporary)) == 0)
                {
                    files.Add((origin, FileStamp.Of(info)));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn(LogSource, $"The {(origin == BulbOrigin.BundledAddOn ? "bundled" : "My Bulbs")} folder could not be read.", e);
            return null;
        }

        return files;
    }

    /// <summary>Waits for indexing (started if needed); returns early when the catalog is shutting down.</summary>
    private void WaitForIndex()
    {
        try
        {
            StartAsync().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Disposed meanwhile: work with what is known.
        }
    }

    private void WaitForIndexingToStop()
    {
        Task? task;
        lock (gate)
        {
            task = indexing;
        }

        try
        {
            task?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancelled or failed; already logged.
        }
    }

    /// <summary>Writes the index cache soon (changes to My Bulbs come in bursts).</summary>
    private void ScheduleIndexSave()
    {
        lock (gate)
        {
            indexDirty = true;
            if (!disposed)
            {
                saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void SaveIndex()
    {
        lock (saveGate)
        {
            List<BulbIndexRecord> records;
            lock (gate)
            {
                if (!indexDirty || !indexComplete)
                {
                    return;
                }

                indexDirty = false;
                records = [.. entries.Values.Where(e => e.Header is not null).Select(BulbIndexRecord.FromEntry),
                           .. damaged.Values.Select(d => BulbIndexRecord.FromDamaged(d.Origin, d.Stamp, d.Reason))];
            }

            indexStore.Save(records);
        }
    }

    private void StartWatcher()
    {
        lock (gate)
        {
            if (watcher is null && !disposed)
            {
                watcher = new FolderWatcher(paths.MyBulbsFolder, BulExtension, WatcherDelay, RescanMyBulbs);
            }
        }
    }

    private void StopWatcher()
    {
        FolderWatcher? stopped;
        lock (gate)
        {
            stopped = watcher;
            watcher = null;
        }

        stopped?.Dispose();
    }

    /// <summary>
    /// Brings the My Bulbs part of the catalog in line with the folder: new and changed files are loaded, missing ones
    /// dropped (files copied in by hand appear live, PRODUCT-SPEC 6.9).
    /// </summary>
    private void RescanMyBulbs()
    {
        lock (rescanGate)
        {
            Dictionary<string, FileStamp> known;
            lock (gate)
            {
                if (disposed || !indexComplete)
                {
                    return;
                }

                known = entries.Values.Where(e => e.Origin == BulbOrigin.UserAddOn)
                    .Select(e => new FileStamp(e.FilePath!, e.FileSize, e.LastWriteUtc!.Value, e.CreatedUtc ?? default))
                    .Concat(damaged.Values.Where(d => d.Origin == BulbOrigin.UserAddOn).Select(d => d.Stamp))
                    .ToDictionary(s => s.Path, StringComparer.OrdinalIgnoreCase);
            }

            // A folder that cannot be read right now says nothing about which files are gone.
            if (EnumerateFolder(paths.MyBulbsFolder, BulbOrigin.UserAddOn) is not { } files)
            {
                return;
            }

            List<IndexedFile> loaded = [.. files
                .Where(f => !known.TryGetValue(f.Stamp.Path, out FileStamp? old) || old.Size != f.Stamp.Size || old.LastWriteUtc != f.Stamp.LastWriteUtc)
                .Select(f => LoadFile(f.Origin, f.Stamp))];
            var present = new HashSet<string>(files.Select(f => f.Stamp.Path), StringComparer.OrdinalIgnoreCase);
            string[] gone = [.. known.Keys.Where(path => !present.Contains(path))];
            if (loaded.Count > 0 || gone.Length > 0)
            {
                CommitFileChanges(loaded, gone);
            }
        }
    }

    /// <summary>Applies loaded and vanished My Bulbs files and reports what the Bulb List gained, changed and lost.</summary>
    private void CommitFileChanges(IReadOnlyList<IndexedFile> loaded, IReadOnlyList<string> gonePaths)
    {
        var added = new List<string>();
        var updated = new List<string>();
        var removed = new List<string>();
        bool damagedChanged = false;
        lock (gate)
        {
            foreach (IndexedFile file in loaded)
            {
                bool wasListed = entries.ContainsKey(file.Id);
                damagedChanged |= file.Entry is null || damaged.ContainsKey(file.Stamp.Path);
                Apply(file);
                if (file.Entry is not null)
                {
                    (wasListed ? updated : added).Add(file.Id);
                }
                else if (wasListed)
                {
                    removed.Add(file.Id);
                }

                if (!indexComplete)
                {
                    touchedPaths.Add(file.Stamp.Path);
                }
            }

            foreach (string path in gonePaths)
            {
                string id = IdFor(BulbOrigin.UserAddOn, path);
                damagedChanged |= damaged.Remove(path);
                if (entries.Remove(id))
                {
                    removed.Add(id);
                }

                instances.Remove(id);
                if (!indexComplete)
                {
                    touchedPaths.Add(path);
                }
            }

            PublishSnapshot();
        }

        foreach (IndexedFile file in loaded.Where(f => f.Entry is null))
        {
            log.Warn(LogSource, $"Bulb file {Path.GetFileName(file.Stamp.Path)} is damaged: {file.Reason}");
        }

        PostChanges(added, updated, removed, damagedChanged);
        ScheduleIndexSave();
    }

    private void PostChanges(List<string> added, List<string> updated, List<string> removed, bool damagedChanged)
    {
        if (added.Count > 0)
        {
            events.Post(BulbCatalogChange.Added, added);
        }

        if (updated.Count > 0)
        {
            events.Post(BulbCatalogChange.Updated, updated);
        }

        if (removed.Count > 0)
        {
            events.Post(BulbCatalogChange.Removed, removed);
        }

        if (damagedChanged && added.Count + updated.Count + removed.Count == 0)
        {
            events.Post(BulbCatalogChange.Updated, []);
        }
    }
}
