using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace HolidayLights.Tests.Platform;

/// <summary>
/// An STA thread that pumps messages with a WPF dispatcher, like the app's UI thread: services with hidden message
/// windows are created and used on it, and their timers and broadcasts are delivered.
/// </summary>
internal sealed class DispatcherThread : IDisposable
{
    private readonly Thread thread;
    private readonly Dispatcher dispatcher;

    public DispatcherThread()
    {
        Dispatcher? created = null;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            created = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Test UI thread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        dispatcher = created!;
    }

    /// <summary>Runs code on the thread and returns its result (exceptions are rethrown here).</summary>
    public T Invoke<T>(Func<T> work)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        dispatcher.Invoke(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        failure?.Throw();
        return result;
    }

    /// <summary>Runs code on the thread.</summary>
    public void Invoke(Action work) => Invoke(() =>
    {
        work();
        return 0;
    });

    /// <summary>Lets the thread pump messages for a while (timers, posted messages), then returns.</summary>
    public static void Wait(TimeSpan duration) => Thread.Sleep(duration);

    /// <summary>Waits until a condition holds or the timeout passes.</summary>
    public static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        DateTime end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }

    public void Dispose()
    {
        dispatcher.InvokeShutdown();
        thread.Join(TimeSpan.FromSeconds(5));
    }
}
