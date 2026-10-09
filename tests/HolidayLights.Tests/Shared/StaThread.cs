using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace HolidayLights.Tests.Shared;

/// <summary>Runs test code on the tests' shared STA thread (WPF objects need one; xUnit threads are MTA). Owner: contracts.</summary>
/// <remarks>
/// <para>One long-lived STA thread with a running dispatcher runs every call, one call at a time. WPF's text services keep
/// hooks per thread: with a new thread per call, a window-move event (<c>EVENT_SYSTEM_MOVESIZEEND</c>, for example the user
/// dragging any window during the run) could reach an earlier call's text box from the wrong thread and crash the test
/// host.</para>
/// <para>The code runs without a synchronization context, as on a plain thread, so awaits inside the code under test resume
/// on the thread pool. The timeout counts from when the code starts. A failure on the thread between calls (a timer tick, a
/// hook callback) is reported by the next call. A call that times out keeps the thread, and later calls get a new one.</para>
/// </remarks>
public static class StaThread
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(1);
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    private static Dispatcher? dispatcher;
    private static Exception? pendingFailure;

    /// <summary>Runs an action on the shared STA thread and rethrows its exception, if any.</summary>
    /// <param name="action">The code to run.</param>
    /// <param name="timeout">Maximum run time (default 30 s).</param>
    /// <exception cref="TimeoutException">The code did not finish in time.</exception>
    public static void Run(Action action, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        OneAtATime.Wait();
        try
        {
            ThrowPendingFailure();
            Dispatcher thread = dispatcher ??= StartThread();
            Exception? failure = null;
            using var started = new ManualResetEventSlim();
            DispatcherOperation operation = thread.InvokeAsync(() =>
            {
                started.Set();
                SynchronizationContext? context = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(null);
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(context);
                }
            });
            if (!started.Wait(StartTimeout) || !operation.Task.Wait(timeout ?? DefaultTimeout))
            {
                dispatcher = null;
                throw new TimeoutException("The STA test code did not finish in time.");
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            ThrowPendingFailure();
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    private static Dispatcher StartThread()
    {
        Dispatcher? created = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            created = Dispatcher.CurrentDispatcher;
            created.UnhandledException += OnUnhandledException;
            ready.Set();
            RunDispatcher(created);
        })
        {
            Name = "STA tests",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return created!;
    }

    /// <summary>Pumps until shutdown; a failure outside any call is kept for the next call instead of ending the thread.</summary>
    private static void RunDispatcher(Dispatcher current)
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

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Interlocked.CompareExchange(ref pendingFailure, e.Exception, null);
        e.Handled = true;
    }

    private static void ThrowPendingFailure()
    {
        if (Interlocked.Exchange(ref pendingFailure, null) is { } failure)
        {
            throw new InvalidOperationException("The shared STA test thread failed outside a test.", failure);
        }
    }
}
