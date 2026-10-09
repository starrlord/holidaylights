using System.Collections.Concurrent;
using HolidayLights.Audio.Native;

namespace HolidayLights.Audio.Scheduling;

/// <summary>
/// The director's music thread (above-normal priority, ARCHITECTURE 3): runs posted actions one at a time and calls a
/// wake-up callback when the owner's next timer is due. Nothing else runs on it, so the director needs no locks.
/// </summary>
internal sealed class MusicThread : IDisposable
{
    private const string LogSource = "Audio.Director";

    /// <summary>Longest single wait; the owner's timers are recomputed at least this often.</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromHours(1);

    private readonly ConcurrentQueue<Action> queue = new();
    private readonly AutoResetEvent signal = new(false);
    private readonly Thread thread;
    private readonly IAppLog log;
    private Func<TimeSpan?> nextDelay = () => null;
    private Action wake = () => { };
    private volatile bool stopping;

    /// <summary>Creates the thread (not started).</summary>
    /// <param name="name">The thread name.</param>
    /// <param name="log">Where failed actions are logged.</param>
    public MusicThread(string name, IAppLog log)
    {
        this.log = log;
        thread = new Thread(Run) { Name = name, IsBackground = true, Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>Starts the loop.</summary>
    /// <param name="delayUntilWake">Returns the time until the owner's next timer (null: none); called on the thread.</param>
    /// <param name="onWake">Called on the thread when that time has come.</param>
    public void Start(Func<TimeSpan?> delayUntilWake, Action onWake)
    {
        nextDelay = delayUntilWake;
        wake = onWake;
        thread.Start();
    }

    /// <summary>Queues an action (any thread).</summary>
    /// <param name="action">The action.</param>
    public void Post(Action action)
    {
        queue.Enqueue(action);
        signal.Set();
    }

    /// <summary>Runs an action on the thread and waits for it (inline when the loop does not run).</summary>
    /// <param name="action">The action.</param>
    public void Invoke(Action action)
    {
        if (Thread.CurrentThread == thread || !thread.IsAlive || stopping)
        {
            Execute(action);
            return;
        }

        using var done = new ManualResetEventSlim();
        Post(() =>
        {
            try
            {
                action();
            }
            finally
            {
                done.Set();
            }
        });
        done.Wait();
    }

    /// <summary>Ends the loop once the queued actions ran, and waits for the thread.</summary>
    public void Dispose()
    {
        stopping = true;
        signal.Set();
        if (thread.IsAlive && Thread.CurrentThread != thread)
        {
            thread.Join();
        }

        signal.Dispose();
    }

    private void Run()
    {
        NativeMethods.DisablePowerThrottlingForCurrentThread();
        while (true)
        {
            while (queue.TryDequeue(out Action? action))
            {
                Execute(action);
            }

            if (stopping)
            {
                return;
            }

            TimeSpan? delay = nextDelay();
            if (delay is { } due && due <= TimeSpan.Zero)
            {
                Execute(wake);
                continue;
            }

            signal.WaitOne(delay is { } wait ? (wait < MaxWait ? wait : MaxWait) : Timeout.InfiniteTimeSpan);
        }
    }

    private void Execute(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            log.Error(LogSource, "A music command failed.", ex);
        }
    }
}
