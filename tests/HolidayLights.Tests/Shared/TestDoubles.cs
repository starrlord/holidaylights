using System.Collections.Concurrent;

namespace HolidayLights.Tests.Shared;

/// <summary>An in-memory <see cref="ISettingsStore"/> with the real semantics minus the file (owner: contracts).</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    /// <summary>Creates the store.</summary>
    /// <param name="initial">The initial settings (default: <c>new AppSettings()</c>).</param>
    public InMemorySettingsStore(AppSettings? initial = null) => Current = initial ?? new AppSettings();

    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public AppSettings Current { get; private set; }

    /// <inheritdoc />
    public SettingsLoadOutcome LoadOutcome => SettingsLoadOutcome.Loaded;

    /// <summary>Every change applied, in order.</summary>
    public List<SettingsChange> History { get; } = [];

    /// <inheritdoc />
    public void Update(Func<AppSettings, AppSettings> transform, SettingsChange change)
    {
        AppSettings old = Current;
        AppSettings updated = transform(old);
        if (ReferenceEquals(updated, old))
        {
            return;
        }

        Current = updated;
        History.Add(change);
        Changed?.Invoke(this, new SettingsChangedEventArgs(old, updated, change));
    }

    /// <inheritdoc />
    public Task FlushAsync() => Task.CompletedTask;
}

/// <summary>A log that keeps every entry for assertions (owner: contracts).</summary>
public sealed class RecordingLog : IAppLog
{
    private readonly ConcurrentQueue<(AppLogLevel Level, string Source, string Message, Exception? Exception)> entries = new();

    /// <summary>The entries written so far.</summary>
    public IReadOnlyList<(AppLogLevel Level, string Source, string Message, Exception? Exception)> Entries => [.. entries];

    /// <inheritdoc />
    public void Write(AppLogLevel level, string source, string message, Exception? exception = null) =>
        entries.Enqueue((level, source, message, exception));
}

/// <summary>
/// An <see cref="IHoldingFolder"/> that moves files into a folder under a test data root and deletes them on commit instead
/// of using the Recycle Bin (owner: contracts).
/// </summary>
public sealed class TestHoldingFolder : IHoldingFolder
{
    private readonly string folder;
    private readonly List<HeldItem> items = [];
    private int sequence;

    /// <summary>Creates the holding folder under <see cref="DataPaths.RemovedFolder"/>.</summary>
    /// <param name="paths">The (temporary) data paths.</param>
    public TestHoldingFolder(DataPaths paths) => folder = Path.Combine(paths.RemovedFolder, "test-session");

    /// <inheritdoc />
    public IReadOnlyList<HeldItem> Items => items;

    /// <inheritdoc />
    public HeldItem Hold(string path)
    {
        Directory.CreateDirectory(folder);
        string held = Path.Combine(folder, $"{sequence++}-{Path.GetFileName(path)}");
        File.Move(path, held);
        var item = new HeldItem(path, held, DateTimeOffset.Now);
        items.Add(item);
        return item;
    }

    /// <inheritdoc />
    public string Restore(HeldItem item)
    {
        string target = item.OriginalPath;
        if (File.Exists(target))
        {
            target = Path.Combine(Path.GetDirectoryName(target)!, $"{Path.GetFileNameWithoutExtension(target)} (2){Path.GetExtension(target)}");
        }

        File.Move(item.HeldPath, target);
        items.Remove(item);
        return target;
    }

    /// <inheritdoc />
    public void CommitSession()
    {
        foreach (HeldItem item in items)
        {
            File.Delete(item.HeldPath);
        }

        items.Clear();
    }

    /// <inheritdoc />
    public void RecycleLeftovers()
    {
    }
}
