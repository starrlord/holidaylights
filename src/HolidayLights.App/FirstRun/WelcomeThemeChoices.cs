namespace HolidayLights.App.FirstRun;

/// <summary>The theme cards of the Welcome card (PRODUCT-SPEC 3.11).</summary>
/// <param name="TodayTheme">The theme Automatic themes show today ("Halloween today" on the "Automatic" card).</param>
/// <param name="Themes">The cards after "Automatic": "Christmas 1", then the next two different calendar themes.</param>
public sealed record WelcomeThemeChoices(string TodayTheme, IReadOnlyList<string> Themes)
{
    /// <summary>How many upcoming calendar themes follow "Christmas 1".</summary>
    public const int UpcomingCount = 2;

    /// <summary>
    /// Chooses the cards: "Christmas 1" (the 2003 look, always offered), then the next two different holiday themes after
    /// today's, skipping "Christmas 1" and the between-holidays theme (on October 8 in the US: Thanksgiving, New Year).
    /// </summary>
    /// <param name="calendar">The calendar settings (its checked entries).</param>
    /// <param name="seasons">The calendar rules.</param>
    /// <param name="today">The local date.</param>
    /// <returns>The choices.</returns>
    public static WelcomeThemeChoices For(CalendarSettings calendar, ISeasonCalendar seasons, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(seasons);
        string todayTheme = seasons.Resolve(calendar, today).ThemeName;
        var themes = new List<string> { ShippedThemeNames.Christmas1 };
        DateOnly date = today;
        for (int step = 0; step < 32 && themes.Count < 1 + UpcomingCount; step++)
        {
            if (seasons.GetNextChange(calendar, date) is not { } change)
            {
                break;
            }

            date = change.Date;
            string name = change.Resolution.ThemeName;
            if (change.Resolution.EntryId is not null
                && !string.Equals(name, todayTheme, StringComparison.OrdinalIgnoreCase)
                && !themes.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                themes.Add(name);
            }
        }

        return new WelcomeThemeChoices(todayTheme, themes);
    }
}
