namespace HolidayLights.App.Contracts;

/// <summary>
/// The desktop lights as the rest of the app sees them (implemented by app-shell's scene builder around rendering's
/// presenter). UI thread; events are raised on the UI thread.
/// </summary>
public interface ILightsController
{
    /// <summary>
    /// The scene the desktop shows (built from settings, displays and pause rules: effective pattern, interval, look, layout
    /// of the enabled displays). Previews read it to match the desktop exactly. Available in every session kind.
    /// </summary>
    LightsScene Scene { get; }

    /// <summary>The presenter's status (fallback modes, resting displays, health).</summary>
    LightsStatus Status { get; }

    /// <summary>The step clock previews follow (the presenter's, or a free-running clock when there is none).</summary>
    IStepClockSource Clock { get; }

    /// <summary>Raised after <see cref="Scene"/> changed.</summary>
    event EventHandler? SceneChanged;

    /// <summary>Raised after <see cref="Status"/> changed.</summary>
    event EventHandler? StatusChanged;

    /// <summary>"Show Lights": changes <c>lights.on</c> (turning on plays the short power-up).</summary>
    /// <param name="on">The new state.</param>
    void SetLightsOn(bool on);

    /// <summary>The location hot key's job: On Desktop and On Top swap (persisted); with the lights off, turns them on, on top.</summary>
    void ToggleLocation();

    /// <summary>"Try Again": retries the requested layer mode.</summary>
    void RetryPreferredLayer();

    /// <summary>"Identify": shows each display's number on it for 3 s.</summary>
    void IdentifyDisplays();
}

/// <summary>Registration state of one hot key (General > Hot Keys).</summary>
/// <param name="Action">The hot key.</param>
/// <param name="Binding">Its setting.</param>
/// <param name="Registration">The outcome of the last registration, or null when the hot key is off.</param>
public sealed record HotKeyStatus(HotKeyAction Action, HotKeyBinding Binding, HotKeyRegistration? Registration);

/// <summary>Registers the hot keys from settings and reports their state (implemented by app-shell). UI thread.</summary>
public interface IHotKeyController
{
    /// <summary>One entry per <see cref="HotKeyAction"/>.</summary>
    IReadOnlyList<HotKeyStatus> Status { get; }

    /// <summary>Raised after a registration changed (settings change, keyboard layouts changed).</summary>
    event EventHandler? StatusChanged;

    /// <summary>Checks a candidate combination for the Change Hot Key dialog.</summary>
    /// <param name="binding">The combination.</param>
    /// <param name="action">The hot key it is for.</param>
    /// <returns>The verdict.</returns>
    HotKeyCheck Validate(HotKeyBinding binding, HotKeyAction action);
}

/// <summary>The tray notifications of PRODUCT-SPEC 3.12.</summary>
public enum NotificationKind
{
    /// <summary>"Your lights stay on" (first close of Settings ever; settings-ui triggers it).</summary>
    FirstClose,

    /// <summary>Automatic themes switched the theme (clicking opens Themes with the Undo InfoBar).</summary>
    ThemeChanged,

    /// <summary>"Hot key not available" (once per change; clicking opens General).</summary>
    HotKeyUnavailable,

    /// <summary>"Music is waiting" (MIDI synthesizer busy; once per session; clicking opens Music Box).</summary>
    MusicWaiting,

    /// <summary>"Settings were reset" (damaged settings file; clicking opens General).</summary>
    SettingsReset,

    /// <summary>"Lights restarted" (once per session).</summary>
    LightsRestarted,
}

/// <summary>
/// Tray balloon notifications (H.NotifyIcon; Windows 11 shows them as toasts), with the spec's throttling: at most one per
/// kind per day (or session), quiet time respected, never during a full-screen app or a Focus session. Implemented by
/// app-shell; clicking a notification opens the page of its kind.
/// </summary>
public interface INotificationService
{
    /// <summary>Shows a notification if its kind is not throttled.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="title">Title (sentence case).</param>
    /// <param name="text">Text.</param>
    /// <returns>True when it was shown.</returns>
    bool Show(NotificationKind kind, string title, string text);
}

/// <summary>Application-level commands (implemented by app-shell).</summary>
public interface IAppShell
{
    /// <summary>Opens (or brings forward) About Holiday Lights.</summary>
    void ShowAbout();

    /// <summary>Opens (or brings forward) Holiday Lights Help on a topic.</summary>
    /// <param name="topicId">A <see cref="HelpTopics"/> id, or null for Contents.</param>
    void ShowHelp(string? topicId = null);

    /// <summary>"Exit Holiday Lights": asks about unsaved Bulb Editing changes, keeps Settings changes, fades lights (300 ms) and music (500 ms) out, exits.</summary>
    /// <returns>A task that completes when the exit was cancelled (the user kept editing) or the app is about to end.</returns>
    Task ExitAsync();

    /// <summary>"Uninstall Holiday Lights..." after its confirmation: starts the per-user uninstaller (6.10) and exits.</summary>
    void StartUninstall();
}

/// <summary>
/// Tracks Holiday Lights screen saver sessions in the running app (implemented by app-shell): while a saver runs (the
/// external <c>/s</c> process announced over the pipe, or the in-process "Preview Screen Saver"), the desktop layers rest
/// and music follows "Play the Chosen Songs" as if the saver were showing.
/// </summary>
public interface IScreenSaverSessions
{
    /// <summary>True while a saver session is active.</summary>
    bool IsSaverRunning { get; }

    /// <summary>A saver session started.</summary>
    void SaverStarted();

    /// <summary>The saver session ended.</summary>
    void SaverStopped();
}
