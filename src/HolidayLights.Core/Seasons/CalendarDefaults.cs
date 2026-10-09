namespace HolidayLights.Core.Seasons;

/// <summary>
/// The default Theme Calendar rows of a region (PRODUCT-SPEC 5.11 table and 3.6.4): July 4th checked only in the United
/// States, Thanksgiving checked in the United States and Canada with their own rules, Christmas from the day after US
/// Thanksgiving in the United States and from Nov 25 elsewhere, southern-hemisphere seasons, Chanukah and the seasons
/// unchecked.
/// </summary>
internal static class CalendarDefaults
{
    /// <summary>Builds the 13 rows for a region (a new list of new entries on every call).</summary>
    /// <param name="region">A region (normalized here).</param>
    /// <returns>The rows in display order.</returns>
    public static IReadOnlyList<CalendarEntry> Entries(string? region)
    {
        string code = CalendarRegions.Normalize(region);
        bool unitedStates = code == CalendarRegions.UnitedStates;
        bool canada = code == CalendarRegions.Canada;
        bool south = CalendarRegions.IsSouthernHemisphere(code);
        var entries = new List<CalendarEntry>();
        foreach (CalendarEntry entry in CalendarSettings.UnitedStatesDefaults)
        {
            entries.Add(entry.Id switch
            {
                CalendarEntryIds.July4th => entry with { Use = unitedStates },
                CalendarEntryIds.Thanksgiving when canada => entry with
                {
                    Use = true,
                    Rule = new CalendarRule { Type = CalendarRuleType.CaThanksgiving, FromOffset = -6, ToOffset = 0 },
                },
                CalendarEntryIds.Thanksgiving => entry with { Use = unitedStates },
                CalendarEntryIds.Christmas when !unitedStates => entry with { Rule = Fixed("11-25", "12-30") },
                CalendarEntryIds.Winter when south => entry with { Rule = Fixed("06-01", "08-31") },
                CalendarEntryIds.Spring when south => entry with { Rule = Fixed("09-01", "11-24") },
                CalendarEntryIds.Summer when south => entry with { Rule = Fixed("01-02", "02-28") },
                CalendarEntryIds.Autumn when south => entry with { Rule = Fixed("03-01", "05-31") },
                _ => entry,
            });
        }

        return entries;
    }

    private static CalendarRule Fixed(string from, string to) => new() { Type = CalendarRuleType.Fixed, From = from, To = to };
}
