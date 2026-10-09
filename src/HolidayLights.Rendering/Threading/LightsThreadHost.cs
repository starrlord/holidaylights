using System.Collections.Concurrent;
using System.Diagnostics;
using HolidayLights.Rendering.Engine;

namespace HolidayLights.Rendering.Threading;

/// <summary>
/// Runs the Lights thread: a dedicated STA thread (above-normal priority) whose <see cref="LightsEngine"/> is fed by a lock-free
/// command queue. A crash ends the thread; it is restarted with the latest scene and pause state at most once per minute
/// (PRODUCT-SPEC 5.13).
/// </summary>
internal sealed class LightsThreadHost : IDisposable
{
    private const string LogSource = "Rendering";

    private readonly EngineDependencies dependencies;
    private readonly IEngineHost engineHost;
    private readonly Action<LightsThreadHost> replay;
    private readonly ConcurrentQueue<Action<LightsEngine>> commands = new();
    private readonly LightsWaiter waiter = new();
    private readonly RestartPolicy restartPolicy = new();
    private readonly DeviceRecoveryPolicy devicePolicy;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();
    private Thread? thread;
    private Timer? restartTimer;
    private bool stopRequested;
    private int restartCount;

    /// <summary>Creates the host (no thread yet).</summary>
    /// <param name="dependencies">The engine's services.</param>
    /// <param name="engineHost">Receives status, clock and diagnostics.</param>
    /// <param name="replay">Posts the latest scene and pause state to a (re)started engine.</param>
    public LightsThreadHost(EngineDependencies dependencies, IEngineHost engineHost, Action<LightsThreadHost> replay)
    {
        this.dependencies = dependencies;
        this.engineHost = engineHost;
        this.replay = replay;
        devicePolicy = new DeviceRecoveryPolicy(dependencies.Options.ForceSoftwareRendering);
    }

    /// <summary>How often the thread was restarted after a crash.</summary>
    public int RestartCount => Volatile.Read(ref restartCount);

    /// <summary>A task that completes when the thread has ended for good.</summary>
    public Task Stopped => stopped.Task;

    /// <summary>Starts the thread.</summary>
    public void Start()
    {
        lock (gate)
        {
            StartThread();
        }
    }

    /// <summary>Queues a command for the engine (any thread).</summary>
    public void Post(Action<LightsEngine> command)
    {
        commands.Enqueue(command);
        waiter.Signal();
    }

    /// <summary>Stops the thread (after the engine's fade-out when <paramref name="fadeOut"/>).</summary>
    public Task StopAsync(bool fadeOut)
    {
        lock (gate)
        {
            stopRequested = true;
            restartTimer?.Dispose();
            restartTimer = null;
            if (thread is { IsAlive: true })
            {
                Post(engine => engine.Stop(fadeOut));
            }
            else
            {
                stopped.TrySetResult();
            }
        }

        return stopped.Task;
    }

    /// <summary>Stops at once and waits up to 5 s for the thread.</summary>
    public void Dispose()
    {
        StopAsync(fadeOut: false);
        Thread? running;
        lock (gate)
        {
            running = thread;
        }

        if (running is not null && running != Thread.CurrentThread)
        {
            running.Join(TimeSpan.FromSeconds(5));
        }

        waiter.Dispose();
    }

    private void StartThread()
    {
        var started = new Thread(Run)
        {
            Name = "Holiday Lights",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        started.SetApartmentState(ApartmentState.STA);
        thread = started;
        started.Start();
    }

    private void Run()
    {
        var engine = new LightsEngine(dependencies, engineHost, commands, waiter, devicePolicy);
        try
        {
            engine.Run();
            stopped.TrySetResult();
        }
        catch (Exception exception)
        {
            OnCrash(exception);
        }
    }

    private void OnCrash(Exception exception)
    {
        dependencies.Log.Error(LogSource, "The Lights thread failed.", exception);
        lock (gate)
        {
            if (stopRequested)
            {
                stopped.TrySetResult();
                return;
            }

            TimeSpan delay = restartPolicy.DelayBeforeRestart(Stopwatch.GetTimestamp());
            restartTimer = new Timer(_ => Restart(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Restart()
    {
        lock (gate)
        {
            restartTimer?.Dispose();
            restartTimer = null;
            if (stopRequested)
            {
                stopped.TrySetResult();
                return;
            }

            restartPolicy.RecordRestart(Stopwatch.GetTimestamp());
            Interlocked.Increment(ref restartCount);
            commands.Clear();
            replay(this);
            dependencies.Log.Warn(LogSource, $"Restarting the Lights thread (restart {restartCount}).");
            StartThread();
        }
    }
}
