using HolidayLights.Core.Seasons;

namespace HolidayLights.Tests.Settings;

public sealed class SeasonCalendarTests
{
    private readonly SeasonCalendar calendar = new();

    [Fact]
    public void WesternEaster_MatchesThePublishedTable2000To2050()
    {
        foreach ((int year, DateOnly easter) in HolidayDateTables.Rows(HolidayDateTables.WesternEaster))
        {
            Assert.Equal(easter, HolidayRules.WesternEaster(year));
        }
    }

    [Fact]
    public void OrthodoxEaster_MatchesThePublishedTable2000To2050()
    {
        foreach ((int year, DateOnly easter) in HolidayDateTables.Rows(HolidayDateTables.OrthodoxEaster))
        {
            Assert.Equal(easter, HolidayRules.OrthodoxEaster(year));
        }
    }

    [Fact]
    public void UsThanksgiving_IsTheFourthThursdayOfNovember2000To2050()
    {
        foreach ((int year, DateOnly thanksgiving) in HolidayDateTables.Rows(HolidayDateTables.UsThanksgiving))
        {
            Assert.Equal(thanksgiving, HolidayRules.UsThanksgiving(year));
            Assert.Equal(DayOfWeek.Thursday, thanksgiving.DayOfWeek);
        }
    }

    [Fact]
    public void CanadianThanksgiving_IsTheSecondMondayOfOctober2000To2050()
    {
        foreach ((int year, DateOnly thanksgiving) in HolidayDateTables.Rows(HolidayDateTables.CanadianThanksgiving))
        {
            Assert.Equal(thanksgiving, HolidayRules.CanadianThanksgiving(year));
        }
    }

    [Fact]
    public void Chanukah_StartsTheDayBefore25KislevAndLastsEightNights2000To2050()
    {
        CalendarEntry chanukah = Entry(Us(), CalendarEntryIds.Chanukah);
        foreach ((int year, DateOnly kislev25) in HolidayDateTables.Rows(HolidayDateTables.Kislev25))
        {
            Assert.Equal(kislev25, HolidayRules.Kislev25(year));
            HolidayOccurrence occurrence = calendar.GetOccurrence(chanukah, "US", new DateOnly(year, 11, 1));
            Assert.Equal(kislev25.AddDays(-1), occurrence.Start);
            Assert.Equal(kislev25.AddDays(7), occurrence.End);
        }
    }

    [Fact]
    public void EasterEntry_RunsFrom14DaysBeforeEasterToEasterMonday2000To2050()
    {
        CalendarEntry easter = Entry(Us(), CalendarEntryIds.Easter);
        foreach ((int year, DateOnly sunday) in HolidayDateTables.Rows(HolidayDateTables.WesternEaster))
        {
            HolidayOccurrence occurrence = calendar.GetOccurrence(easter, "US", new DateOnly(year, 1, 2));
            Assert.Equal((sunday.AddDays(-14), sunday.AddDays(1)), (occurrence.Start, occurrence.End));
        }
    }

    [Fact]
    public void OrthodoxRegions_UseTheJulianComputus()
    {
        CalendarEntry easter = Entry(Us(), CalendarEntryIds.Easter);

        HolidayOccurrence greece = calendar.GetOccurrence(easter, "gr", new DateOnly(2026, 1, 1));
        HolidayOccurrence germany = calendar.GetOccurrence(easter, "DE", new DateOnly(2026, 1, 1));

        Assert.Equal(new DateOnly(2026, 4, 13), greece.End);
        Assert.Equal(new DateOnly(2026, 4, 6), germany.End);
    }

    [Theory]
    [InlineData(2026, 10, 31, "halloween", "Halloween")]
    [InlineData(2026, 11, 1, "thanksgiving", "Thanksgiving")]
    [InlineData(2026, 11, 27, "christmas", "Christmas 1")]
    [InlineData(2026, 12, 31, "newYear", "New Year")]
    [InlineData(2027, 1, 1, "newYear", "New Year")]
    [InlineData(2026, 7, 15, null, "Classic Lights")]
    [InlineData(2026, 10, 8, "halloween", "Halloween")]
    [InlineData(2027, 2, 14, "valentinesDay", "Valentine's Day")]
    [InlineData(2027, 3, 20, "easter", "Easter Eggs")]
    [InlineData(2027, 3, 14, "stPatricksDay", "St. Patrick's Day")]
    [InlineData(2027, 3, 29, "easter", "Easter Eggs")]
    [InlineData(2026, 7, 4, "july4th", "July 4th")]
    public void Resolve_FollowsTheAcceptanceDatesOfTheUnitedStates(int year, int month, int day, string? entry, string theme)
    {
        CalendarResolution resolution = calendar.Resolve(Us(), new DateOnly(year, month, day));

        Assert.Equal(entry, resolution.EntryId);
        Assert.Equal(theme, resolution.ThemeName);
    }

    [Fact]
    public void Resolve_GivesTheRangeOfTheActiveEntry()
    {
        CalendarResolution resolution = calendar.Resolve(Us(), new DateOnly(2026, 10, 8));

        Assert.Equal(new DateOnly(2026, 10, 1), resolution.Start);
        Assert.Equal(new DateOnly(2026, 10, 31), resolution.End);
        Assert.Null(calendar.Resolve(Us(), new DateOnly(2026, 7, 15)).End);
    }

    [Fact]
    public void Resolve_ShorterRangeWins_ChanukahInsideChristmasThanksgivingInsideAutumnValentinesInsideWinter()
    {
        CalendarSettings all = Us() with { Entries = [.. Us().Entries.Select(e => e with { Use = true })] };

        Assert.Equal("chanukah", calendar.Resolve(all, new DateOnly(2026, 12, 5)).EntryId);
        Assert.Equal("christmas", calendar.Resolve(all, new DateOnly(2026, 12, 13)).EntryId);
        Assert.Equal("thanksgiving", calendar.Resolve(all, new DateOnly(2026, 11, 2)).EntryId);
        Assert.Equal("halloween", calendar.Resolve(all, new DateOnly(2026, 10, 2)).EntryId);
        Assert.Equal("autumn", calendar.Resolve(all, new DateOnly(2026, 9, 2)).EntryId);
        Assert.Equal("valentinesDay", calendar.Resolve(all, new DateOnly(2027, 2, 10)).EntryId);
        Assert.Equal("winter", calendar.Resolve(all, new DateOnly(2027, 2, 20)).EntryId);
    }

    [Fact]
    public void Resolve_EqualLengthLaterStartWins()
    {
        CalendarSettings settings = Us() with
        {
            Entries =
            [
                Fixed("first", "12-01", "12-10", "One"),
                Fixed("second", "12-05", "12-14", "Two"),
            ],
        };

        Assert.Equal("Two", calendar.Resolve(settings, new DateOnly(2026, 12, 6)).ThemeName);
        Assert.Equal("One", calendar.Resolve(settings, new DateOnly(2026, 12, 4)).ThemeName);
    }

    [Fact]
    public void Resolve_UncheckedEntriesAreIgnored_BetweenThemeIsShown()
    {
        CalendarSettings settings = Us() with { Between = "Bubble Lights" };
        settings = settings with { Entries = [.. settings.Entries.Select(e => e.Id == "halloween" ? e with { Use = false } : e)] };

        CalendarResolution resolution = calendar.Resolve(settings, new DateOnly(2026, 10, 8));

        Assert.Null(resolution.EntryId);
        Assert.Equal("Bubble Lights", resolution.ThemeName);
    }

    [Fact]
    public void Resolve_ARangeEndingFebruary28CoversFebruary29InLeapYears()
    {
        CalendarSettings seasons = Us() with { Entries = [.. Us().Entries.Select(e => e.Id is "winter" or "spring" ? e with { Use = true } : e)] };

        Assert.Equal("winter", calendar.Resolve(seasons, new DateOnly(2028, 2, 29)).EntryId);
        Assert.Equal("spring", calendar.Resolve(seasons, new DateOnly(2028, 3, 1)).EntryId);
        Assert.Equal("winter", calendar.Resolve(seasons, new DateOnly(2027, 2, 28)).EntryId);
    }

    [Fact]
    public void GetNextChange_FindsTheNextBoundary()
    {
        (DateOnly date, CalendarResolution resolution) = calendar.GetNextChange(Us(), new DateOnly(2026, 10, 8))!.Value;

        Assert.Equal(new DateOnly(2026, 11, 1), date);
        Assert.Equal("Thanksgiving", resolution.ThemeName);

        (DateOnly afterChristmas, CalendarResolution between) = calendar.GetNextChange(Us(), new DateOnly(2026, 12, 1))!.Value;
        Assert.Equal(new DateOnly(2026, 12, 31), afterChristmas);
        Assert.Equal("newYear", between.EntryId);

        (DateOnly afterNewYear, CalendarResolution gap) = calendar.GetNextChange(Us(), new DateOnly(2027, 1, 1))!.Value;
        Assert.Equal(new DateOnly(2027, 1, 2), afterNewYear);
        Assert.Null(gap.EntryId);
    }

    [Fact]
    public void GetNextChange_NothingWithinAYear_IsNull()
    {
        CalendarSettings none = Us() with { Entries = [.. Us().Entries.Select(e => e with { Use = false })] };

        Assert.Null(calendar.GetNextChange(none, new DateOnly(2026, 10, 8)));
    }

    [Fact]
    public void GetOccurrence_GivesTheThisYearColumnOfTheThemeCalendar()
    {
        var date = new DateOnly(2026, 10, 8);
        CalendarSettings us = Us();

        Assert.Equal((new DateOnly(2027, 3, 14), new DateOnly(2027, 3, 29)), Range(calendar.GetOccurrence(Entry(us, "easter"), "US", date)));
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 26)), Range(calendar.GetOccurrence(Entry(us, "thanksgiving"), "US", date)));
        Assert.Equal((new DateOnly(2026, 11, 27), new DateOnly(2026, 12, 30)), Range(calendar.GetOccurrence(Entry(us, "christmas"), "US", date)));
        Assert.Equal((new DateOnly(2026, 12, 4), new DateOnly(2026, 12, 12)), Range(calendar.GetOccurrence(Entry(us, "chanukah"), "US", date)));
        Assert.Equal((new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1)), Range(calendar.GetOccurrence(Entry(us, "newYear"), "US", date)));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), Range(calendar.GetOccurrence(Entry(us, "halloween"), "US", date)));
    }

    [Fact]
    public void GetOccurrence_MalformedRule_Throws()
    {
        var entry = new CalendarEntry { Id = "x", Use = true, Theme = "T", Rule = new CalendarRule { Type = CalendarRuleType.Fixed, From = "13-01", To = "12-01" } };

        Assert.Throws<ArgumentException>(() => calendar.GetOccurrence(entry, "US", new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void CreateDefaultEntries_UnitedStates()
    {
        IReadOnlyList<CalendarEntry> entries = calendar.CreateDefaultEntries("US");

        Assert.Equal(CalendarEntryIds.All, entries.Select(e => e.Id));
        Assert.Equal(
            ["newYear", "valentinesDay", "stPatricksDay", "easter", "july4th", "halloween", "thanksgiving", "christmas"],
            entries.Where(e => e.Use).Select(e => e.Id));
        Assert.Equal(CalendarRuleType.AfterUsThanksgiving, Entry(entries, "christmas").Rule.Type);
        Assert.Equal("Winter Wonderland", Entry(entries, "winter").Theme);
    }

    [Fact]
    public void CreateDefaultEntries_Canada_UsesCanadianThanksgivingAndChristmasFromNovember25()
    {
        var canada = new CalendarSettings { Region = "CA", Entries = calendar.CreateDefaultEntries("ca") };

        Assert.False(Entry(canada.Entries, "july4th").Use);
        Assert.Equal((new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 12)), Range(calendar.GetOccurrence(Entry(canada.Entries, "thanksgiving"), "CA", new DateOnly(2026, 10, 1))));
        Assert.Equal("thanksgiving", calendar.Resolve(canada, new DateOnly(2026, 10, 12)).EntryId);
        Assert.Equal("halloween", calendar.Resolve(canada, new DateOnly(2026, 10, 13)).EntryId);
        Assert.Equal("christmas", calendar.Resolve(canada, new DateOnly(2026, 11, 25)).EntryId);
        Assert.Null(calendar.Resolve(canada, new DateOnly(2026, 11, 24)).EntryId);
    }

    [Fact]
    public void CreateDefaultEntries_OtherRegions_LeaveUsHolidaysUnchecked()
    {
        var britain = new CalendarSettings { Region = "GB", Entries = calendar.CreateDefaultEntries("GB") };

        Assert.False(Entry(britain.Entries, "july4th").Use);
        Assert.False(Entry(britain.Entries, "thanksgiving").Use);
        Assert.Null(calendar.Resolve(britain, new DateOnly(2026, 7, 4)).EntryId);
        Assert.Equal("christmas", calendar.Resolve(britain, new DateOnly(2026, 11, 25)).EntryId);
    }

    [Fact]
    public void CreateDefaultEntries_SouthernHemisphere_SwapsTheSeasons()
    {
        IReadOnlyList<CalendarEntry> entries = calendar.CreateDefaultEntries("AU");
        var australia = new CalendarSettings { Region = "AU", Entries = [.. entries.Select(e => e.Id is "winter" or "summer" or "spring" or "autumn" ? e with { Use = true } : e)] };

        Assert.Equal("Winter Wonderland", calendar.Resolve(australia, new DateOnly(2026, 7, 15)).ThemeName);
        Assert.Equal("Summer Nights", calendar.Resolve(australia, new DateOnly(2027, 1, 15)).ThemeName);
        Assert.Equal("Spring Garden", calendar.Resolve(australia, new DateOnly(2026, 9, 15)).ThemeName);
        Assert.Equal("Autumn Harvest", calendar.Resolve(australia, new DateOnly(2026, 4, 15)).ThemeName);
    }

    [Fact]
    public void DisplayNamesAndBulbCategories()
    {
        Assert.Equal("Valentine's Day", calendar.GetDisplayName("valentinesDay"));
        Assert.Equal("St. Patrick's Day", calendar.GetDisplayName("stPatricksDay"));
        Assert.Equal("custom", calendar.GetDisplayName("custom"));
        Assert.Equal(["Halloween", "Autumn"], calendar.GetBulbCategories("halloween"));
        Assert.Equal(["Christmas", "Winter"], calendar.GetBulbCategories("christmas"));
        Assert.Equal(["Easter", "Spring"], calendar.GetBulbCategories("easter"));
        Assert.Equal(["July 4th", "Flags"], calendar.GetBulbCategories("july4th"));
        Assert.Equal(["Hanukkah"], calendar.GetBulbCategories("chanukah"));
        Assert.Empty(calendar.GetBulbCategories("winter"));
    }

    private static CalendarSettings Us() => new() { Region = "US", Entries = new SeasonCalendar().CreateDefaultEntries("US") };

    private static CalendarEntry Entry(CalendarSettings settings, string id) => Entry(settings.Entries, id);

    private static CalendarEntry Entry(IReadOnlyList<CalendarEntry> entries, string id) => entries.Single(e => e.Id == id);

    private static CalendarEntry Fixed(string id, string from, string to, string theme) =>
        new() { Id = id, Use = true, Theme = theme, Rule = new CalendarRule { Type = CalendarRuleType.Fixed, From = from, To = to } };

    private static (DateOnly, DateOnly) Range(HolidayOccurrence occurrence) => (occurrence.Start, occurrence.End);
}
