using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace HolidayLights.Tests.Ui.Fakes;

/// <summary>
/// The one UI thread of the Ui tests (a fixture of <see cref="WpfCollection"/>): an STA thread with a running dispatcher on
/// which every test builds and drives its windows, as the app does on its single UI thread.
/// </summary>
/// <remarks>
/// WPF's text services (TSF) keep per-thread state that the text boxes of one thread can reach from another. With a new STA
/// thread per test, a window-move event (<c>EVENT_SYSTEM_MOVESIZEEND</c>, for example the user dragging any window during the
/// run) reached an earlier test's text box from the wrong thread, and the test host crashed. One long-lived thread avoids
/// that; disposing the fixture shuts the dispatcher down, which removes the text services' hooks before the thread ends.
/// Failures that happen between tests (a timer tick, a hook callback) are reported by the next <see cref="Run"/>.
/// </remarks>
public sealed class UiThread : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly Thread thread;
    private readonly Dispatcher dispatcher;
    private Exception? pendingFailure;

    /// <summary>Starts the thread and its dispatcher.</summary>
    public UiThread()
    {
        Dispatcher? created = null;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            created = Dispatcher.CurrentDispatcher;
            created.UnhandledException += OnUnhandledException;
            ready.Set();
            RunDispatcher(created);
        })
        {
            Name = "Ui tests",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        dispatcher = created!;
    }

    /// <summary>Runs test code on the UI thread and rethrows its exception, if any.</summary>
    /// <param name="action">The test code.</param>
    /// <param name="timeout">The longest it may run (default 30 s).</param>
    /// <exception cref="TimeoutException">The code did not finish in time.</exception>
    public void Run(Action action, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowPendingFailure();
        Exception? failure = null;
        DispatcherOperation operation = dispatcher.InvokeAsync(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        if (!operation.Task.Wait(timeout ?? DefaultTimeout))
        {
            throw new TimeoutException("The UI test code did not finish in time.");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        ThrowPendingFailure();
    }

    /// <summary>Shuts the dispatcher down (text services, timers, hooks) and lets the thread end.</summary>
    public void Dispose()
    {
        if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.InvokeShutdown();
        }

        thread.Join(TimeSpan.FromSeconds(10));
    }

    /// <summary>Pumps until shutdown; a failure outside any test (a hook callback) is kept for the next test instead of ending the thread.</summary>
    private void RunDispatcher(Dispatcher current)
    {
        while (!current.HasShutdownStarted)
        {
            try
            {
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref pendingFailure, exception, null);
            }
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Interlocked.CompareExchange(ref pendingFailure, e.Exception, null);
        e.Handled = true;
    }

    private void ThrowPendingFailure()
    {
        if (Interlocked.Exchange(ref pendingFailure, null) is { } failure)
        {
            throw new InvalidOperationException("The UI thread failed outside a test.", failure);
        }
    }
}
