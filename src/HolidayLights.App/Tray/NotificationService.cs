using HolidayLights.App.Shell;

namespace HolidayLights.App.Tray;

/// <summary>Tray balloon notifications with the spec's throttling (see <see cref="INotificationService"/>). Owner: app-shell.</summary>
/// <remarks>
/// <para>PRODUCT-SPEC 3.12: no buttons; at most one notification of each kind per day ("Your lights stay on" once ever;
/// "Music is waiting" and "Lights restarted" once per session); quiet time respected; never while a full-screen app,
/// a game, a presentation or a Focus session is in front. Clicking a notification opens the page of its kind.</para>
/// <para>UI thread. Without a tray icon (settings-only and screen saver sessions) nothing is shown.</para>
/// </remarks>
public sealed class NotificationService : INotificationService
{
    private const string LogSource = "Tray.Notifications";

    private readonly IAppServices services;
    private readonly Func<string, string, bool> showBalloon;
    private readonly Func<PauseState> pause;
    private readonly TimeProvider time;
    private readonly HashSet<NotificationKind> shownThisSession = [];
    private Action? clickAction;

    /// <summary>Creates the service.</summary>
    /// <param name="services">The services (settings for once-per-day bookkeeping, pause signals, window navigation).</param>
    /// <param name="tray">The tray icon that shows the balloons.</param>
    public NotificationService(IAppServices services, TrayIconController tray)
        : this(services, (title, text) => tray?.ShowNotification(title, text) ?? false, () => PauseState.None, TimeProvider.System)
    {
        ArgumentNullException.ThrowIfNull(tray);
    }

    /// <summary>Creates the service with the composition root's balloon (the tray icon is created later) and pause state.</summary>
    /// <param name="services">The services.</param>
    /// <param name="showBalloon">Shows a balloon on the tray icon; false when there is no icon.</param>
    /// <param name="pause">What rests now (full-screen apps and Focus sessions hold notifications back).</param>
    /// <param name="time">The clock (local days).</param>
    internal NotificationService(IAppServices services, Func<string, string, bool> showBalloon, Func<PauseState> pause, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(showBalloon);
        ArgumentNullException.ThrowIfNull(pause);
        ArgumentNullException.ThrowIfNull(time);
        this.services = services;
        this.showBalloon = showBalloon;
        this.pause = pause;
        this.time = time;
    }

    /// <inheritdoc />
    public bool Show(NotificationKind kind, string title, string text) => Show(kind, title, text, ClickActionFor(kind));

    /// <summary>The Automatic theme notification; clicking it opens Themes with "Holiday Lights switched to &lt;theme&gt; on &lt;date&gt;." [Undo].</summary>
    /// <param name="title">The theme's screen saver message, else "Holiday Lights".</param>
    /// <param name="text">"Your lights switched to the Halloween theme. Click to undo or choose another."</param>
    /// <param name="themeName">The theme that was applied.</param>
    /// <param name="date">The day of the switch.</param>
    /// <returns>True when it was shown.</returns>
    public bool ShowThemeSwitched(string title, string text, string themeName, DateOnly date) =>
        Show(NotificationKind.ThemeChanged, title, text,
            () => services.SettingsWindow.Show(SettingsPageId.Themes, new ThemeSwitchedRequest(themeName, date)));

    /// <summary>Opens the page of the last notification (the tray reports a click on it).</summary>
    public void OnClicked() => clickAction?.Invoke();

    private bool Show(NotificationKind kind, string title, string text, Action? onClick)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(text);
        DateOnly today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        ISettingsStore settings = services.Settings;
        if (pause().SuppressesNotifications
            || !NotificationThrottle.IsAllowed(kind, settings.Current.Onboarding, shownThisSession, today)
            || !showBalloon(title, text))
        {
            return false;
        }

        shownThisSession.Add(kind);
        clickAction = onClick;
        settings.Update(
            s =>
            {
                OnboardingState onboarding = NotificationThrottle.Record(kind, s.Onboarding, today);
                return ReferenceEquals(onboarding, s.Onboarding) ? s : s with { Onboarding = onboarding };
            },
            SettingsChange.Internal);
        services.Log.Info(LogSource, $"Notification shown: {kind}.");
        return true;
    }

    private Action? ClickActionFor(NotificationKind kind) => kind switch
    {
        NotificationKind.FirstClose => () => services.SettingsWindow.Show(SettingsPageId.Home),
        NotificationKind.ThemeChanged => () => services.SettingsWindow.Show(SettingsPageId.Themes),
        NotificationKind.HotKeyUnavailable or NotificationKind.SettingsReset => () => services.SettingsWindow.Show(SettingsPageId.General),
        NotificationKind.MusicWaiting => () => services.SettingsWindow.Show(SettingsPageId.MusicBox),
        _ => null,
    };
}
