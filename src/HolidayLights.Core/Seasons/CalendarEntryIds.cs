namespace HolidayLights.Core.Seasons;

/// <summary>The stable ids of the 13 Theme Calendar rows (<see cref="CalendarEntry.Id"/>, PRODUCT-SPEC 5.11).</summary>
public static class CalendarEntryIds
{
    /// <summary>"New Year" (Dec 31 - Jan 1).</summary>
    public const string NewYear = "newYear";

    /// <summary>"Valentine's Day" (Feb 1 - Feb 14).</summary>
    public const string ValentinesDay = "valentinesDay";

    /// <summary>"St. Patrick's Day" (Mar 10 - Mar 17).</summary>
    public const string StPatricksDay = "stPatricksDay";

    /// <summary>"Easter" (14 days before Easter Sunday to Easter Monday).</summary>
    public const string Easter = "easter";

    /// <summary>"July 4th" (Jul 1 - Jul 5).</summary>
    public const string July4th = "july4th";

    /// <summary>"Halloween" (Oct 1 - Oct 31).</summary>
    public const string Halloween = "halloween";

    /// <summary>"Thanksgiving" (US: Nov 1 to Thanksgiving Day; Canada: the week up to the second Monday of October).</summary>
    public const string Thanksgiving = "thanksgiving";

    /// <summary>"Christmas" (US: the day after Thanksgiving to Dec 30; elsewhere Nov 25 - Dec 30).</summary>
    public const string Christmas = "christmas";

    /// <summary>"Chanukah" (the eight nights).</summary>
    public const string Chanukah = "chanukah";

    /// <summary>"Winter".</summary>
    public const string Winter = "winter";

    /// <summary>"Spring".</summary>
    public const string Spring = "spring";

    /// <summary>"Summer".</summary>
    public const string Summer = "summer";

    /// <summary>"Autumn".</summary>
    public const string Autumn = "autumn";

    /// <summary>The value of <see cref="CalendarSettings.ActiveEntryId"/> while the between-holidays theme is shown.</summary>
    public const string Between = "between";

    /// <summary>The 13 rows in display order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [NewYear, ValentinesDay, StPatricksDay, Easter, July4th, Halloween, Thanksgiving, Christmas, Chanukah, Winter, Spring, Summer, Autumn];
}
