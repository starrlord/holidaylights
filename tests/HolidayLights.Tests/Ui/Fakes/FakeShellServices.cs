using System.Diagnostics;
using HolidayLights.App.Preview;
using HolidayLights.Core.Flash;

namespace HolidayLights.Tests.Ui.Fakes;

/// <summary>A step clock that runs freely (as the presenter's would) and can be restarted.</summary>
public sealed class FakeClockSource : IStepClockSource
{
    /// <summary>Starts the clock now.</summary>
    /// <param name="interval">Ticks per step.</param>
    public FakeClockSource(int interval) => Clock = StepClock.Start(Stopwatch.GetTimestamp(), interval);

    /// <inheritdoc />
    public event EventHandler? ClockChanged;

    /// <inheritdoc />
    public StepClock Clock { get; private set; }

    /// <summary>Changes the speed at the next step boundary.</summary>
    /// <param name="interval">Ticks per step.</param>
    public void SetInterval(int interval)
    {
        if (interval == Clock.Interval)
        {
            return;
        }

        Clock = Clock.WithInterval(interval, Stopwatch.GetTimestamp());
        ClockChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// The scene builder in miniature: builds <see cref="LightsScene"/>s from the settings and the displays with the real layout
/// engine (as app-shell does), so previews and pages see the same scene the desktop would.
/// </summary>
public sealed class FakeLightsController : ILightsController
{
    private readonly ISettingsStore settings;
    private readonly IDisplayService displays;
    private readonly ILayoutEngine layout;
    private readonly IBulbResolver bulbs;
    private readonly FakeClockSource clock;
    private long revision;

    /// <summary>Creates the controller and builds the first scene.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="displays">The displays.</param>
    /// <param name="layout">The layout engine.</param>
    /// <param name="bulbs">Resolves bulbs.</param>
    public FakeLightsController(ISettingsStore settings, IDisplayService displays, ILayoutEngine layout, IBulbResolver bulbs)
    {
        this.settings = settings;
        this.displays = displays;
        this.layout = layout;
        this.bulbs = bulbs;
        clock = new FakeClockSource(settings.Current.Current.Flash.Interval);
        Scene = Build(settings.Current);
        settings.Changed += (_, e) => Rebuild();
        displays.DisplaysChanged += (_, _) => Rebuild();
    }

    /// <inheritdoc />
    public event EventHandler? SceneChanged;

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public LightsScene Scene { get; private set; }

    /// <inheritdoc />
    public LightsStatus Status { get; private set; } = LightsStatus.Initial with { Health = LightsHealth.Running };

    /// <inheritdoc />
    public IStepClockSource Clock => clock;

    /// <summary>How often Identify and Try Again were used.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Replaces the status (fallbacks, resting displays, no graphics device).</summary>
    /// <param name="status">The status.</param>
    public void SetStatus(LightsStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void SetLightsOn(bool on) =>
        settings.Update(s => s.Lights.On == on ? s : s with { Lights = s.Lights with { On = on } }, SettingsChange.Edit(on ? "Turn the lights on" : "Turn the lights off"));

    /// <inheritdoc />
    public void ToggleLocation() =>
        settings.Update(
            s => s with { Lights = s.Lights with { On = true, Drawing = s.Lights.Drawing == BulbDrawing.Desktop ? BulbDrawing.OnTop : BulbDrawing.Desktop } },
            SettingsChange.Edit("Switch where the bulbs are drawn"));

    /// <inheritdoc />
    public void RetryPreferredLayer() => Calls.Add("RetryPreferredLayer");

    /// <inheritdoc />
    public void IdentifyDisplays() => Calls.Add("IdentifyDisplays");

    private void Rebuild()
    {
        Scene = Build(settings.Current);
        clock.SetInterval(Scene.Interval);
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    private LightsScene Build(AppSettings s)
    {
        DisplayScene[] scenes = [.. displays.Displays.Select(d => new DisplayScene(d, !s.Lights.Displays.Disabled.Contains(d.DeviceId, StringComparer.Ordinal)))];
        if (!scenes.Any(d => d.Enabled) && scenes.Length > 0)
        {
            scenes[0] = scenes[0] with { Enabled = true };
        }

        LayoutTarget[] targets = [.. scenes.Where(d => d.Enabled).Select(d => PreviewLayouts.TargetFor(d.Display, s.Lights.Size))];
        bool limit = s.Accessibility.LimitFlashing;
        return new LightsScene
        {
            Revision = ++revision,
            LightsOn = s.Lights.On,
            RequestedLayer = LayerModes.FromSettings(s.Lights.Drawing, s.Lights.BehindIcons),
            Displays = scenes,
            Layout = layout.Layout(targets, s.Current.Arrangement, bulbs, s.Lights.FrameMode),
            Flash = new FlashOptions { Pattern = s.Current.Flash.Pattern, SmoothFading = s.Look.SmoothFading || limit, LimitFlashing = limit, PatternSeed = 42 },
            Interval = FlashClock.LimitInterval(s.Current.Flash.Interval, limit),
            Effects = new SceneEffects { Pixels = s.Look.Pixels, Glow = s.Look.Glow },
        };
    }
}

/// <summary>Hot keys whose registration always succeeds (or reports a configured verdict).</summary>
public sealed class FakeHotKeyController(ISettingsStore settings) : IHotKeyController
{
    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <summary>The verdict <see cref="Validate"/> returns.</summary>
    public HotKeyCheck Verdict { get; set; } = new(HotKeyValidity.Ok);

    /// <summary>The registration reported for enabled hot keys.</summary>
    public HotKeyRegistration Registration { get; set; } = HotKeyRegistration.Registered;

    /// <inheritdoc />
    public IReadOnlyList<HotKeyStatus> Status =>
    [
        new(HotKeyAction.SwitchLocation, settings.Current.HotKeys.Location, settings.Current.HotKeys.Location.Enabled ? Registration : null),
        new(HotKeyAction.ToggleLights, settings.Current.HotKeys.Lights, settings.Current.HotKeys.Lights.Enabled ? HotKeyRegistration.Registered : null),
    ];

    /// <inheritdoc />
    public HotKeyCheck Validate(HotKeyBinding binding, HotKeyAction action) => Verdict;

    /// <summary>Raises <see cref="StatusChanged"/>.</summary>
    public void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Notifications that are only recorded.</summary>
public sealed class FakeNotifications : INotificationService
{
    /// <summary>Every notification shown.</summary>
    public List<(NotificationKind Kind, string Title, string Text)> Shown { get; } = [];

    /// <inheritdoc />
    public bool Show(NotificationKind kind, string title, string text)
    {
        Shown.Add((kind, title, text));
        return true;
    }
}

/// <summary>Application commands that are only recorded.</summary>
public sealed class FakeAppShell : IAppShell
{
    /// <summary>Every call, e.g. "ShowHelp changing-bulbs".</summary>
    public List<string> Calls { get; } = [];

    /// <inheritdoc />
    public void ShowAbout() => Calls.Add("ShowAbout");

    /// <inheritdoc />
    public void ShowHelp(string? topicId = null) => Calls.Add($"ShowHelp {topicId}");

    /// <inheritdoc />
    public Task ExitAsync()
    {
        Calls.Add("Exit");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void StartUninstall() => Calls.Add("StartUninstall");
}

/// <summary>Screen saver sessions that only count.</summary>
public sealed class FakeSaverSessions : IScreenSaverSessions
{
    /// <inheritdoc />
    public bool IsSaverRunning { get; private set; }

    /// <inheritdoc />
    public void SaverStarted() => IsSaverRunning = true;

    /// <inheritdoc />
    public void SaverStopped() => IsSaverRunning = false;
}
