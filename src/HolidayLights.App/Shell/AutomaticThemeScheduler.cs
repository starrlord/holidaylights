using System.Windows.Threading;
using HolidayLights.App.Tray;
using Microsoft.Win32;

namespace HolidayLights.App.Shell;

/// <summary>
/// Automatic themes in the running app (PRODUCT-SPEC 5.11): evaluates the Theme Calendar at program start, at local
/// midnight, after resume from sleep, on time-zone or clock changes, when the calendar is edited and when Automatic themes
/// are turned on; switches only at boundaries, waits while the Settings window is open, records Recent Settings
/// ("Before Halloween (automatic)"), uses the theme transition and shows one notification per switch. UI thread.
/// </summary>
public sealed class AutomaticThemeScheduler : IDisposable
{
    private const string LogSource = "Shell.Themes";

    private readonly IAppServices services;
    private readonly NotificationService? notifications;
    private readonly TimeProvider time;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer midnight;
    private bool waiting;
    private bool started;
    private bool disposed;

    /// <summary>Creates the scheduler; call <see cref="Start"/>.</summary>
    /// <param name="services">The services (settings, calendar, themes, the Settings window).</param>
    /// <param name="notifications">Shows the theme-change notification, or null in sessions without a tray.</param>
    /// <param name="time">The clock (local dates).</param>
    public AutomaticThemeScheduler(IAppServices services, NotificationService? notifications, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(time);
        this.services = services;
        this.notifications = notifications;
        this.time = time;
        dispatcher = Dispatcher.CurrentDispatcher;
        midnight = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        midnight.Tick += OnMidnight;
    }

    /// <summary>True while a boundary waits for the Settings window to close.</summary>
    public bool IsWaiting => waiting;

    /// <summary>Starts watching the clock, the calendar and the Settings window, and evaluates now.</summary>
    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        services.Settings.Changed += OnSettingsChanged;
        services.SettingsWindow.Closed += OnSettingsWindowClosed;
        SystemEvents.TimeChanged += OnSystemClockChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Evaluate(turnedOn: false);
        ArmMidnightTimer();
    }

    /// <summary>Evaluates the calendar now and switches the theme when a boundary was crossed.</summary>
    /// <param name="turnedOn">Automatic themes were just turned on: today's theme applies even inside the same entry.</param>
    /// <returns>The decision that was taken.</returns>
    public AutomaticThemeDecision Evaluate(bool turnedOn)
    {
        if (disposed)
        {
            return AutomaticThemeDecision.None;
        }

        DateTimeOffset now = time.GetLocalNow();
        AppSettings current = services.Settings.Current;
        AutomaticThemeDecision decision = AutomaticThemeRules.Decide(
            current.Calendar, services.Calendar, DateOnly.FromDateTime(now.DateTime), services.SettingsWindow.IsOpen, turnedOn);
        waiting = decision.Action == AutomaticThemeAction.Wait;
        if (decision.Action == AutomaticThemeAction.Switch)
        {
            Switch(decision, now, notify: !turnedOn);
        }
        else if (waiting)
        {
            services.Log.Info(LogSource, $"The calendar changed to {decision.Resolution!.ThemeName}; waiting for the Settings window to close.");
        }

        return decision;
    }

    /// <summary>Stops watching.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        midnight.Stop();
        if (started)
        {
            services.Settings.Changed -= OnSettingsChanged;
            services.SettingsWindow.Closed -= OnSettingsWindowClosed;
            SystemEvents.TimeChanged -= OnSystemClockChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }

    private void Switch(AutomaticThemeDecision decision, DateTimeOffset now, bool notify)
    {
        CalendarResolution resolution = decision.Resolution!;
        string activeId = decision.ActiveEntryId!;
        ThemeDefinition? theme = AutomaticThemeRules.FindTheme(services.Themes, resolution.ThemeName);
        if (theme is null)
        {
            services.Log.Warn(LogSource, $"The calendar asks for the theme \"{resolution.ThemeName}\", which does not exist.");
            services.Settings.Update(s => s with { Calendar = s.Calendar with { ActiveEntryId = activeId } }, SettingsChange.Internal);
            return;
        }

        var options = new ThemeApplyOptions(AutomaticThemeRules.RecentLabel(theme.Name), now, TurnOffAutomaticThemes: false);
        services.Settings.Update(
            s =>
            {
                AppSettings applied = services.ThemeService.Apply(s, theme, options);
                return applied with { Calendar = applied.Calendar with { ActiveEntryId = activeId } };
            },
            new SettingsChange(SettingsChangeKind.AutomaticTheme, $"Switch to the {theme.Name} theme"));
        services.Log.Info(LogSource, $"Automatic themes switched to {theme.Name} ({activeId}).");

        if (notify && services.Settings.Current.Calendar.Notify && notifications is not null)
        {
            (string title, string text) = AutomaticThemeRules.Notification(theme);
            notifications.ShowThemeSwitched(title, text, theme.Name, DateOnly.FromDateTime(now.DateTime));
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        CalendarSettings before = e.OldSettings.Calendar;
        CalendarSettings after = e.NewSettings.Calendar;
        bool turnedOn = !before.Enabled && after.Enabled;
        bool edited = after.Enabled && e.Change.Kind != SettingsChangeKind.AutomaticTheme
            && (!before.Entries.SequenceEqual(after.Entries) || !string.Equals(before.Region, after.Region, StringComparison.Ordinal)
                || !string.Equals(before.Between, after.Between, StringComparison.Ordinal));
        if (turnedOn || edited)
        {
            // After the change that triggered it has reached every subscriber.
            dispatcher.BeginInvoke(() => Evaluate(turnedOn));
        }
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        if (waiting)
        {
            Evaluate(turnedOn: false);
        }
    }

    private void OnMidnight(object? sender, EventArgs e)
    {
        Evaluate(turnedOn: false);
        ArmMidnightTimer();
    }

    private void OnSystemClockChanged(object? sender, EventArgs e) => dispatcher.BeginInvoke(() =>
    {
        TimeZoneInfo.ClearCachedData();
        Evaluate(turnedOn: false);
        ArmMidnightTimer();
    });

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            dispatcher.BeginInvoke(() =>
            {
                Evaluate(turnedOn: false);
                ArmMidnightTimer();
            });
        }
    }

    private void ArmMidnightTimer()
    {
        if (disposed)
        {
            return;
        }

        DateTimeOffset now = time.GetUtcNow();
        midnight.Stop();
        midnight.Interval = NextLocalMidnight(now, time.LocalTimeZone) - now + TimeSpan.FromSeconds(1);
        midnight.Start();
    }

    /// <summary>The next local midnight after a moment (daylight saving time included; a midnight that does not exist moves to the next valid time).</summary>
    /// <param name="utcNow">The moment.</param>
    /// <param name="zone">The local time zone.</param>
    /// <returns>The moment of the next midnight.</returns>
    internal static DateTimeOffset NextLocalMidnight(DateTimeOffset utcNow, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(utcNow, zone).DateTime;
        DateTime midnightLocal = local.Date.AddDays(1);
        while (zone.IsInvalidTime(midnightLocal))
        {
            midnightLocal = midnightLocal.AddMinutes(30);
        }

        return new DateTimeOffset(midnightLocal, zone.GetUtcOffset(midnightLocal));
    }
}
