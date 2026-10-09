namespace HolidayLights.Core.Seasons;

/// <summary>
/// The Theme Calendar rules of PRODUCT-SPEC 5.11 (Gregorian and Julian computus, US and Canadian Thanksgiving, Chanukah
/// via <c>HebrewCalendar</c>, southern-hemisphere seasons, overlaps). See <see cref="ISeasonCalendar"/>. Owner: core-settings.
/// </summary>
/// <remarks>
/// Ranges are inclusive whole local days and may cross the new year. Among the checked entries that contain a date, the
/// shortest range wins; at equal length the later start wins (Thanksgiving inside Autumn, Chanukah inside Christmas,
/// Valentine's Day inside Winter); at equal start the earlier row wins. A range that ends on February 28 also covers
/// February 29 in leap years, so rows that meet at the end of February leave no gap. Pure and thread-safe.
/// </remarks>
public sealed class SeasonCalendar : ISeasonCalendar
{
    /// <summary>How far <see cref="GetNextChange"/> looks ahead.</summary>
    private const int LookAheadDays = 366;

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [CalendarEntryIds.NewYear] = "New Year",
        [CalendarEntryIds.ValentinesDay] = "Valentine's Day",
        [CalendarEntryIds.StPatricksDay] = "St. Patrick's Day",
        [CalendarEntryIds.Easter] = "Easter",
        [CalendarEntryIds.July4th] = "July 4th",
        [CalendarEntryIds.Halloween] = "Halloween",
        [CalendarEntryIds.Thanksgiving] = "Thanksgiving",
        [CalendarEntryIds.Christmas] = "Christmas",
        [CalendarEntryIds.Chanukah] = "Chanukah",
        [CalendarEntryIds.Winter] = "Winter",
        [CalendarEntryIds.Spring] = "Spring",
        [CalendarEntryIds.Summer] = "Summer",
        [CalendarEntryIds.Autumn] = "Autumn",
    };

    /// <summary>The categories of the "For &lt;Holiday&gt;" Show filter (PRODUCT-SPEC 3.2.5); the seasons have none.</summary>
    private static readonly Dictionary<string, string[]> BulbCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        [CalendarEntryIds.NewYear] = ["New Year"],
        [CalendarEntryIds.ValentinesDay] = ["Valentine's Day"],
        [CalendarEntryIds.StPatricksDay] = ["St. Patrick's Day"],
        [CalendarEntryIds.Easter] = ["Easter", "Spring"],
        [CalendarEntryIds.July4th] = ["July 4th", "Flags"],
        [CalendarEntryIds.Halloween] = ["Halloween", "Autumn"],
        [CalendarEntryIds.Thanksgiving] = ["Thanksgiving", "Autumn"],
        [CalendarEntryIds.Christmas] = ["Christmas", "Winter"],
        [CalendarEntryIds.Chanukah] = ["Hanukkah"],
    };

    /// <inheritdoc />
    public CalendarResolution Resolve(CalendarSettings calendar, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        string region = CalendarRegions.Normalize(calendar.Region);
        (CalendarEntry Entry, HolidayOccurrence Occurrence)? best = null;
        foreach (CalendarEntry entry in UsableEntries(calendar))
        {
            if (FindOccurrence(entry, region, date, containing: true) is not { } occurrence)
            {
                continue;
            }

            if (best is not { } current || Wins(occurrence, current.Occurrence))
            {
                best = (entry, occurrence);
            }
        }

        return best is { } winner
            ? new CalendarResolution(winner.Entry.Id, winner.Entry.Theme, winner.Occurrence.Start, winner.Occurrence.End)
            : new CalendarResolution(null, BetweenTheme(calendar), null, null);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The entry's rule can never produce dates (for example a malformed fixed date).</exception>
    public HolidayOccurrence GetOccurrence(CalendarEntry entry, string region, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return FindOccurrence(entry, CalendarRegions.Normalize(region), date, containing: false)
            ?? throw new ArgumentException($"The rule of calendar entry \"{entry.Id}\" is not valid.", nameof(entry));
    }

    /// <inheritdoc />
    public (DateOnly Date, CalendarResolution Resolution)? GetNextChange(CalendarSettings calendar, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        string region = CalendarRegions.Normalize(calendar.Region);
        string? today = Resolve(calendar, date).EntryId;
        DateOnly last = date.AddDays(LookAheadDays);
        var boundaries = new SortedSet<DateOnly>();
        foreach (CalendarEntry entry in UsableEntries(calendar))
        {
            for (int year = date.Year - 1; year <= last.Year; year++)
            {
                if (HolidayRules.TryGetOccurrence(entry.Rule, region, year, out DateOnly start, out DateOnly end))
                {
                    boundaries.Add(start);
                    boundaries.Add(end.AddDays(1));
                }
            }
        }

        foreach (DateOnly boundary in boundaries.GetViewBetween(date.AddDays(1), last))
        {
            CalendarResolution resolution = Resolve(calendar, boundary);
            if (!string.Equals(resolution.EntryId, today, StringComparison.Ordinal))
            {
                return (boundary, resolution);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<CalendarEntry> CreateDefaultEntries(string region) => CalendarDefaults.Entries(region);

    /// <inheritdoc />
    /// <remarks>An unknown id is returned as is.</remarks>
    public string GetDisplayName(string entryId)
    {
        ArgumentNullException.ThrowIfNull(entryId);
        return DisplayNames.TryGetValue(entryId, out string? name) ? name : entryId;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetBulbCategories(string entryId)
    {
        ArgumentNullException.ThrowIfNull(entryId);
        return BulbCategories.TryGetValue(entryId, out string[]? categories) ? categories : [];
    }

    /// <summary>The checked entries with a usable rule and theme.</summary>
    private static IEnumerable<CalendarEntry> UsableEntries(CalendarSettings calendar) =>
        (calendar.Entries ?? []).Where(e => e is { Use: true } && !string.IsNullOrWhiteSpace(e.Theme) && CalendarSanitizer.IsValidRule(e.Rule));

    private static string BetweenTheme(CalendarSettings calendar) =>
        string.IsNullOrWhiteSpace(calendar.Between) ? ShippedThemeNames.ClassicLights : calendar.Between;

    /// <summary>True when <paramref name="candidate"/> beats <paramref name="current"/>: shorter, or as long and starting later.</summary>
    private static bool Wins(HolidayOccurrence candidate, HolidayOccurrence current)
    {
        int candidateLength = candidate.End.DayNumber - candidate.Start.DayNumber;
        int currentLength = current.End.DayNumber - current.Start.DayNumber;
        return candidateLength < currentLength || (candidateLength == currentLength && candidate.Start > current.Start);
    }

    /// <summary>
    /// The earliest occurrence that has not ended on <paramref name="date"/>; with <paramref name="containing"/>, only an
    /// occurrence that has also started.
    /// </summary>
    private static HolidayOccurrence? FindOccurrence(CalendarEntry entry, string region, DateOnly date, bool containing)
    {
        if (entry.Rule is null)
        {
            return null;
        }

        for (int year = date.Year - 1; year <= date.Year + 1; year++)
        {
            if (HolidayRules.TryGetOccurrence(entry.Rule, region, year, out DateOnly start, out DateOnly end)
                && end >= date && (!containing || start <= date))
            {
                return new HolidayOccurrence(entry.Id, start, end);
            }
        }

        return null;
    }
}
