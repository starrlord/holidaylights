using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>How a calendar entry computes its dates (Appendix C rule types).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalendarRuleType>))]
public enum CalendarRuleType
{
    /// <summary>Fixed month-day range <see cref="CalendarRule.From"/> to <see cref="CalendarRule.To"/> (inclusive; may cross the new year).</summary>
    [JsonStringEnumMemberName("fixed")]
    Fixed,

    /// <summary>Offsets in days from Easter Sunday (<see cref="CalendarRule.FromOffset"/>, <see cref="CalendarRule.ToOffset"/>); Western or Orthodox computus by region.</summary>
    [JsonStringEnumMemberName("easter")]
    Easter,

    /// <summary>From the fixed <see cref="CalendarRule.From"/> to the fourth Thursday of November.</summary>
    [JsonStringEnumMemberName("usThanksgiving")]
    UsThanksgiving,

    /// <summary>Offsets in days from Canadian Thanksgiving (second Monday of October).</summary>
    [JsonStringEnumMemberName("caThanksgiving")]
    CaThanksgiving,

    /// <summary>From the day after US Thanksgiving to the fixed <see cref="CalendarRule.To"/>.</summary>
    [JsonStringEnumMemberName("afterUsThanksgiving")]
    AfterUsThanksgiving,

    /// <summary>The day before 25 Kislev through 25 Kislev + 7 days (.NET <c>HebrewCalendar</c>).</summary>
    [JsonStringEnumMemberName("chanukah")]
    Chanukah,
}

/// <summary>The date rule of a calendar entry.</summary>
public sealed record CalendarRule
{
    /// <summary>The rule type.</summary>
    public CalendarRuleType Type { get; set; } = CalendarRuleType.Fixed;

    /// <summary>Start as "MM-DD" (fixed rules and the start of <see cref="CalendarRuleType.UsThanksgiving"/>).</summary>
    public string? From { get; set; }

    /// <summary>End as "MM-DD" (fixed rules and the end of <see cref="CalendarRuleType.AfterUsThanksgiving"/>).</summary>
    public string? To { get; set; }

    /// <summary>Start offset in days for offset rules (Easter: -14; Canadian Thanksgiving: -6).</summary>
    public int FromOffset { get; set; }

    /// <summary>End offset in days for offset rules (Easter: +1; Canadian Thanksgiving: 0).</summary>
    public int ToOffset { get; set; }
}

/// <summary>One row of the Theme Calendar (PRODUCT-SPEC 3.6.4, 5.11).</summary>
public sealed record CalendarEntry
{
    /// <summary>Stable id: newYear, valentinesDay, stPatricksDay, easter, july4th, halloween, thanksgiving, christmas, chanukah, winter, spring, summer, autumn.</summary>
    public string Id { get; set; } = "";

    /// <summary>The "Use" check box.</summary>
    public bool Use { get; set; }

    /// <summary>The dates.</summary>
    public CalendarRule Rule { get; set; } = new();

    /// <summary>The theme name applied while the entry is active.</summary>
    public string Theme { get; set; } = "";
}

/// <summary>Settings key <c>calendar</c>: Automatic themes ("Change Themes Automatically on Holidays").</summary>
public sealed record CalendarSettings
{
    /// <summary>
    /// The default rows for the United States (the default region); other regions are derived by the season calendar
    /// (<see cref="ISeasonCalendar.CreateDefaultEntries"/>). A new list on every call.
    /// </summary>
    public static IReadOnlyList<CalendarEntry> UnitedStatesDefaults =>
    [
        Fixed("newYear", true, "12-31", "01-01", ShippedThemeNames.NewYear),
        Fixed("valentinesDay", true, "02-01", "02-14", ShippedThemeNames.ValentinesDay),
        Fixed("stPatricksDay", true, "03-10", "03-17", ShippedThemeNames.StPatricksDay),
        new() { Id = "easter", Use = true, Rule = new() { Type = CalendarRuleType.Easter, FromOffset = -14, ToOffset = 1 }, Theme = ShippedThemeNames.EasterEggs },
        Fixed("july4th", true, "07-01", "07-05", ShippedThemeNames.July4th),
        Fixed("halloween", true, "10-01", "10-31", ShippedThemeNames.Halloween),
        new() { Id = "thanksgiving", Use = true, Rule = new() { Type = CalendarRuleType.UsThanksgiving, From = "11-01" }, Theme = ShippedThemeNames.Thanksgiving },
        new() { Id = "christmas", Use = true, Rule = new() { Type = CalendarRuleType.AfterUsThanksgiving, To = "12-30" }, Theme = ShippedThemeNames.Christmas1 },
        new() { Id = "chanukah", Use = false, Rule = new() { Type = CalendarRuleType.Chanukah }, Theme = ShippedThemeNames.Chanukah },
        Fixed("winter", false, "01-02", "02-28", ShippedThemeNames.WinterWonderland),
        Fixed("spring", false, "03-01", "05-31", ShippedThemeNames.SpringGarden),
        Fixed("summer", false, "06-01", "08-31", ShippedThemeNames.SummerNights),
        Fixed("autumn", false, "09-01", "11-24", ShippedThemeNames.AutumnHarvest),
    ];

    /// <summary>The switch (on for newcomers and factory-default 5.4 imports; off for customized imports).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>"Holidays for": a two-letter region (default: the Windows region).</summary>
    public string Region { get; set; } = "US";

    /// <summary>"Between holidays, show": the theme when no checked entry contains today.</summary>
    public string Between { get; set; } = ShippedThemeNames.ClassicLights;

    /// <summary>"Tell Me When the Theme Changes".</summary>
    public bool Notify { get; set; } = true;

    /// <summary>The rows, in display order.</summary>
    public IReadOnlyList<CalendarEntry> Entries { get; set; } = UnitedStatesDefaults;

    /// <summary>
    /// The entry whose theme was applied last (<c>"between"</c> for the between-holidays theme), so a switch happens only
    /// when the active entry changes ("boundaries only"); null before the first evaluation.
    /// </summary>
    public string? ActiveEntryId { get; set; }

    private static CalendarEntry Fixed(string id, bool use, string from, string to, string theme) =>
        new() { Id = id, Use = use, Rule = new() { Type = CalendarRuleType.Fixed, From = from, To = to }, Theme = theme };
}
