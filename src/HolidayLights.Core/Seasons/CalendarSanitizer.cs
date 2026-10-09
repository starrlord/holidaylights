namespace HolidayLights.Core.Seasons;

/// <summary>
/// Repairs the Theme Calendar settings: a valid region, a between-holidays theme, and the 13 standard rows in display
/// order (a missing or malformed row gets the region's default; a malformed rule its default rule). Rows with other ids
/// are kept after the standard rows when they are usable.
/// </summary>
internal static class CalendarSanitizer
{
    /// <summary>Repairs the calendar settings.</summary>
    /// <param name="calendar">The stored settings, possibly null when read from a file.</param>
    /// <returns>The same instance when valid, else a repaired copy.</returns>
    public static CalendarSettings Sanitize(CalendarSettings? calendar)
    {
        if (calendar is null)
        {
            return new CalendarSettings();
        }

        string region = CalendarRegions.Normalize(calendar.Region);
        string between = string.IsNullOrWhiteSpace(calendar.Between) ? ShippedThemeNames.ClassicLights : calendar.Between;
        IReadOnlyList<CalendarEntry> entries = SanitizeEntries(calendar.Entries, region);
        string? active = string.IsNullOrWhiteSpace(calendar.ActiveEntryId) ? null : calendar.ActiveEntryId;
        bool unchanged = region == calendar.Region && between == calendar.Between
            && ReferenceEquals(entries, calendar.Entries) && active == calendar.ActiveEntryId;
        return unchanged
            ? calendar
            : calendar with { Region = region, Between = between, Entries = entries, ActiveEntryId = active };
    }

    /// <summary>True when a rule can produce dates.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>True when usable.</returns>
    public static bool IsValidRule(CalendarRule? rule) => rule is not null && rule.Type switch
    {
        CalendarRuleType.Fixed => MonthDay.TryParse(rule.From, out _) && MonthDay.TryParse(rule.To, out _),
        CalendarRuleType.UsThanksgiving => MonthDay.TryParse(rule.From, out _),
        CalendarRuleType.AfterUsThanksgiving => MonthDay.TryParse(rule.To, out _),
        CalendarRuleType.Easter or CalendarRuleType.CaThanksgiving => HolidayRules.HasValidOffsets(rule),
        CalendarRuleType.Chanukah => true,
        _ => false,
    };

    private static IReadOnlyList<CalendarEntry> SanitizeEntries(IReadOnlyList<CalendarEntry>? stored, string region)
    {
        if (stored is null)
        {
            return CalendarDefaults.Entries(region);
        }

        var result = new List<CalendarEntry>();
        foreach (CalendarEntry defaults in CalendarDefaults.Entries(region))
        {
            CalendarEntry? entry = stored.FirstOrDefault(e => e is not null && string.Equals(e.Id, defaults.Id, StringComparison.OrdinalIgnoreCase));
            result.Add(entry is null ? defaults : Repair(entry, defaults));
        }

        var seen = new HashSet<string>(CalendarEntryIds.All, StringComparer.OrdinalIgnoreCase);
        foreach (CalendarEntry entry in stored)
        {
            if (entry is not null && !string.IsNullOrWhiteSpace(entry.Id) && IsValidRule(entry.Rule)
                && !string.IsNullOrWhiteSpace(entry.Theme) && seen.Add(entry.Id))
            {
                result.Add(entry);
            }
        }

        return result.Count == stored.Count && result.Zip(stored).All(pair => ReferenceEquals(pair.First, pair.Second)) ? stored : result;
    }

    private static CalendarEntry Repair(CalendarEntry entry, CalendarEntry defaults)
    {
        CalendarRule rule = IsValidRule(entry.Rule) ? entry.Rule : defaults.Rule;
        string theme = string.IsNullOrWhiteSpace(entry.Theme) ? defaults.Theme : entry.Theme;
        bool unchanged = entry.Id == defaults.Id && ReferenceEquals(rule, entry.Rule) && theme == entry.Theme;
        return unchanged ? entry : entry with { Id = defaults.Id, Rule = rule, Theme = theme };
    }
}
