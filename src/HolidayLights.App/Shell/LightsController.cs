using System.Diagnostics;
using System.Windows.Threading;
using HolidayLights.Rendering;

namespace HolidayLights.App.Shell;

/// <summary>
/// Builds <see cref="LightsScene"/>s from settings, displays and pause signals (effective pattern, interval and look under
/// the flash limit and the energy-saver rules; seeds; transitions) and drives the presenter; aggregates pause signals
/// into <see cref="LightsPauseState"/> and music pause rules (see <see cref="ILightsController"/>). Owner: app-shell.
/// </summary>
/// <remarks>
/// <para>UI thread. The scene is rebuilt at once on every relevant change (settings, displays, Windows animation effects,
/// Energy Saver, a bulb in use changing), so previews reading <see cref="Scene"/> always match; unchanged rebuilds keep
/// the same scene instance and are not sent to the presenter. Transitions follow PRODUCT-SPEC 4.3.2-4.4 (the short
/// power-up when the lights come on, the theme transition when a theme changed what they show).</para>
/// <para>Before <see cref="Start"/> the controller only builds scenes; sessions without a presenter (settings-only,
/// screen saver) never start it and previews follow a free-running clock.</para>
/// </remarks>
public sealed class LightsController : ILightsController, IDisposable
{
    private const string LogSource = "Shell.Lights";
    private static readonly TimeSpan IdentifyDuration = TimeSpan.FromSeconds(3);

    private readonly IAppServices services;
    private readonly ISettingsStore settings;
    private readonly IDisplayService displays;
    private readonly ISystemInfo systemInfo;
    private readonly IBulbCatalog catalog;
    private readonly IAppLog log;
    private readonly Dispatcher dispatcher;
    private readonly SceneBuilder builder;
    private readonly PauseMonitor? pause;
    private readonly LightsPresenter? presenter;
    private readonly bool ownsPresenter;
    private readonly FreeRunningClock? freeClock;
    private LightsStatus status = LightsStatus.Initial;
    private long startTimestamp;
    private bool firstFrameLogged;
    private int relayoutPending;
    private bool started;
    private bool disposed;

    /// <summary>Creates the controller (no presenter in settings-only and screen saver sessions).</summary>
    /// <param name="services">The services.</param>
    public LightsController(IAppServices services)
        : this(services, null, PresenterFor(services), ownsPresenter: true)
    {
    }

    /// <summary>Creates the controller with the shared pause monitor and presenter of the composition root.</summary>
    /// <param name="services">The services.</param>
    /// <param name="pause">The pause aggregation, or null when nothing rests (no presenter).</param>
    /// <param name="presenter">The desktop lights, or null in sessions without lights.</param>
    /// <param name="ownsPresenter">True when the controller disposes the presenter.</param>
    internal LightsController(IAppServices services, PauseMonitor? pause, LightsPresenter? presenter, bool ownsPresenter)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
        this.pause = pause;
        this.presenter = presenter;
        this.ownsPresenter = ownsPresenter;
        settings = services.Settings;
        displays = services.Displays;
        systemInfo = services.SystemInfo;
        catalog = services.Bulbs;
        log = services.Log;
        dispatcher = Dispatcher.CurrentDispatcher;
        builder = new SceneBuilder(services.Layout, catalog);
        builder.Build(Inputs());
        freeClock = presenter is null ? new FreeRunningClock(builder.Current) : null;

        settings.Changed += OnSettingsChanged;
        displays.DisplaysChanged += OnDisplaysChanged;
        systemInfo.Changed += OnSystemInfoChanged;
        catalog.Changed += OnCatalogChanged;
        if (pause is not null)
        {
            pause.Changed += OnPauseChanged;
        }

        if (presenter is not null)
        {
            presenter.EffectiveModeChanged += OnPresenterStatus;
        }
    }

    /// <inheritdoc />
    public event EventHandler? SceneChanged;

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public LightsScene Scene => builder.Current;

    /// <inheritdoc />
    public LightsStatus Status => status;

    /// <inheritdoc />
    public IStepClockSource Clock => (IStepClockSource?)presenter ?? freeClock!;

    /// <summary>The presenter's internals for logs and "Copy Version Info", or null without a presenter.</summary>
    public LightsDiagnostics? Diagnostics => presenter?.Diagnostics;

    /// <summary>Starts the desktop lights with the first scene (lights first, PRODUCT-SPEC 1.2 #1).</summary>
    /// <param name="transition">The first-run, sign-in or short power-up.</param>
    /// <param name="processStartTimestamp">When the process started (<see cref="Stopwatch.GetTimestamp"/>), for the time-to-first-frame log (goal G1).</param>
    public void Start(SceneTransition transition, long processStartTimestamp)
    {
        if (started || disposed || presenter is null)
        {
            return;
        }

        started = true;
        startTimestamp = processStartTimestamp;
        if (pause is not null)
        {
            presenter.SetPaused(pause.Current.Lights);
        }

        presenter.Apply(builder.Current, transition);
        presenter.Start();
        log.Info(LogSource, $"Lights started: {DescribeScene(builder.Current)}, {transition}.");
    }

    /// <summary>Fades the lights out (300 ms, PRODUCT-SPEC 2.2 item 10) and stops the Lights thread.</summary>
    /// <returns>A task that completes when the lights are gone.</returns>
    public Task StopAsync() => presenter?.ShutdownAsync(fadeOut: true) ?? Task.CompletedTask;

    /// <inheritdoc />
    public void SetLightsOn(bool on) => SettingsActions.SetLightsOn(on).ApplyTo(settings);

    /// <inheritdoc />
    public void ToggleLocation() => SettingsActions.ToggleLocation(settings.Current).ApplyTo(settings);

    /// <inheritdoc />
    public void RetryPreferredLayer() => presenter?.RetryPreferredLayer();

    /// <inheritdoc />
    public void IdentifyDisplays() => presenter?.ShowIdentify(displays.Displays, IdentifyDuration);

    /// <summary>Shows the on-screen pill (hot key feedback) on the display under the mouse pointer.</summary>
    /// <param name="request">Glyph and text.</param>
    public void ShowPill(PillRequest request) => presenter?.ShowPill(request);

    /// <summary>Stops listening and stops the presenter at once.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        settings.Changed -= OnSettingsChanged;
        displays.DisplaysChanged -= OnDisplaysChanged;
        systemInfo.Changed -= OnSystemInfoChanged;
        catalog.Changed -= OnCatalogChanged;
        if (pause is not null)
        {
            pause.Changed -= OnPauseChanged;
        }

        if (presenter is not null)
        {
            presenter.EffectiveModeChanged -= OnPresenterStatus;
            if (ownsPresenter)
            {
                presenter.Dispose();
            }
        }
    }

    /// <summary>The presenter of a normal session; none in settings-only and screen saver sessions.</summary>
    private static LightsPresenter? PresenterFor(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Options.Session == AppSessionKind.Normal ? CreatePresenter(services) : null;
    }

    /// <summary>Creates the desktop lights presenter from the shared engines.</summary>
    /// <param name="services">The services.</param>
    /// <returns>The presenter (not started).</returns>
    internal static LightsPresenter CreatePresenter(IAppServices services) =>
        new(services.Bulbs, services.Sprites, services.Flash, services.Music.Events, new LightsPresenterOptions(), services.Log);

    private static string DescribeScene(LightsScene scene) =>
        $"{scene.Layout.Placements.Count} bulbs on {scene.Layout.Displays.Count} display(s), {scene.RequestedLayer}, " +
        $"{scene.Flash.Pattern} every {scene.Interval * StepClock.TickMilliseconds} ms, lights {(scene.LightsOn ? "on" : "off")}";

    private SceneInputs Inputs() =>
        new(settings.Current, displays.Displays, pause?.Signals.EnergySaverOn ?? false, systemInfo.AnimationsEnabled);

    private void Rebuild(SettingsChange? change, bool relayout)
    {
        if (disposed)
        {
            return;
        }

        LightsScene previous = builder.Current;
        LightsScene next = builder.Build(Inputs(), relayout);
        if (ReferenceEquals(previous, next))
        {
            return;
        }

        if (started)
        {
            presenter!.Apply(next, SceneRules.ChooseTransition(change, previous, next));
        }

        freeClock?.Follow(next);
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Rebuild(e.Change, relayout: false);

    private void OnDisplaysChanged(object? sender, DisplaysChangedEventArgs e)
    {
        log.Info(LogSource, $"Displays changed: {string.Join("; ", e.Current.Select(d => d.Describe()))}.");
        Rebuild(null, relayout: false);
    }

    private void OnSystemInfoChanged(object? sender, EventArgs e) => Rebuild(null, relayout: false);

    private void OnPauseChanged(object? sender, EventArgs e)
    {
        if (started)
        {
            presenter!.SetPaused(pause!.Current.Lights);
        }

        Rebuild(null, relayout: false);
    }

    /// <summary>A bulb in use was added, removed or rewritten: lay out again (on the UI thread, coalesced).</summary>
    private void OnCatalogChanged(object? sender, BulbCatalogChangedEventArgs e)
    {
        if (e.Change == BulbCatalogChange.IndexProgress || e.BulbIds.Count == 0)
        {
            return;
        }

        SlotAssignment arrangement = settings.Current.Current.Arrangement;
        if (!e.BulbIds.Any(arrangement.Contains) || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (Interlocked.Exchange(ref relayoutPending, 1) == 0)
        {
            dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref relayoutPending, 0);
                Rebuild(null, relayout: true);
            });
        }
    }

    private void OnPresenterStatus(object? sender, LightsStatusChangedEventArgs e)
    {
        if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvoke(() => ApplyStatus(e.Status));
        }
    }

    private void ApplyStatus(LightsStatus next)
    {
        if (disposed)
        {
            return;
        }

        LightsStatus previous = status;
        status = next;
        if (next.RestartCount > previous.RestartCount)
        {
            log.Warn(LogSource, $"The lights thread was restarted ({next.RestartCount} this session).");
            services.Notifications.Show(NotificationKind.LightsRestarted, "Lights restarted", "Something went wrong drawing your lights, so they were restarted.");
        }

        if (!firstFrameLogged && next.FirstFrameTimestamp is { } first && startTimestamp != 0)
        {
            firstFrameLogged = true;
            double milliseconds = Stopwatch.GetElapsedTime(startTimestamp, first).TotalMilliseconds;
            log.Info(LogSource, $"First lights committed {milliseconds:0} ms after the program started.");
        }

        if (next.Health != previous.Health || next.IsFallbackActive != previous.IsFallbackActive)
        {
            log.Info(LogSource, $"Lights {next.Health}; requested {next.Requested}; " +
                string.Join(", ", next.Displays.Select(d => $"{d.DisplayId}: {d.Effective?.ToString() ?? "none"}{(d.Resting ? " (resting)" : "")}")) + ".");
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
