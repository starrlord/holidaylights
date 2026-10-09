using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Displays;

/// <summary>
/// Monitors, work areas and effective DPI (<c>EnumDisplayMonitors</c>, <c>GetMonitorInfo</c>, <c>GetDpiForMonitor</c>),
/// with debounced change notification (see <see cref="IDisplayService"/>). Owner: platform.
/// </summary>
/// <remarks>
/// <para>A hidden top-level window on the creating (UI) thread receives <c>WM_DISPLAYCHANGE</c>, <c>WM_DPICHANGED</c>,
/// <c>WM_SETTINGCHANGE(SPI_SETWORKAREA)</c> and <c>TaskbarCreated</c>; each restarts a 300 ms debounce timer, after which
/// the displays are read again. A 2 s poll compares the cheap monitor tuple and starts the same debounce when it differs
/// (for example a scale change on a display without our window). <see cref="DisplaysChanged"/> is raised only when
/// <see cref="DisplayChanges.Compute"/> finds a difference in <c>{device, rcMonitor, rcWork, dpi}</c>.</para>
/// <para><see cref="Displays"/>, <see cref="Primary"/> and <see cref="FromPoint"/> read an immutable snapshot and may be
/// called from any thread.</para>
/// </remarks>
public sealed class DisplayService : IDisplayService
{
    private const string LogSource = "Platform.Displays";
    private const nuint PollTimerId = 1;
    private const nuint DebounceTimerId = 2;

    private readonly IAppLog log;
    private readonly IDisplaySource source;
    private readonly TimeSpan debounce;
    private readonly MessageWindow window;
    private readonly uint taskbarCreatedMessage;
    private volatile Snapshot snapshot;

    /// <summary>Enumerates the displays and creates the hidden message window on the calling (UI) thread.</summary>
    /// <param name="log">The log.</param>
    public DisplayService(IAppLog log)
        : this(log, new SystemDisplaySource(), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(300))
    {
    }

    /// <summary>Creates the service with an explicit monitor source and timings (tests).</summary>
    /// <param name="log">The log.</param>
    /// <param name="source">Where monitors are read from.</param>
    /// <param name="pollInterval">The safety-net poll interval.</param>
    /// <param name="debounce">How long changes must settle before the displays are read again.</param>
    internal DisplayService(IAppLog log, IDisplaySource source, TimeSpan pollInterval, TimeSpan debounce)
    {
        this.log = log;
        this.source = source;
        this.debounce = debounce;
        snapshot = ReadSnapshot();
        log.Info(LogSource, $"{snapshot.Displays.Count} display(s): {string.Join("; ", snapshot.Displays.Select(d => d.Describe()))}.");

        taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        using (PhysicalPixelsScope.Enter())
        {
            // A per-monitor aware window receives WM_DPICHANGED whatever the process awareness is.
            window = new MessageWindow("Holiday Lights Displays", OnMessage, log, LogSource);
        }

        window.StartTimer(PollTimerId, pollInterval);
    }

    /// <inheritdoc />
    public event EventHandler<DisplaysChangedEventArgs>? DisplaysChanged;

    /// <inheritdoc />
    public IReadOnlyList<DisplayInfo> Displays => snapshot.Displays;

    /// <inheritdoc />
    public DisplayInfo Primary => snapshot.Displays[0];

    /// <inheritdoc />
    public DisplayInfo FromPoint(PointI point) => DisplayTopology.Nearest(snapshot.Displays, point);

    /// <inheritdoc />
    public unsafe DisplayInfo FromCursor()
    {
        using PhysicalPixelsScope scope = PhysicalPixelsScope.Enter();
        NativePoint cursor;
        return NativeMethods.GetCursorPos(&cursor) ? FromPoint(new PointI(cursor.X, cursor.Y)) : Primary;
    }

    /// <summary>Destroys the message window and stops polling.</summary>
    public void Dispose()
    {
        window.StopTimer(PollTimerId);
        window.StopTimer(DebounceTimerId);
        window.Dispose();
    }

    /// <summary>(Re)starts the debounce: called for every topology message and by the poll; tests call it to simulate one.</summary>
    internal void NotifyTopologyMessage() => window.StartTimer(DebounceTimerId, debounce);

    private bool OnMessage(uint message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (message)
        {
            case NativeMethods.WM_TIMER when wParam == (nint)PollTimerId:
                Poll();
                return true;
            case NativeMethods.WM_TIMER when wParam == (nint)DebounceTimerId:
                window.StopTimer(DebounceTimerId);
                Refresh();
                return true;
            case NativeMethods.WM_DISPLAYCHANGE:
            case NativeMethods.WM_DPICHANGED:
            case NativeMethods.WM_SETTINGCHANGE when wParam == (nint)NativeMethods.SPI_SETWORKAREA:
                NotifyTopologyMessage();
                return message == NativeMethods.WM_DPICHANGED;
            default:
                if (message == taskbarCreatedMessage && message != 0)
                {
                    NotifyTopologyMessage();
                }

                return false;
        }
    }

    private void Poll()
    {
        IReadOnlyList<MonitorSample> samples = source.SampleMonitors();
        if (!samples.SequenceEqual(snapshot.Samples))
        {
            NotifyTopologyMessage();
        }
    }

    private void Refresh()
    {
        Snapshot previous = snapshot;
        Snapshot current = ReadSnapshot();
        DisplayChanges changes = DisplayChanges.Compute(previous.Displays, current.Displays);
        if (changes.IsEmpty)
        {
            // Keep the published list (numbers and names) but remember the samples, so the poll stays quiet.
            snapshot = previous with { Samples = current.Samples };
            return;
        }

        snapshot = current;
        log.Info(
            LogSource,
            $"Displays changed ({changes.Added.Count} added, {changes.Removed.Count} removed, {changes.Changed.Count} changed): " +
            string.Join("; ", current.Displays.Select(d => d.Describe())) + ".");
        DisplaysChanged?.Invoke(this, new DisplaysChangedEventArgs(previous.Displays, current.Displays));
    }

    private Snapshot ReadSnapshot()
    {
        IReadOnlyList<MonitorSample> samples = source.SampleMonitors();
        IReadOnlyList<DisplayInfo> displays = DisplayTopology.Build(samples, source.ReadIdentities(samples));
        if (displays.Count > 0)
        {
            return new Snapshot(samples, displays);
        }

        // No monitor reported (a disconnected remote session): describe the virtual screen as one 96 DPI display.
        log.Warn(LogSource, "Windows reported no display; using the screen size as one display.");
        var bounds = RectI.FromXYWH(
            0, 0, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN), NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));
        var fallback = new MonitorSample(@"\\.\DISPLAY", bounds, bounds, 96, IsPrimary: true);
        return new Snapshot(samples, DisplayTopology.Build([fallback], new Dictionary<string, DisplayIdentity>()));
    }

    private sealed record Snapshot(IReadOnlyList<MonitorSample> Samples, IReadOnlyList<DisplayInfo> Displays);
}
