using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace HolidayLights.App.Shell;

/// <summary>
/// The rotating log (PRODUCT-SPEC 6.6.5): <see cref="DataPaths.LogsFolder"/>, 3 files of 1 MB, thread-safe, never throws.
/// Owner: app-shell.
/// </summary>
/// <remarks>
/// <para>Entries are queued and written by a background task, so callers on the UI and Lights threads never wait for
/// the disk; <see cref="Flush"/> writes what is queued at once (unhandled exceptions, exit). The file is opened for each
/// batch with write access shared only with readers, so the app, a screen saver process and a settings-only session can
/// log into the same files without interleaving lines; a writer that finds the file busy retries shortly after.</para>
/// <para>When <c>HolidayLights.log</c> reaches 1 MB it becomes <c>HolidayLights.1.log</c> (the previous one
/// <c>HolidayLights.2.log</c>; older entries are dropped). Entries below <see cref="MinimumLevel"/> are discarded; the
/// environment variable <see cref="LevelVariable"/> (debug, info, warning, error) changes the default of Info.</para>
/// </remarks>
public sealed class RollingFileLog : IAppLog, IDisposable
{
    /// <summary>The environment variable that sets the minimum level ("debug" for diagnostics).</summary>
    public const string LevelVariable = "HOLIDAYLIGHTS_LOG_LEVEL";

    /// <summary>The current log file name.</summary>
    public const string FileName = "HolidayLights.log";

    /// <summary>A file is rotated when it reaches this size.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>Number of files kept (the current one and two older ones).</summary>
    public const int FileCount = 3;

    private const int MaxQueuedEntries = 10_000;
    private const int MaxWriteAttempts = 20;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');

    private readonly string folder;
    private readonly string processTag;
    private readonly long maxFileBytes;
    private readonly Channel<string> entries = Channel.CreateUnbounded<string>();
    private readonly Lock fileGate = new();
    private readonly Task writer;
    private int queued;
    private int dropped;
    private int disposed;
    private volatile bool filesStopped;

    /// <summary>Opens the log.</summary>
    /// <param name="paths">The logs folder.</param>
    public RollingFileLog(DataPaths paths)
        : this(paths.LogsFolder, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), LevelFromEnvironment(), MaxFileBytes)
    {
    }

    /// <summary>Opens a log in any folder with explicit limits (tests).</summary>
    /// <param name="folder">The folder of the log files.</param>
    /// <param name="processTag">Identifies the writing process in every line.</param>
    /// <param name="minimumLevel">Entries below this level are discarded.</param>
    /// <param name="maxFileBytes">The rotation size.</param>
    internal RollingFileLog(string folder, string processTag, AppLogLevel minimumLevel, long maxFileBytes)
    {
        ArgumentNullException.ThrowIfNull(folder);
        this.folder = folder;
        this.processTag = processTag;
        this.maxFileBytes = maxFileBytes;
        MinimumLevel = minimumLevel;
        writer = Task.Run(WriteLoopAsync);
    }

    /// <summary>Entries below this level are discarded.</summary>
    public AppLogLevel MinimumLevel { get; }

    /// <summary>The current log file.</summary>
    public string CurrentFile => Path.Combine(folder, FileName);

    /// <inheritdoc />
    public void Write(AppLogLevel level, string source, string message, Exception? exception = null)
    {
        try
        {
            if (level < MinimumLevel || Volatile.Read(ref disposed) != 0 || filesStopped)
            {
                return;
            }

            if (Interlocked.Increment(ref queued) > MaxQueuedEntries)
            {
                Interlocked.Decrement(ref queued);
                Interlocked.Increment(ref dropped);
                return;
            }

            entries.Writer.TryWrite(Format(DateTime.Now, level, source, message, exception));
        }
        catch (Exception)
        {
            // A log must never take the program down.
        }
    }

    /// <summary>Writes every queued entry now, on the calling thread (unhandled exceptions, exit).</summary>
    public void Flush()
    {
        try
        {
            WritePending();
        }
        catch (Exception)
        {
            // Never throws.
        }
    }

    /// <summary>
    /// Writes what is queued, then keeps every later entry off the disk: the uninstaller is about to remove the logs folder
    /// with the user's settings ("Also remove my settings and themes"), and a later line would create it again.
    /// </summary>
    public void StopWritingFiles()
    {
        try
        {
            lock (fileGate)
            {
                WriteBatch(Drain());
                filesStopped = true;
            }
        }
        catch (Exception)
        {
            // Never throws.
            filesStopped = true;
        }
    }

    /// <summary>Flushes and closes the file.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        entries.Writer.TryComplete();
        try
        {
            writer.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The loop never faults (every write is guarded); nothing is left to report.
        }

        Flush();
    }

    /// <summary>Formats one entry: local time, level, process, source, message and the exception's details.</summary>
    /// <param name="time">The local time.</param>
    /// <param name="level">The level.</param>
    /// <param name="source">The component.</param>
    /// <param name="message">The message.</param>
    /// <param name="exception">The exception, if any.</param>
    /// <returns>One or more lines, ending with a line break.</returns>
    internal string Format(DateTime time, AppLogLevel level, string source, string message, Exception? exception)
    {
        var text = new StringBuilder(128);
        text.Append(time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(LevelText(level))
            .Append(" [").Append(processTag).Append("] ")
            .Append(source).Append(": ").Append(message).Append(Environment.NewLine);
        if (exception is not null)
        {
            foreach (string line in Scrub(exception.ToString()).Split(["\r\n", "\n"], StringSplitOptions.None))
            {
                text.Append("    ").Append(line).Append(Environment.NewLine);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Replaces the user's profile folder, which names the account, with <c>%USERPROFILE%</c> in exception details: I/O
    /// errors carry full paths (CONTRACTS 4, never log personal data; review r1 #75).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text without the profile folder.</returns>
    internal static string Scrub(string text) =>
        UserProfile.Length > 3 ? text.Replace(UserProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : text;

    private static string LevelText(AppLogLevel level) => level switch
    {
        AppLogLevel.Debug => "DEBUG",
        AppLogLevel.Info => "INFO ",
        AppLogLevel.Warning => "WARN ",
        _ => "ERROR",
    };

    private static AppLogLevel LevelFromEnvironment() =>
        Environment.GetEnvironmentVariable(LevelVariable)?.Trim().ToLowerInvariant() switch
        {
            "debug" => AppLogLevel.Debug,
            "warning" or "warn" => AppLogLevel.Warning,
            "error" => AppLogLevel.Error,
            _ => AppLogLevel.Info,
        };

    private async Task WriteLoopAsync()
    {
        try
        {
            while (await entries.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                WritePending();
            }
        }
        catch (Exception)
        {
            // Never throws; Flush writes what is left.
        }
    }

    /// <summary>Takes the queued entries and appends them, under one lock so batches keep their order.</summary>
    private void WritePending()
    {
        lock (fileGate)
        {
            WriteBatch(Drain());
        }
    }

    private List<string> Drain()
    {
        var batch = new List<string>();
        while (entries.Reader.TryRead(out string? entry))
        {
            batch.Add(entry);
        }

        Interlocked.Add(ref queued, -batch.Count);
        int lost = Interlocked.Exchange(ref dropped, 0);
        if (lost > 0)
        {
            batch.Add(Format(DateTime.Now, AppLogLevel.Warning, "Log", $"{lost} log entries were dropped because too many were written at once.", null));
        }

        return batch;
    }

    private void WriteBatch(List<string> batch)
    {
        if (batch.Count == 0 || filesStopped)
        {
            return;
        }

        byte[] bytes = Utf8.GetBytes(string.Concat(batch));
        for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
        {
            try
            {
                Directory.CreateDirectory(folder);
                RotateIfFull();
                using var file = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
                file.Write(bytes);
                return;
            }
            catch (IOException e) when (attempt < MaxWriteAttempts && IsBusy(e))
            {
                // Another process is writing its batch: wait for it.
                Thread.Sleep(RetryDelay);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The disk is full or the folder is not writable: these entries are lost.
                return;
            }
        }
    }

    /// <summary>True for a sharing or lock violation (<c>ERROR_SHARING_VIOLATION</c>, <c>ERROR_LOCK_VIOLATION</c>): the file is busy, not broken.</summary>
    private static bool IsBusy(IOException exception) => (exception.HResult & 0xFFFF) is 32 or 33;

    private void RotateIfFull()
    {
        var current = new FileInfo(CurrentFile);
        if (!current.Exists || current.Length < maxFileBytes)
        {
            return;
        }

        try
        {
            for (int index = FileCount - 1; index >= 1; index--)
            {
                string older = Path.Combine(folder, RotatedName(index));
                string newer = index == 1 ? CurrentFile : Path.Combine(folder, RotatedName(index - 1));
                if (index == FileCount - 1)
                {
                    File.Delete(older);
                }

                if (File.Exists(newer))
                {
                    File.Move(newer, older);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Another process has the file open: rotate with a later batch.
        }
    }

    private static string RotatedName(int index) =>
        $"{Path.GetFileNameWithoutExtension(FileName)}.{index.ToString(CultureInfo.InvariantCulture)}{Path.GetExtension(FileName)}";
}
