using System.Diagnostics;
using HolidayLights.Rendering.Engine;
using HolidayLights.Rendering.Threading;

namespace HolidayLights.Rendering;

/// <summary>
/// The desktop lights (ARCHITECTURE 5, PRODUCT-SPEC 5.1-5.14): a dedicated STA "Lights" thread owns the D3D11 and
/// DirectComposition devices, one layer window per enabled display in the requested layer mode with the (a) to (b) to (c)
/// fallback chain, desktop host discovery and its 2 s maintenance, sprite surfaces, the step clock, opacity animations
/// (fades, glow, Slow Glow, Dance), the power-up wave and theme transition, the on-screen pill, Identify, resting, and
/// device-lost and crash recovery. Owner: rendering.
/// </summary>
/// <remarks>
/// <para>Threading: every public member may be called from any thread; commands are queued to the Lights thread, which
/// never blocks (its layer windows can be children of Explorer's desktop). Events are raised on the Lights thread;
/// marshal them to the UI thread.</para>
/// <para>The presenter never draws while paused, hidden, or with lights off: no commits, no timer wake-ups (one 2 s
/// maintenance poll at most).</para>
/// </remarks>
public sealed class LightsPresenter : IStepClockSource, IDisposable, IEngineHost
{
    private const string LogSource = "Rendering";

    private readonly EngineDependencies dependencies;
    private readonly Lock gate = new();
    private LightsThreadHost? host;
    private SceneRequest? latestScene;
    private LightsPauseState latestPause = LightsPauseState.None;
    private LightsStatus status = LightsStatus.Initial;
    private StepClock clock = StepClock.Start(Stopwatch.GetTimestamp(), FlashSettings.DefaultInterval);
    private LightsDiagnostics diagnostics = LightsDiagnostics.Empty;
    private bool shutDown;

    /// <summary>Creates the presenter (no thread or window yet).</summary>
    /// <param name="bulbs">Resolves the bulbs of scenes.</param>
    /// <param name="sprites">Scaled sprites and glow halos.</param>
    /// <param name="flash">Creates the flash sequencer for each scene.</param>
    /// <param name="musicEvents">Music events for Dance to the Music (subscribed on the Lights thread).</param>
    /// <param name="options">Options.</param>
    /// <param name="log">The log.</param>
    public LightsPresenter(
        IBulbResolver bulbs,
        ISpriteProvider sprites,
        IFlashEngine flash,
        IMusicEventSource musicEvents,
        LightsPresenterOptions options,
        IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(bulbs);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(flash);
        ArgumentNullException.ThrowIfNull(musicEvents);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        dependencies = new EngineDependencies(bulbs, sprites, flash, musicEvents, options, log);
    }

    /// <summary>Raised on the Lights thread when the effective layer modes, resting or health changed.</summary>
    public event EventHandler<LightsStatusChangedEventArgs>? EffectiveModeChanged;

    /// <inheritdoc />
    public event EventHandler? ClockChanged;

    /// <summary>The latest status (thread-safe snapshot).</summary>
    public LightsStatus Status => Volatile.Read(ref status);

    /// <summary>The step clock the lights follow (restarted at step 0 when a pattern starts after a power-up or transition; speed changes at the next boundary).</summary>
    public StepClock Clock => Volatile.Read(ref clock);

    /// <summary>The latest diagnostics (thread-safe snapshot, refreshed after each scene and every 2 s while layer windows exist).</summary>
    public LightsDiagnostics Diagnostics => Volatile.Read(ref diagnostics);

    /// <inheritdoc />
    int IEngineHost.RestartCount => host?.RestartCount ?? 0;

    /// <summary>Starts the Lights thread. The first scene should follow at once with <see cref="Apply"/>.</summary>
    public void Start()
    {
        LightsThreadHost created;
        lock (gate)
        {
            if (host is not null || shutDown)
            {
                return;
            }

            created = new LightsThreadHost(dependencies, this, Replay);
            host = created;
        }

        // Outside the lock: the host takes its own lock, and a crash restart calls Replay (this lock) while holding it.
        Replay(created);
        created.Start();
    }

    /// <summary>Shows a scene (the latest call wins; affected displays only are rebuilt).</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="transition">How to get there (Automatic for edits; power-ups and the theme transition when asked).</param>
    public void Apply(LightsScene scene, SceneTransition transition = SceneTransition.Automatic)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var request = new SceneRequest(scene, transition);
        lock (gate)
        {
            latestScene = request;
            host?.Post(engine => engine.RequestScene(request));
        }
    }

    /// <summary>Applies the current pause state (resting displays fade out over 300 ms, or instantly under reduced motion; resuming fades in).</summary>
    /// <param name="state">What rests.</param>
    public void SetPaused(LightsPauseState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (gate)
        {
            latestPause = state;
            host?.Post(engine => engine.SetPause(state));
        }
    }

    /// <summary>"Try Again": retries the requested layer mode at once.</summary>
    public void RetryPreferredLayer() => Post(engine => engine.RetryPreferredLayer());

    /// <summary>Shows the on-screen pill on the display under the mouse pointer (PRODUCT-SPEC 3.12).</summary>
    /// <param name="request">Glyph and text.</param>
    public void ShowPill(PillRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Post(engine => engine.ShowPill(request));
    }

    /// <summary>"Identify": a large number on each display for the duration, click-through.</summary>
    /// <param name="displays">The displays and their numbers.</param>
    /// <param name="duration">How long (3 s).</param>
    public void ShowIdentify(IReadOnlyList<DisplayInfo> displays, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(displays);
        DisplayInfo[] copy = [.. displays];
        Post(engine => engine.ShowIdentify(copy, duration));
    }

    /// <summary>Fades the lights out (300 ms unless <paramref name="fadeOut"/> is false), destroys every window and the devices, and ends the thread.</summary>
    /// <param name="fadeOut">Fade out first (tray Exit); false for an immediate stop.</param>
    /// <returns>A task that completes when the Lights thread has ended.</returns>
    public Task ShutdownAsync(bool fadeOut = true)
    {
        LightsThreadHost? running;
        lock (gate)
        {
            shutDown = true;
            running = host;
        }

        return running?.StopAsync(fadeOut) ?? Task.CompletedTask;
    }

    /// <summary>Stops immediately (same as <see cref="ShutdownAsync"/> without fading, synchronously).</summary>
    public void Dispose()
    {
        LightsThreadHost? running;
        lock (gate)
        {
            shutDown = true;
            running = host;
        }

        running?.Dispose();
    }

    /// <summary>Tests: behave as if the graphics device was lost.</summary>
    internal void SimulateDeviceLoss() => Post(engine => engine.SimulateDeviceLoss());

    /// <summary>Tests: make the Lights thread fail as an unexpected exception would (it is restarted with the latest scene).</summary>
    internal void SimulateCrash() => Post(_ => throw new InvalidOperationException("A simulated failure of the Lights thread."));

    /// <summary>Tests: the hidden watcher window of the current Lights thread.</summary>
    internal Task<nint> GetWatcherWindowAsync()
    {
        var result = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Post(engine => result.TrySetResult(engine.WatcherWindow)))
        {
            result.TrySetResult(0);
        }

        return result.Task;
    }

    /// <summary>Tests: diagnostics taken now on the Lights thread (without waiting for the 2 s check, and without a commit).</summary>
    internal Task<LightsDiagnostics> GetDiagnosticsAsync()
    {
        var result = new TaskCompletionSource<LightsDiagnostics>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Post(engine =>
        {
            engine.RefreshDiagnostics();
            result.TrySetResult(Diagnostics);
        }))
        {
            result.TrySetResult(Diagnostics);
        }

        return result.Task;
    }

    /// <inheritdoc />
    void IEngineHost.PublishStatus(LightsStatus newStatus)
    {
        Volatile.Write(ref status, newStatus);
        Raise(() => EffectiveModeChanged?.Invoke(this, new LightsStatusChangedEventArgs(newStatus)));
    }

    /// <inheritdoc />
    void IEngineHost.PublishClock(StepClock newClock)
    {
        Volatile.Write(ref clock, newClock);
        Raise(() => ClockChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <inheritdoc />
    void IEngineHost.PublishDiagnostics(LightsDiagnostics newDiagnostics) => Volatile.Write(ref diagnostics, newDiagnostics);

    private bool Post(Action<LightsEngine> command)
    {
        lock (gate)
        {
            if (host is null || shutDown)
            {
                return false;
            }

            host.Post(command);
            return true;
        }
    }

    /// <summary>Posts the latest scene and pause state to a (re)started engine (crash recovery shows them without animation).</summary>
    private void Replay(LightsThreadHost target)
    {
        lock (gate)
        {
            LightsPauseState pause = latestPause;
            target.Post(engine => engine.SetPause(pause));
            if (latestScene is { } scene)
            {
                SceneRequest request = target.RestartCount > 0 ? scene with { Transition = SceneTransition.None } : scene;
                target.Post(engine => engine.RequestScene(request));
            }
        }
    }

    /// <summary>Runs an event handler; a failing subscriber is logged and never stops the Lights thread.</summary>
    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception exception)
        {
            dependencies.Log.Error(LogSource, "A lights event handler failed.", exception);
        }
    }
}
