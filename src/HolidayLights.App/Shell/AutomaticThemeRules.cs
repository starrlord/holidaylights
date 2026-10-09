using HolidayLights.Core.Seasons;

namespace HolidayLights.App.Shell;

/// <summary>What the Automatic theme scheduler does at an evaluation point.</summary>
public enum AutomaticThemeAction
{
    /// <summary>Nothing: Automatic themes are off, or the active calendar entry did not change.</summary>
    None,

    /// <summary>Apply the resolved theme now.</summary>
    Switch,

    /// <summary>A boundary was crossed while the Settings window is open: switch when it closes.</summary>
    Wait,
}

/// <summary>The outcome of one evaluation.</summary>
/// <param name="Action">What to do.</param>
/// <param name="Resolution">Today's calendar resolution (null when Automatic themes are off).</param>
/// <param name="ActiveEntryId">The entry to remember as applied (<see cref="CalendarEntryIds.Between"/> between holidays).</param>
public sealed record AutomaticThemeDecision(AutomaticThemeAction Action, CalendarResolution? Resolution, string? ActiveEntryId)
{
    /// <summary>Nothing to do.</summary>
    public static AutomaticThemeDecision None { get; } = new(AutomaticThemeAction.None, null, null);
}

/// <summary>
/// The switching rules of Automatic themes (PRODUCT-SPEC 5.11): only at boundaries (the active entry changed) or when
/// Automatic themes were just turned on; a boundary waits while the Settings window is open. Pure.
/// </summary>
public static class AutomaticThemeRules
{
    /// <summary>The Recent Settings label of the values an automatic switch replaces ("Before Halloween (automatic)").</summary>
    /// <param name="themeName">The theme that is applied.</param>
    /// <returns>The label.</returns>
    public static string RecentLabel(string themeName) => $"Before {themeName} (automatic)";

    /// <summary>Decides what to do now.</summary>
    /// <param name="calendar">The calendar settings.</param>
    /// <param name="seasons">The calendar rules.</param>
    /// <param name="today">The local date.</param>
    /// <param name="settingsOpen">The Settings window is open.</param>
    /// <param name="turnedOn">Automatic themes were just turned on (today's theme applies at once).</param>
    /// <returns>The decision.</returns>
    public static AutomaticThemeDecision Decide(CalendarSettings calendar, ISeasonCalendar seasons, DateOnly today, bool settingsOpen, bool turnedOn)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(seasons);
        if (!calendar.Enabled)
        {
            return AutomaticThemeDecision.None;
        }

        CalendarResolution resolution = seasons.Resolve(calendar, today);
        string activeId = resolution.EntryId ?? CalendarEntryIds.Between;
        if (turnedOn)
        {
            return new AutomaticThemeDecision(AutomaticThemeAction.Switch, resolution, activeId);
        }

        if (string.Equals(activeId, calendar.ActiveEntryId, StringComparison.OrdinalIgnoreCase))
        {
            return AutomaticThemeDecision.None;
        }

        return new AutomaticThemeDecision(settingsOpen ? AutomaticThemeAction.Wait : AutomaticThemeAction.Switch, resolution, activeId);
    }

    /// <summary>The theme to apply: the library's theme of that name, else its shipped original (a deleted shipped theme still shows).</summary>
    /// <param name="themes">The theme library.</param>
    /// <param name="name">The theme name.</param>
    /// <returns>The theme, or null when no such theme exists.</returns>
    public static ThemeDefinition? FindTheme(IThemeLibrary themes, string name)
    {
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(name);
        return themes.Find(name)
            ?? themes.ShippedOriginals.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The notification of an automatic switch (PRODUCT-SPEC 3.12): the theme's screen saver message as the title ("Happy Halloween!"), else "Holiday Lights".</summary>
    /// <param name="theme">The theme that was applied.</param>
    /// <returns>Title and text.</returns>
    public static (string Title, string Text) Notification(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        string message = (theme.Saver?.Message ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "";
        string title = message.Length == 0 ? "Holiday Lights" : message;
        return (title, $"Your lights switched to the {theme.Name} theme. Click to undo or choose another.");
    }
}
