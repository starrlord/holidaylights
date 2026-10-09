using System.Collections.Concurrent;
using System.Diagnostics;
using HolidayLights.Rendering.Animation;
using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Interop;
using HolidayLights.Rendering.Layers;
using HolidayLights.Rendering.Overlays;
using HolidayLights.Rendering.Presentation;
using HolidayLights.Rendering.Scenes;
using HolidayLights.Rendering.Shell;
using HolidayLights.Rendering.Threading;

namespace HolidayLights.Rendering.Engine;

/// <summary>
/// The brain of the Lights thread (ARCHITECTURE 3 and 5). One instance lives on one dedicated STA thread and owns everything
/// there: the composition device, the layer windows and their visuals, the hidden watcher window, the step clock and the
/// animator. It sleeps in <see cref="LightsWaiter"/> until the next deadline, a queued command or a window message, and never
/// blocks. This file holds the loop; the other parts handle scenes, visibility, layer windows and the device.
/// </summary>
internal sealed partial class LightsEngine : ILightsThreadSink
{
    private const string LogSource = "Rendering";

    private static readonly long MaintenanceInterval = Ticks(2_000);
    private static readonly long MusicPollInterval = Ticks(DanceEnvelope.BatchMilliseconds);
    private static readonly long TopologyDebounce = Ticks(300);

    /// <summary>Repeating animations are created again this often (the same picture; none runs in DWM for days).</summary>
    private static readonly long RepeatRefreshInterval = Ticks(30 * 60_000);

    private readonly EngineDependencies dependencies;
    private readonly IEngineHost host;
    private readonly ConcurrentQueue<Action<LightsEngine>> commands;
    private readonly LightsWaiter waiter;
    private readonly ScenePreparer preparer;
    private readonly DeviceRecoveryPolicy devicePolicy;
    private readonly CancellationTokenSource lifetime = new();
    private readonly VisualRouter router = new();
    private readonly Dictionary<string, DisplayLayer> layers = new(StringComparer.Ordinal);
    private readonly ClockKeeper clock;
    private readonly long startedAt = Stopwatch.GetTimestamp();

    private bool dirty;
    private long busyTicks;
    private long nextMaintenance;
    private long nextRepeatRefresh;
    private long? topologyCheckAt;
    private long? layerWake;
    private long? overlayWake;
    private Exception? failure;
    private long? stopAt;
    private bool stopping;

    /// <summary>Creates the engine (call <see cref="Run"/> on the Lights thread).</summary>
    public LightsEngine(
        EngineDependencies dependencies, IEngineHost host, ConcurrentQueue<Action<LightsEngine>> commands, LightsWaiter waiter, DeviceRecoveryPolicy devicePolicy)
    {
        this.dependencies = dependencies;
        this.host = host;
        this.commands = commands;
        this.waiter = waiter;
        this.devicePolicy = devicePolicy;
        preparer = new ScenePreparer(dependencies.Bulbs, dependencies.Sprites, dependencies.Flash, dependencies.Log);
        clock = new ClockKeeper(startedAt, FlashSettings.DefaultInterval);
    }

    private IAppLog Log => dependencies.Log;

    private static long Now => Stopwatch.GetTimestamp();

    /// <summary>Runs the loop until <see cref="Stop"/> has finished. Exceptions end the thread (the presenter restarts it).</summary>
    public void Run()
    {
        try
        {
            Initialize();
            while (stopAt is not { } stop || Now < stop)
            {
                waiter.Wait(NextDue(Now));
                long awake = Now;
                PumpMessages();
                RunCommands();
                Tick(Now);
                Flush(Now);
                busyTicks += Now - awake;
            }
        }
        finally
        {
            Teardown();
        }
    }

    /// <summary>Ends the loop, after a 300 ms fade-out when <paramref name="fadeOut"/> and something is visible.</summary>
    public void Stop(bool fadeOut)
    {
        long now = Now;
        stopping = true;
        bool fade = fadeOut && Scene is { Effects.ReducedMotion: false } && AnyLayerVisible;
        double milliseconds = fade ? MotionTimings.LightsOffFade : 0;
        if (batch is not null)
        {
            foreach (DisplayLayer layer in layers.Values)
            {
                layer.FadeOutAndHide(now, milliseconds, batch);
            }

            dirty = true;
        }

        stopAt = now + Ticks(milliseconds);
    }

    /// <inheritdoc />
    public void ReportFailure(Exception exception) => failure ??= exception;

    /// <summary>Converts milliseconds to <see cref="Stopwatch"/> ticks.</summary>
    internal static long Ticks(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000);

    private static long? Earliest(long? a, long? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    private void Initialize()
    {
        User32.SetThreadDpiAwarenessContext(Win32Constants.DpiAwarenessContextPerMonitorAwareV2);
        OptOutOfPowerThrottling();
        LightsThreadContext.Attach(this);
        WindowClasses.EnsureRegistered();
        CreateWatcherWindow();
        InstallForegroundHook();
        desktops = VirtualDesktopManager.TryCreate();
        topology = MonitorTopology.Capture();
        nextMaintenance = Now + MaintenanceInterval;
        nextRepeatRefresh = Now + RepeatRefreshInterval;
        TryCreateDevice(Now);
    }

    private void Teardown()
    {
        lifetime.Cancel();
        DisposeMusicReader();
        DestroyComposition();
        advancedColor.Dispose();
        RemoveForegroundHook();
        DestroyWatcherWindow();
        desktops?.Dispose();
        desktops = null;
        health = LightsHealth.Stopped;
        PublishStatus(force: true);
        PublishDiagnostics();
        LightsThreadContext.Detach();
        lifetime.Dispose();
    }

    private void PumpMessages()
    {
        while (User32.PeekMessageW(out MSG message, 0, 0, 0, Win32Constants.PmRemove))
        {
            User32.TranslateMessage(in message);
            User32.DispatchMessageW(in message);
            ThrowIfFailed();
        }
    }

    private void RunCommands()
    {
        while (commands.TryDequeue(out Action<LightsEngine>? command))
        {
            command(this);
            ThrowIfFailed();
        }
    }

    private void Post(Action<LightsEngine> command)
    {
        commands.Enqueue(command);
        waiter.Signal();
    }

    private void ThrowIfFailed()
    {
        if (failure is { } error)
        {
            failure = null;
            throw new LightsThreadException("A Lights thread callback failed.", error);
        }
    }

    private long? NextDue(long now)
    {
        long? due = stopAt;
        due = Earliest(due, clock.NextDue(AnyLayerVisible && (animator?.NeedsSteps ?? false)));
        if (MusicWanted)
        {
            due = Earliest(due, nextMusicPoll);
        }

        if (MaintenanceWanted)
        {
            due = Earliest(due, nextMaintenance);
        }

        due = Earliest(due, topologyCheckAt);
        due = Earliest(due, layerWake);
        due = Earliest(due, overlayWake);
        due = Earliest(due, ShellDeadline);
        due = Earliest(due, deviceRetryAt);
        return due is { } value ? Math.Max(value, now) : null;
    }

    private void Tick(long now)
    {
        if (device is null)
        {
            if (deviceRetryAt is { } retry && now >= retry)
            {
                TryCreateDevice(now);
            }

            return;
        }

        if (clock.StartDue(now))
        {
            StartPatternAfterPowerUp();
        }

        if (clock.TakeSpeedChange(now) is { } changedAt && animator is not null)
        {
            animator.ChangeSpeed(clock.TimingAt(changedAt), router);
            dirty = true;
        }

        if (AnyLayerVisible && animator is { NeedsSteps: true } && clock.DueStep(now) is { } step)
        {
            ProcessStep(step, now);
        }

        if (MusicWanted && now >= nextMusicPoll)
        {
            PollMusic(now);
        }

        if (now >= nextRepeatRefresh)
        {
            RefreshRepeatingAnimations(now);
        }

        if (topologyCheckAt is { } check && now >= check)
        {
            topologyCheckAt = null;
            Maintain(now, topologyEvent: true);
        }
        else if (now >= nextMaintenance)
        {
            Maintain(now, topologyEvent: false);
        }

        TickShell(now);
        TickLayers(now);
    }

    private void TickLayers(long now)
    {
        if (batch is null)
        {
            return;
        }

        bool changed = false;
        long? next = null;
        foreach (DisplayLayer layer in layers.Values)
        {
            next = Earliest(next, layer.Tick(now, batch, ref changed));
        }

        if (CompleteLayerMoves(now))
        {
            // The moved layers fade in again in their new windows: tick once more (a no-op for what is already done) so the
            // next wake-up includes the end of those fades.
            changed = true;
            next = null;
            foreach (DisplayLayer layer in layers.Values)
            {
                next = Earliest(next, layer.Tick(now, batch, ref changed));
            }
        }

        foreach ((DisplayLayer layer, _) in retiringLayers)
        {
            next = Earliest(next, layer.Tick(now, batch, ref changed));
        }

        int retired = retiringLayers.Count;
        layerWake = Earliest(next, ReleaseRetiredLayers(now));
        changed |= retiringLayers.Count != retired;
        if (overlays is not null && overlayWake is { } due && now >= due)
        {
            overlayWake = overlays.Tick(now, batch, ref changed);
        }

        dirty |= changed;
    }

    private void Flush(long now)
    {
        if (dirty && device is not null && batch is not null)
        {
            dirty = false;
            bool committed = device.TryCommit();
            batch.ReleaseAll();
            if (!committed)
            {
                OnDeviceLost(now, "the commit failed");
            }
            else
            {
                commitCount++;
                if (firstFrame is null && layers.Values.Any(l => l.IsVisible && l.Scene is { Bulbs.Count: > 0 }))
                {
                    firstFrame = Now;
                    Log.Info(LogSource, $"First lights shown {(firstFrame.Value - startedAt) * 1000.0 / Stopwatch.Frequency:F0} ms after the Lights thread started.");
                }
            }
        }

        PublishStatus(force: false);
    }

    /// <summary>
    /// Re-creates the repeating animations every half hour while lights show (checked at any wake-up; maintenance wakes the
    /// thread every 2 s while layers exist).
    /// </summary>
    private void RefreshRepeatingAnimations(long now)
    {
        nextRepeatRefresh = now + RepeatRefreshInterval;
        if (AnyLayerVisible && clock.IsRunning && animator is not null)
        {
            animator.RefreshRepeating(now, clock.TimingAt(now), router);
            dirty = true;
        }
    }

    private void ProcessStep(long step, long now)
    {
        long boundary = clock.Clock.TimestampOfStep(step);
        maxStepLatency = Math.Max(maxStepLatency, Math.Max(0, (now - boundary) * 1000.0 / Stopwatch.Frequency));
        animator!.Step(step, boundary, clock.TimingAt(now), router);
        clock.MarkShown(step);
        stepCount++;
        dirty = true;
    }

    private static unsafe void OptOutOfPowerThrottling()
    {
        // Step timing must stay exact: the Lights thread never runs under EcoQoS (PRODUCT-SPEC 5.14, risk R8).
        var state = new THREAD_POWER_THROTTLING_STATE
        {
            Version = Win32Constants.ThreadPowerThrottlingCurrentVersion,
            ControlMask = Win32Constants.ThreadPowerThrottlingExecutionSpeed,
            StateMask = 0,
        };
        Kernel32.SetThreadInformation(Kernel32.GetCurrentThread(), Win32Constants.ThreadPowerThrottling, &state, (uint)sizeof(THREAD_POWER_THROTTLING_STATE));
    }
}

/// <summary>The Lights thread failed; the presenter restarts it (PRODUCT-SPEC 5.13).</summary>
internal sealed class LightsThreadException : Exception
{
    /// <summary>Creates the exception.</summary>
    public LightsThreadException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
