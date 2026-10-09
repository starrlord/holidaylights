using System.Windows.Threading;

namespace HolidayLights.App.Shell;

/// <summary>Screen saver sessions in the running app (see <see cref="IScreenSaverSessions"/>). Owner: app-shell.</summary>
/// <remarks>
/// Thread-safe: the instance pipe reports the external <c>/s</c> process from the thread pool, "Preview Screen Saver"
/// reports from the UI thread. Sessions are counted, so a preview and a real saver can overlap. <see cref="Changed"/> is
/// raised on the thread that created the tracker (the UI thread), where the desktop layers and the music follow it.
/// </remarks>
public sealed class ScreenSaverSessions : IScreenSaverSessions
{
    private const string LogSource = "Shell.Saver";

    private readonly IAppLog log;
    private readonly Dispatcher dispatcher;
    private int sessions;

    /// <summary>Creates the tracker.</summary>
    /// <param name="services">The services (lights and music follow the sessions).</param>
    public ScreenSaverSessions(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        log = services.Log;
        dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>Raised on the UI thread after a session started or ended.</summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool IsSaverRunning => Volatile.Read(ref sessions) > 0;

    /// <inheritdoc />
    public void SaverStarted()
    {
        int count = Interlocked.Increment(ref sessions);
        log.Info(LogSource, $"A screen saver session started ({count} running).");
        RaiseChanged();
    }

    /// <inheritdoc />
    public void SaverStopped()
    {
        int count;
        int seen;
        do
        {
            seen = Volatile.Read(ref sessions);
            count = Math.Max(0, seen - 1);
        }
        while (Interlocked.CompareExchange(ref sessions, count, seen) != seen);

        log.Info(LogSource, $"A screen saver session ended ({count} running).");
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        if (dispatcher.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty));
        }
    }
}
