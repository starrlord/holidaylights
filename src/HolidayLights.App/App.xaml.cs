using System.Windows;
using System.Windows.Threading;
using HolidayLights.App.Shell;

namespace HolidayLights.App;

/// <summary>
/// The WPF application object (owner: app-shell). Created by <see cref="Program.Main"/> after the screen saver and
/// single-instance checks; it never shows a window by itself and shuts down explicitly.
/// </summary>
/// <remarks>
/// Unexpected errors are logged (PRODUCT-SPEC 6.6.5), never shown in a box: an exception on the UI thread is logged and
/// the program goes on (the lights run on their own thread), unless errors repeat so quickly that something is stuck, in
/// which case the program ends after logging. When Windows ends the session, settings and the log are written at once.
/// </remarks>
public partial class App : Application
{
    private const string LogSource = "App";
    private const int ErrorBurstLimit = 20;
    private static readonly TimeSpan ErrorBurstWindow = TimeSpan.FromSeconds(10);

    private readonly Queue<DateTime> recentErrors = new();
    private IAppLog log = NullAppLog.Instance;
    private Action? sessionEnding;

    /// <summary>Creates the application and loads its resources (Fluent theme, tokens, illustrations).</summary>
    public App() => InitializeComponent();

    /// <summary>Creates the application with the program's log, catching every unhandled exception into it.</summary>
    /// <param name="log">The log.</param>
    /// <param name="onSessionEnding">Writes pending settings when Windows ends the session.</param>
    public App(IAppLog log, Action onSessionEnding)
        : this()
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(onSessionEnding);
        this.log = log;
        sessionEnding = onSessionEnding;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        SessionEnding += OnSessionEnding;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        log.Error(LogSource, "Unexpected error on the UI thread.", e.Exception);
        DateTime now = DateTime.UtcNow;
        recentErrors.Enqueue(now);
        while (recentErrors.Count > 0 && now - recentErrors.Peek() > ErrorBurstWindow)
        {
            recentErrors.Dequeue();
        }

        // Keep running after an isolated error; a burst means the UI is stuck in a failing loop.
        e.Handled = recentErrors.Count <= ErrorBurstLimit;
        if (!e.Handled)
        {
            log.Error(LogSource, "Too many errors in a row; Holiday Lights is closing.");
            (log as RollingFileLog)?.Flush();
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        log.Error(LogSource, $"Unexpected error{(e.IsTerminating ? "; Holiday Lights is closing" : "")}.", e.ExceptionObject as Exception);
        (log as RollingFileLog)?.Flush();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        log.Error(LogSource, "Unexpected error in a background task.", e.Exception);
        e.SetObserved();
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        log.Info(LogSource, $"Windows is ending the session ({e.ReasonSessionEnding}).");
        sessionEnding?.Invoke();
        sessionEnding = null;
    }
}
