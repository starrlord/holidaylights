using HolidayLights.App.Tray;

namespace HolidayLights.App.Shell;

/// <summary>Registers the two hot keys from settings, shows the pill on use, reports failures (see <see cref="IHotKeyController"/>). Owner: app-shell.</summary>
/// <remarks>
/// <para>Registration happens in the normal app only (PRODUCT-SPEC 6.6.4): at start, whenever <c>hotkeys</c> changes and
/// when the keyboard layouts change (the AltGr check). A combination another program owns is reported in General and, once
/// per change, as the "Hot key not available" notification.</para>
/// <para>The location hot key swaps On Desktop and On Top (pressed while the lights are off, it turns them on, on top);
/// the lights hot key toggles "Show Lights". Each use shows the on-screen pill; the first three uses of the location hot
/// key add "Press Ctrl+Alt+Shift+B again to put them back." for 4 s (3.12). UI thread.</para>
/// </remarks>
public sealed class HotKeyController : IHotKeyController, IDisposable
{
    /// <summary>How many times the location pill shows its second line.</summary>
    public const int LocationHintCount = 3;

    private const string LogSource = "Shell.HotKeys";
    private static readonly TimeSpan HintDuration = TimeSpan.FromSeconds(4);

    private readonly IAppServices services;
    private readonly IHotKeyService hotKeys;
    private readonly Action<PillRequest> showPill;
    private readonly bool registers;
    private readonly Dictionary<HotKeyAction, HotKeyRegistration?> registrations = new()
    {
        [HotKeyAction.SwitchLocation] = null,
        [HotKeyAction.ToggleLights] = null,
    };

    private bool started;
    private bool disposed;

    /// <summary>Creates the controller (registration only in normal sessions).</summary>
    /// <param name="services">The services.</param>
    /// <param name="hotKeys">The platform hot key service (the controller disposes it).</param>
    public HotKeyController(IAppServices services, IHotKeyService hotKeys)
        : this(services, hotKeys, request => (services?.Lights as LightsController)?.ShowPill(request))
    {
    }

    /// <summary>Creates the controller with the pill presenter of the composition root.</summary>
    /// <param name="services">The services.</param>
    /// <param name="hotKeys">The platform hot key service (the controller disposes it).</param>
    /// <param name="showPill">Shows the on-screen pill.</param>
    internal HotKeyController(IAppServices services, IHotKeyService hotKeys, Action<PillRequest> showPill)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(hotKeys);
        ArgumentNullException.ThrowIfNull(showPill);
        this.services = services;
        this.hotKeys = hotKeys;
        this.showPill = showPill;
        registers = services.Options.Session == AppSessionKind.Normal;
    }

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public IReadOnlyList<HotKeyStatus> Status
    {
        get
        {
            HotKeySettings settings = services.Settings.Current.HotKeys;
            return
            [
                new HotKeyStatus(HotKeyAction.SwitchLocation, settings.Location, registrations[HotKeyAction.SwitchLocation]),
                new HotKeyStatus(HotKeyAction.ToggleLights, settings.Lights, registrations[HotKeyAction.ToggleLights]),
            ];
        }
    }

    /// <summary>Registers the hot keys of the settings and follows their changes (normal sessions only).</summary>
    public void Start()
    {
        if (started || !registers)
        {
            return;
        }

        started = true;
        services.Settings.Changed += OnSettingsChanged;
        hotKeys.Pressed += OnPressed;
        hotKeys.KeyboardLayoutsChanged += OnKeyboardLayoutsChanged;
        RegisterAll();
    }

    /// <inheritdoc />
    public HotKeyCheck Validate(HotKeyBinding binding, HotKeyAction action)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return hotKeys.Validate(binding, action);
    }

    /// <summary>The location hot key's job with its pill (also <c>--toggle-layer</c> and the pipe's <c>toggle-layer</c>).</summary>
    public void SwitchLocation()
    {
        ISettingsStore store = services.Settings;
        SettingsActions.ToggleLocation(store.Current).ApplyTo(store);
        AppSettings now = store.Current;
        bool onTop = now.Lights.Drawing == BulbDrawing.OnTop;
        string? hint = null;
        HotKeyBinding location = now.HotKeys.Location;
        if (registrations[HotKeyAction.SwitchLocation] == HotKeyRegistration.Registered && now.Onboarding.LocationHotKeyHints < LocationHintCount)
        {
            hint = $"Press {location.Format("+")} again to put them back.";
            store.Update(s => s with { Onboarding = s.Onboarding with { LocationHotKeyHints = s.Onboarding.LocationHotKeyHints + 1 } }, SettingsChange.Internal);
        }

        showPill(onTop
            ? new PillRequest(PillGlyph.OnTop, "Bulbs on top of all windows", hint, hint is null ? default : HintDuration)
            : new PillRequest(PillGlyph.OnDesktop, "Bulbs on the desktop", hint, hint is null ? default : HintDuration));
    }

    /// <summary>Turns the lights on or off with the pill (the lights hot key, <c>--lights</c>).</summary>
    /// <param name="on">The new state.</param>
    public void SetLights(bool on)
    {
        SettingsActions.SetLightsOn(on).ApplyTo(services.Settings);
        showPill(on ? new PillRequest(PillGlyph.LightsOn, "Lights on") : new PillRequest(PillGlyph.LightsOff, "Lights off"));
    }

    /// <summary>Unregisters the hot keys.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (started)
        {
            services.Settings.Changed -= OnSettingsChanged;
            hotKeys.Pressed -= OnPressed;
            hotKeys.KeyboardLayoutsChanged -= OnKeyboardLayoutsChanged;
            hotKeys.Unregister(HotKeyAction.SwitchLocation);
            hotKeys.Unregister(HotKeyAction.ToggleLights);
        }

        hotKeys.Dispose();
    }

    private void RegisterAll()
    {
        HotKeySettings settings = services.Settings.Current.HotKeys;
        Register(HotKeyAction.SwitchLocation, settings.Location);
        Register(HotKeyAction.ToggleLights, settings.Lights);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Register(HotKeyAction action, HotKeyBinding binding)
    {
        if (!binding.Enabled)
        {
            hotKeys.Unregister(action);
            registrations[action] = null;
            ForgetReported(action);
            return;
        }

        HotKeyRegistration outcome = hotKeys.Register(action, binding);
        registrations[action] = outcome;
        if (outcome == HotKeyRegistration.Registered)
        {
            ForgetReported(action);
            return;
        }

        services.Log.Warn(LogSource, $"The hot key {binding.Format("+")} ({action}) is {outcome}.");
        ISettingsStore store = services.Settings;
        if (outcome == HotKeyRegistration.InUse && !NotificationThrottle.WasHotKeyReported(store.Current.Onboarding, action, binding)
            && services.Notifications.Show(NotificationKind.HotKeyUnavailable, "Hot key not available",
                $"{binding.Format("+")} is used by another program. Choose another one in General."))
        {
            // Once per change (PRODUCT-SPEC 3.12), remembered across sessions: not again at the next start or the next day.
            DateOnly today = DateOnly.FromDateTime(DateTime.Now);
            store.Update(s => s with { Onboarding = NotificationThrottle.RecordHotKeyReported(s.Onboarding, action, binding, today) }, SettingsChange.Internal);
        }
    }

    /// <summary>The action's combination works or is off: a later failure is a new change worth a notification.</summary>
    private void ForgetReported(HotKeyAction action)
    {
        ISettingsStore store = services.Settings;
        if (!ReferenceEquals(NotificationThrottle.ForgetHotKeyReported(store.Current.Onboarding, action), store.Current.Onboarding))
        {
            store.Update(s => s with { Onboarding = NotificationThrottle.ForgetHotKeyReported(s.Onboarding, action) }, SettingsChange.Internal);
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.HotKeys != e.NewSettings.HotKeys)
        {
            RegisterAll();
        }
    }

    private void OnKeyboardLayoutsChanged(object? sender, EventArgs e) => RegisterAll();

    private void OnPressed(object? sender, HotKeyPressedEventArgs e)
    {
        if (e.Action == HotKeyAction.SwitchLocation)
        {
            SwitchLocation();
        }
        else
        {
            SetLights(!services.Settings.Current.Lights.On);
        }
    }
}
