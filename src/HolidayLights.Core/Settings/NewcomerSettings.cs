using HolidayLights.Core.Seasons;

namespace HolidayLights.Core.Settings;

/// <summary>Facts about this PC that first-run settings depend on.</summary>
/// <param name="Today">The local date (the Automatic theme of today).</param>
/// <param name="RegionCode">The Windows region (<see cref="ISystemInfo.RegionCode"/>): holiday rules and hemisphere.</param>
/// <param name="AnimationsEnabled">Windows "Animation effects": when off, "Limit Flashing" starts on (D13).</param>
/// <param name="Now">The time recorded with Recent Settings entries.</param>
public sealed record FirstRunContext(DateOnly Today, string RegionCode, bool AnimationsEnabled, DateTimeOffset Now);

/// <summary>
/// The newcomer settings of PRODUCT-SPEC 2.5.1 and 7.1: Automatic themes on with today's theme applied ("Halloween" on
/// October 8, "Classic Lights" between holidays), "Play Holiday Music" off, "Automatically Start Holiday Lights" on, the
/// calendar defaults of the region, "Limit Flashing" on when animation effects are off. "Start Fresh Instead" and "Reset
/// All Settings" start from these values too. Owner: core-settings.
/// </summary>
/// <remarks>
/// <para>Every setting returns to its default except what belongs to the user rather than to the look of the lights: the
/// history (Recent Settings, the 5.4 import record), one-time hints and notification throttling, the remembered Windows
/// screen saver, the Settings window placement, and the user's own data about bulbs, songs and pictures (favorites,
/// removed bundled items, category overrides) and custom colours.</para>
/// <para>Today's theme comes from the theme library; when the user deleted it, its shipped original is used, and when the
/// name is no theme at all, the 5.4 default values are kept. <see cref="CalendarSettings.ActiveEntryId"/> is set to
/// today's entry, so the Automatic theme scheduler does not switch again until the next boundary.</para>
/// </remarks>
public static class NewcomerSettings
{
    /// <summary>Creates the newcomer settings from <paramref name="baseline"/>.</summary>
    /// <param name="baseline">The settings to start from (<c>new AppSettings()</c> on a first run; the current settings for "Start Fresh Instead" and "Reset All Settings", whose themes, files, imports and history are kept).</param>
    /// <param name="context">Facts about this PC.</param>
    /// <param name="calendar">Resolves today's theme.</param>
    /// <param name="themes">The theme library (already loaded and seeded).</param>
    /// <param name="themeService">Applies today's theme.</param>
    /// <param name="recentLabel">When not null, the baseline's 13 values are recorded in Recent Settings under this label (e.g. "Before Reset").</param>
    /// <returns>The settings.</returns>
    public static AppSettings Create(
        AppSettings baseline,
        FirstRunContext context,
        ISeasonCalendar calendar,
        IThemeLibrary themes,
        IThemeService themeService,
        string? recentLabel = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(themeService);

        string region = CalendarRegions.Normalize(context.RegionCode);
        var calendarSettings = new CalendarSettings { Region = region, Entries = calendar.CreateDefaultEntries(region) };
        CalendarResolution today = calendar.Resolve(calendarSettings, context.Today);
        var fresh = new AppSettings
        {
            Current = baseline.Current,
            Calendar = calendarSettings with { ActiveEntryId = today.EntryId ?? CalendarEntryIds.Between },
            Accessibility = new AccessibilitySettings { LimitFlashing = !context.AnimationsEnabled },
            Saver = new SaverDeviceSettings { Previous = baseline.Saver.Previous },
            Colors = baseline.Colors,
            Ui = new UiSettings { Settings = new SettingsWindowSettings { Window = baseline.Ui.Settings.Window } },
            Bulbs = baseline.Bulbs,
            Songs = baseline.Songs,
            Pictures = baseline.Pictures,
            RecentSettings = baseline.RecentSettings,
            Import54 = baseline.Import54,
            Onboarding = baseline.Onboarding,
        };

        ThemeDefinition? theme = themes.Find(today.ThemeName)
            ?? themes.ShippedOriginals.FirstOrDefault(t => string.Equals(t.Name, today.ThemeName, StringComparison.OrdinalIgnoreCase));
        AppSettings result = theme is null
            ? fresh with { Current = new ThemeableSettings() }
            : themeService.Apply(fresh, theme, new ThemeApplyOptions(recentLabel ?? "", context.Now, TurnOffAutomaticThemes: false));

        // Recent Settings: exactly the baseline's history, plus the baseline's values when a label is given.
        return result with
        {
            RecentSettings = recentLabel is null
                ? baseline.RecentSettings
                : RecentSettingsHistory.Record(baseline.RecentSettings, recentLabel, context.Now, baseline.Current),
        };
    }
}
