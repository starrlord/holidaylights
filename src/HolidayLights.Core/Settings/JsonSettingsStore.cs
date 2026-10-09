namespace HolidayLights.Core.Settings;

/// <summary>
/// <c>settings.json</c> with source-generated JSON, version migrations, sanitizing of out-of-range values, atomic
/// debounced writes and damaged-file recovery (see <see cref="ISettingsStore"/>, PRODUCT-SPEC Appendix C, 5.13).
/// Owner: core-settings.
/// </summary>
/// <remarks>
/// <para>Every update is sanitized (<see cref="AppSettings"/> values out of range are corrected), published at once and
/// written 500 ms after the last change on the thread pool, so the UI thread never waits for the disk. A failed write
/// keeps the change in memory, raises <see cref="SaveFailed"/> (on the synchronization context of the last
/// <see cref="Update"/> caller) and is retried with the next change or <see cref="FlushAsync"/>.</para>
/// <para>Values that cannot be read (a hand edit, a value written by a newer version) take their defaults instead of
/// resetting the whole file; a file whose content cannot be read at all is renamed to
/// <c>settings.damaged-&lt;date&gt;.json</c> (or copied there when another program keeps it from being renamed).</para>
/// <para>Only content counts as damaged. A file that cannot be opened (held by a sync, backup or antivirus tool, in the
/// middle of being replaced, access denied) is retried for <see cref="ReadPatience"/>. After that the defaults are used
/// (outcome <see cref="SettingsLoadOutcome.Unavailable"/>) and
/// the file is <b>never renamed or written while it has not been read</b>. It is read again in the background; once it
/// can be read, its settings replace the defaults (<see cref="Changed"/> with an internal change, on the synchronization
/// context of the loading or updating thread) and the explicit changes made meanwhile (edits, loaded themes, imports,
/// resets) are applied again on top of them and saved.</para>
/// </remarks>
public sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    /// <summary>The delay between the last change and the write (PRODUCT-SPEC Appendix C).</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>How long loading keeps trying to open a settings file that another program holds.</summary>
    internal static readonly TimeSpan ReadPatience = TimeSpan.FromSeconds(3);

    /// <summary>The first pause before a settings file that could not be read is read again (it doubles up to 30 s).</summary>
    internal static readonly TimeSpan FirstRecoveryDelay = TimeSpan.FromSeconds(2);

    private const string LogSource = "Settings";

    private static readonly TimeSpan MaxRecoveryDelay = TimeSpan.FromSeconds(30);

    private readonly string filePath;
    private readonly IAppLog log;
    private readonly SettingsMigrator migrator;
    private readonly object updateGate = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly Timer saveTimer;
    private readonly Timer recoveryTimer;
    private readonly List<Func<AppSettings, AppSettings>> editsWhileUnread = [];
    private volatile AppSettings current;
    private volatile bool fileUnread;
    private long changeNumber;
    private long savedChangeNumber;
    private TimeSpan recoveryDelay;
    private bool unreadWriteLogged;
    private SynchronizationContext? eventContext;
    private volatile bool disposed;

    private JsonSettingsStore(string filePath, AppSettings settings, SettingsLoadOutcome outcome, IAppLog log, SettingsMigrator migrator, bool fileUnread, TimeSpan recoveryDelay)
    {
        this.filePath = filePath;
        this.log = log;
        this.migrator = migrator;
        current = settings;
        LoadOutcome = outcome;
        this.fileUnread = fileUnread;
        this.recoveryDelay = recoveryDelay;
        eventContext = SynchronizationContext.Current;

        // A new or replaced file is written on the first change or flush, even when the settings did not change; a file
        // that could not be read is not written at all until it has been read.
        changeNumber = outcome == SettingsLoadOutcome.Loaded || fileUnread ? 0 : 1;
        saveTimer = new Timer(_ => _ = WritePendingAsync(), null, Timeout.Infinite, Timeout.Infinite);
        recoveryTimer = new Timer(_ => Recover(), null, Timeout.Infinite, Timeout.Infinite);
        if (fileUnread)
        {
            ScheduleRecovery();
        }
    }

    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed;

    /// <inheritdoc />
    public AppSettings Current => current;

    /// <inheritdoc />
    public SettingsLoadOutcome LoadOutcome { get; }

    /// <summary>True while the settings file exists but has not been read yet (it is not written meanwhile).</summary>
    internal bool IsFileUnread => fileUnread;

    /// <summary>
    /// Reads <see cref="DataPaths.SettingsFile"/>. A missing file gives <see cref="SettingsLoadOutcome.Created"/> with
    /// <c>new AppSettings()</c> (not written until the first change or <see cref="FlushAsync"/>; the start-up flow then
    /// applies the newcomer or 5.4-import values with <see cref="Update"/>); a damaged file is renamed to
    /// <c>settings.damaged-&lt;date&gt;.json</c> and gives <see cref="SettingsLoadOutcome.ReplacedDamaged"/>; a file that
    /// cannot be opened for a few seconds gives the defaults and <see cref="SettingsLoadOutcome.Unavailable"/>, and is left
    /// alone and used as soon as it can be read.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <param name="log">The log.</param>
    /// <returns>The store; its events are raised on the thread that calls <see cref="Update"/>.</returns>
    public static JsonSettingsStore Load(DataPaths paths, IAppLog log) => Load(paths, log, SettingsMigrator.Default, DateTime.Now);

    /// <summary>True when <see cref="DataPaths.SettingsFile"/> exists (decides between first run and later starts).</summary>
    /// <param name="paths">The data paths.</param>
    /// <returns>True when a settings file exists.</returns>
    public static bool Exists(DataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return File.Exists(paths.SettingsFile);
    }

    /// <inheritdoc />
    public void Update(Func<AppSettings, AppSettings> transform, SettingsChange change)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(change);
        ObjectDisposedException.ThrowIf(disposed, this);

        AppSettings old;
        AppSettings updated;
        lock (updateGate)
        {
            old = current;
            AppSettings transformed = transform(old) ?? throw new InvalidOperationException("The settings transform returned null.");
            if (ReferenceEquals(transformed, old))
            {
                return;
            }

            updated = SettingsSanitizer.Sanitize(transformed);
            current = updated;
            changeNumber++;
            eventContext = SynchronizationContext.Current ?? eventContext;
            if (fileUnread && IsExplicit(change.Kind))
            {
                editsWhileUnread.Add(transform);
            }
        }

        saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        Changed?.Invoke(this, new SettingsChangedEventArgs(old, updated, change));
    }

    /// <inheritdoc />
    public Task FlushAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.Run(WritePendingAsync);
    }

    /// <summary>Writes pending changes and stops the timers.</summary>
    /// <remarks>A file that still has not been read is tried once more; when it still cannot be read it is left unchanged.</remarks>
    public void Dispose()
    {
        lock (updateGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        // The semaphore stays usable: a timer callback already queued may still run and finds nothing to write.
        saveTimer.Dispose();
        recoveryTimer.Dispose();
        if (fileUnread && !TryRecover(atExit: true))
        {
            log.Warn(LogSource, "The settings file still couldn't be read, so it was left unchanged; changes made since the start were not saved.");
        }

        WritePendingAsync().GetAwaiter().GetResult();
    }

    /// <summary>Loads with explicit migrations and clock (tests).</summary>
    /// <param name="paths">The data paths.</param>
    /// <param name="log">The log.</param>
    /// <param name="migrator">The migrations to run.</param>
    /// <param name="now">The local time (names the damaged-file copy).</param>
    /// <returns>The store.</returns>
    internal static JsonSettingsStore Load(DataPaths paths, IAppLog log, SettingsMigrator migrator, DateTime now) =>
        Load(paths, log, migrator, now, ReadPatience, FirstRecoveryDelay);

    /// <summary>Loads with explicit migrations, clock and waits (tests).</summary>
    /// <param name="paths">The data paths.</param>
    /// <param name="log">The log.</param>
    /// <param name="migrator">The migrations to run.</param>
    /// <param name="now">The local time (names the damaged-file copy).</param>
    /// <param name="patience">How long to keep trying to open a file that another program holds.</param>
    /// <param name="recoveryDelay">The first pause before a file that could not be read is read again.</param>
    /// <returns>The store.</returns>
    internal static JsonSettingsStore Load(DataPaths paths, IAppLog log, SettingsMigrator migrator, DateTime now, TimeSpan patience, TimeSpan recoveryDelay)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(migrator);
        string path = paths.SettingsFile;
        AtomicFile.DeleteLeftovers(paths.RoamingRoot, Path.GetFileName(path));
        if (!File.Exists(path))
        {
            log.Info(LogSource, "No settings file: first run.");
            return new JsonSettingsStore(path, new AppSettings(), SettingsLoadOutcome.Created, log, migrator, false, recoveryDelay);
        }

        SettingsReadResult result;
        try
        {
            result = SettingsFileReader.Read(path, migrator, patience);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new JsonSettingsStore(path, new AppSettings(), SettingsLoadOutcome.Created, log, migrator, false, recoveryDelay);
        }

        switch (result.Status)
        {
            case SettingsReadStatus.Read:
                Report(path, result, log);
                return new JsonSettingsStore(path, result.Settings!, SettingsLoadOutcome.Loaded, log, migrator, false, recoveryDelay);

            case SettingsReadStatus.Damaged:
                bool kept = KeepDamaged(path, result, now, log);
                return new JsonSettingsStore(path, new AppSettings(), SettingsLoadOutcome.ReplacedDamaged, log, migrator, !kept, recoveryDelay);

            default:
                log.Error(LogSource, $"The settings file could not be read ({result.Problem}); the defaults are used, and the file is left "
                    + "unchanged and used as soon as it can be read.");
                return new JsonSettingsStore(path, new AppSettings(), SettingsLoadOutcome.Unavailable, log, migrator, true, recoveryDelay);
        }
    }

    /// <summary>Changes the user chose (applied again on top of the file's settings once a file that could not be read is read).</summary>
    private static bool IsExplicit(SettingsChangeKind kind) =>
        kind is SettingsChangeKind.Edit or SettingsChangeKind.ThemeLoaded or SettingsChangeKind.Import or SettingsChangeKind.Reset;

    /// <summary>Renames a damaged file, or copies its bytes when it cannot be renamed. False when neither worked (the file must not be replaced).</summary>
    private static bool KeepDamaged(string path, SettingsReadResult result, DateTime now, IAppLog log)
    {
        DateOnly today = DateOnly.FromDateTime(now);
        try
        {
            string renamed = SettingsFileReader.RenameDamaged(path, today);
            log.Warn(LogSource, $"The settings file could not be read ({result.Problem}); it was renamed to {Path.GetFileName(renamed)} and the defaults are used.");
            return true;
        }
        catch (Exception renameError) when (renameError is IOException or UnauthorizedAccessException)
        {
            try
            {
                string copy = SettingsFileReader.CopyDamaged(path, result.Content!, today);
                log.Warn(LogSource, $"The settings file could not be read ({result.Problem}) nor renamed ({renameError.Message}); a copy was kept as "
                    + $"{Path.GetFileName(copy)} and the defaults are used.");
                return true;
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                log.Error(LogSource, $"The settings file could not be read ({result.Problem}) nor kept; the defaults are used and the file is left unchanged.", copyError);
                return false;
            }
        }
    }

    private static void Report(string path, SettingsReadResult result, IAppLog log)
    {
        foreach (string step in result.Migration!.AppliedSteps)
        {
            log.Info(LogSource, $"Settings migrated: {step}.");
        }

        if (result.Migration.IsNewerThanCurrent)
        {
            try
            {
                string? copy = SettingsFileReader.BackUpNewerVersion(path, result.Migration.FileVersion);
                log.Warn(LogSource, $"The settings were written by a newer Holiday Lights (version {result.Migration.FileVersion})"
                    + (copy is null ? "." : $"; a copy was kept as {Path.GetFileName(copy)}."));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Warn(LogSource, "The settings of a newer Holiday Lights could not be copied before use.", e);
            }
        }

        if (result.RemovedValues.Count > 0)
        {
            log.Warn(LogSource, $"Settings values that could not be read use their defaults: {string.Join(", ", result.RemovedValues)}.");
        }
    }

    private void ScheduleRecovery()
    {
        lock (updateGate)
        {
            if (disposed)
            {
                return;
            }

            recoveryTimer.Change(recoveryDelay, Timeout.InfiniteTimeSpan);
            recoveryDelay = recoveryDelay * 2 < MaxRecoveryDelay ? recoveryDelay * 2 : MaxRecoveryDelay;
        }
    }

    /// <summary>The recovery timer: reads a file that could not be read before, until it can be.</summary>
    private void Recover()
    {
        bool done;
        try
        {
            done = disposed || !fileUnread || TryRecover(atExit: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn(LogSource, "The settings file could not be read again.", e);
            done = false;
        }

        if (!done)
        {
            ScheduleRecovery();
        }
    }

    /// <summary>
    /// Reads a file that could not be read before. Its settings are adopted on the event context (inline at exit, without
    /// <see cref="Changed"/>); a damaged one is kept and may then be replaced; a vanished one needs no protection.
    /// </summary>
    /// <returns>True when the file no longer needs protecting (or its adoption was posted).</returns>
    private bool TryRecover(bool atExit)
    {
        SettingsReadResult result;
        try
        {
            result = SettingsFileReader.Read(filePath, migrator, TimeSpan.Zero);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            log.Info(LogSource, "The settings file that could not be read is gone; the current settings are saved from now on.");
            Release();
            return true;
        }

        switch (result.Status)
        {
            case SettingsReadStatus.Read:
                SynchronizationContext? context = eventContext;
                if (atExit)
                {
                    Report(filePath, result, log);
                    Adopt(result.Settings!, raiseChanged: false);
                }
                else if (context is not null)
                {
                    Report(filePath, result, log);
                    context.Post(_ => Adopt(result.Settings!, raiseChanged: true), null);
                }
                else
                {
                    // Without a UI context the settings cannot be replaced safely now; the next attempt may have one.
                    return false;
                }

                return true;

            case SettingsReadStatus.Damaged:
                if (!KeepDamaged(filePath, result, DateTime.Now, log))
                {
                    return false;
                }

                Release();
                return true;

            default:
                return false;
        }
    }

    /// <summary>Uses the settings of a file that could finally be read, with the explicit changes made meanwhile applied again.</summary>
    private void Adopt(AppSettings loaded, bool raiseChanged)
    {
        AppSettings old;
        AppSettings adopted;
        int replayed;
        lock (updateGate)
        {
            if (!fileUnread || (raiseChanged && disposed))
            {
                return;
            }

            old = current;
            adopted = loaded;
            foreach (Func<AppSettings, AppSettings> edit in editsWhileUnread)
            {
                adopted = Replay(edit, adopted);
            }

            replayed = editsWhileUnread.Count;
            editsWhileUnread.Clear();
            current = adopted;
            if (replayed > 0)
            {
                changeNumber++;
            }
            else
            {
                // The file holds exactly these settings: nothing to write.
                Interlocked.Exchange(ref savedChangeNumber, Interlocked.Read(ref changeNumber));
            }

            fileUnread = false;
        }

        log.Info(LogSource, "The settings file could be read now; its settings are used"
            + (replayed > 0 ? $", with the {replayed} changes made meanwhile applied again." : "."));
        if (replayed > 0)
        {
            ScheduleSave();
        }

        if (raiseChanged)
        {
            Changed?.Invoke(this, new SettingsChangedEventArgs(old, adopted, SettingsChange.Internal));
        }
    }

    private AppSettings Replay(Func<AppSettings, AppSettings> edit, AppSettings settings)
    {
        try
        {
            return edit(settings) is { } edited ? SettingsSanitizer.Sanitize(edited) : settings;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log.Warn(LogSource, "A change made while the settings file could not be read could not be applied again.", e);
            return settings;
        }
    }

    /// <summary>Ends the protection without reading (the file is gone, or it was damaged and has been kept): the settings in memory are saved.</summary>
    private void Release()
    {
        lock (updateGate)
        {
            if (!fileUnread)
            {
                return;
            }

            editsWhileUnread.Clear();
            changeNumber++;
            fileUnread = false;
        }

        ScheduleSave();
    }

    /// <summary>Starts the debounce from a background path, where the store may be disposing meanwhile.</summary>
    private void ScheduleSave()
    {
        try
        {
            saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Disposing: Dispose writes what is pending.
        }
    }

    private async Task WritePendingAsync()
    {
        await writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (fileUnread)
            {
                SkipWriteOfUnreadFile();
                return;
            }

            long target = Interlocked.Read(ref changeNumber);
            if (target == Interlocked.Read(ref savedChangeNumber))
            {
                return;
            }

            AppSettings snapshot = current;
            try
            {
                AtomicFile.WriteAllText(filePath, HolidayLightsJson.Serialize(snapshot));
                Interlocked.Exchange(ref savedChangeNumber, target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Error(LogSource, "Couldn't save the settings.", e);
                RaiseSaveFailed(e);
            }
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <summary>
    /// A write while the file has not been read: nothing is written (the file holds the user's real settings). Explicit
    /// changes are kept and saved once the file can be read; the user is told that they are not saved yet.
    /// </summary>
    private void SkipWriteOfUnreadFile()
    {
        bool userChanges;
        bool firstTime;
        lock (updateGate)
        {
            userChanges = editsWhileUnread.Count > 0;
            firstTime = !unreadWriteLogged;
            unreadWriteLogged = true;
        }

        if (firstTime)
        {
            log.Warn(LogSource, "The settings are not saved while the settings file can't be read, so it is not replaced; they are saved once it can be read.");
        }

        if (userChanges)
        {
            RaiseSaveFailed(new IOException("The settings file is in use or can't be read, so it isn't replaced yet. Your changes are saved as soon as it can be read."));
        }
    }

    private void RaiseSaveFailed(Exception exception)
    {
        EventHandler<SettingsSaveFailedEventArgs>? handler = SaveFailed;
        if (handler is null)
        {
            return;
        }

        var args = new SettingsSaveFailedEventArgs(exception);
        if (eventContext is { } context)
        {
            context.Post(_ => handler(this, args), null);
        }
        else
        {
            handler(this, args);
        }
    }
}
