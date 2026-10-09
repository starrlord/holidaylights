using System.Windows.Threading;

namespace HolidayLights.App.Shell;

/// <summary>
/// The pause aggregation of the running app (CONTRACTS 6.2): the OS signals of <see cref="IPauseSignalSource"/>, the
/// "When the Lights Rest" settings and the screen saver sessions become one <see cref="PauseState"/> for the presenter,
/// the music and the notifications. UI thread; <see cref="Changed"/> is raised on the UI thread.
/// </summary>
public sealed class PauseMonitor : IDisposable
{
    private readonly IPauseSignalSource source;
    private readonly ISettingsStore settings;
    private readonly ScreenSaverSessions sessions;
    private readonly Dispatcher dispatcher;
    private bool started;
    private bool disposed;

    /// <summary>Creates the monitor; call <see cref="Start"/>.</summary>
    /// <param name="source">The OS signals.</param>
    /// <param name="settings">The rest settings.</param>
    /// <param name="sessions">The screen saver sessions.</param>
    public PauseMonitor(IPauseSignalSource source, ISettingsStore settings, ScreenSaverSessions sessions)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sessions);
        this.source = source;
        this.settings = settings;
        this.sessions = sessions;
        dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>Raised on the UI thread when the signals or the resulting state changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The latest OS signals.</summary>
    public PauseSignals Signals { get; private set; } = PauseSignals.None;

    /// <summary>What rests now.</summary>
    public PauseState Current { get; private set; } = PauseState.None;

    /// <summary>Starts listening and polling.</summary>
    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        source.Changed += OnSignalsChanged;
        settings.Changed += OnSettingsChanged;
        sessions.Changed += OnSessionsChanged;
        source.Start();
        Recompute(always: true);
    }

    /// <summary>Stops listening and disposes the signal source.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        source.Changed -= OnSignalsChanged;
        settings.Changed -= OnSettingsChanged;
        sessions.Changed -= OnSessionsChanged;
        source.Dispose();
    }

    private void OnSignalsChanged(object? sender, EventArgs e)
    {
        if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(() => Recompute(always: true));
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Rest != e.NewSettings.Rest)
        {
            Recompute(always: false);
        }
    }

    private void OnSessionsChanged(object? sender, EventArgs e) => Recompute(always: false);

    private void Recompute(bool always)
    {
        if (disposed)
        {
            return;
        }

        Signals = source.Current;
        PauseState next = PauseRules.Evaluate(Signals, settings.Current.Rest, sessions.IsSaverRunning);
        bool changed = !IsSame(Current, next);
        Current = next;
        if (changed || always)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsSame(PauseState a, PauseState b) =>
        a.Lights.Global == b.Lights.Global && a.Lights.RestingDisplayIds.SetEquals(b.Lights.RestingDisplayIds)
        && a.MusicPausedByRules == b.MusicPausedByRules && a.MusicStopped == b.MusicStopped
        && a.FullScreenActive == b.FullScreenActive && a.FocusSessionActive == b.FocusSessionActive;
}
