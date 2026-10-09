using System.Globalization;

namespace HolidayLights.Core.Seasons;

/// <summary>
/// The dates of the Theme Calendar rules (PRODUCT-SPEC 5.11, Appendix C rule types): Easter by the Gregorian or the
/// Julian computus, US and Canadian Thanksgiving, Chanukah from the Hebrew calendar, and fixed month-day ranges.
/// </summary>
internal static class HolidayRules
{
    /// <summary>The largest Easter or Canadian Thanksgiving offset accepted, in days.</summary>
    public const int MaxOffsetDays = 180;

    private static readonly HebrewCalendar Hebrew = new();

    private static readonly JulianCalendar Julian = new();

    /// <summary>Western Easter Sunday (the anonymous Gregorian algorithm, Meeus/Jones/Butcher).</summary>
    /// <param name="year">The year.</param>
    /// <returns>Easter Sunday.</returns>
    public static DateOnly WesternEaster(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = ((19 * a) + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        int m = (a + (11 * h) + (22 * l)) / 451;
        int month = (h + l - (7 * m) + 114) / 31;
        int day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    /// <summary>Orthodox Easter Sunday: the Julian computus (Meeus), converted to the Gregorian calendar.</summary>
    /// <param name="year">The year.</param>
    /// <returns>Easter Sunday as a Gregorian date.</returns>
    public static DateOnly OrthodoxEaster(int year)
    {
        int a = year % 4;
        int b = year % 7;
        int c = year % 19;
        int d = ((19 * c) + 15) % 30;
        int e = ((2 * a) + (4 * b) - d + 34) % 7;
        int month = (d + e + 114) / 31;
        int day = ((d + e + 114) % 31) + 1;
        return DateOnly.FromDateTime(Julian.ToDateTime(year, month, day, 0, 0, 0, 0));
    }

    /// <summary>US Thanksgiving: the fourth Thursday of November.</summary>
    /// <param name="year">The year.</param>
    /// <returns>Thanksgiving Day.</returns>
    public static DateOnly UsThanksgiving(int year) => NthWeekday(year, 11, DayOfWeek.Thursday, 4);

    /// <summary>Canadian Thanksgiving: the second Monday of October.</summary>
    /// <param name="year">The year.</param>
    /// <returns>Thanksgiving Day.</returns>
    public static DateOnly CanadianThanksgiving(int year) => NthWeekday(year, 10, DayOfWeek.Monday, 2);

    /// <summary>25 Kislev of the Hebrew year that begins in the autumn of <paramref name="year"/> (the first day of Chanukah).</summary>
    /// <param name="year">A Gregorian year inside the range of <see cref="HebrewCalendar"/>.</param>
    /// <returns>The Gregorian date of 25 Kislev (late November or December).</returns>
    public static DateOnly Kislev25(int year) =>
        DateOnly.FromDateTime(Hebrew.ToDateTime(year + 3761, 3, 25, 0, 0, 0, 0)); // month 3 is always Kislev

    /// <summary>The occurrence of a rule that belongs to <paramref name="year"/> (it may end in the next year).</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="region">A normalized region (Easter computus).</param>
    /// <param name="year">The year.</param>
    /// <param name="start">The first day.</param>
    /// <param name="end">The last day (inclusive).</param>
    /// <returns>False when the rule is malformed or the year is outside the supported range.</returns>
    public static bool TryGetOccurrence(CalendarRule rule, string region, int year, out DateOnly start, out DateOnly end)
    {
        start = end = default;
        if (year is < 1600 or > 2200)
        {
            return false;
        }

        switch (rule.Type)
        {
            case CalendarRuleType.Fixed when MonthDay.TryParse(rule.From, out MonthDay from) && MonthDay.TryParse(rule.To, out MonthDay to):
                start = from.StartIn(year);
                end = to.EndIn(year);
                if (end < start)
                {
                    end = to.EndIn(year + 1);
                }

                return true;
            case CalendarRuleType.Easter when HasValidOffsets(rule):
                DateOnly easter = CalendarRegions.UsesOrthodoxEaster(region) ? OrthodoxEaster(year) : WesternEaster(year);
                (start, end) = (easter.AddDays(rule.FromOffset), easter.AddDays(rule.ToOffset));
                return true;
            case CalendarRuleType.CaThanksgiving when HasValidOffsets(rule):
                DateOnly canadian = CanadianThanksgiving(year);
                (start, end) = (canadian.AddDays(rule.FromOffset), canadian.AddDays(rule.ToOffset));
                return true;
            case CalendarRuleType.UsThanksgiving when MonthDay.TryParse(rule.From, out MonthDay first):
                end = UsThanksgiving(year);
                start = first.StartIn(year);
                if (start > end)
                {
                    start = end;
                }

                return true;
            case CalendarRuleType.AfterUsThanksgiving when MonthDay.TryParse(rule.To, out MonthDay last):
                start = UsThanksgiving(year).AddDays(1);
                end = last.EndIn(year);
                if (end < start)
                {
                    end = last.EndIn(year + 1);
                }

                return true;
            case CalendarRuleType.Chanukah:
                DateOnly kislev25 = Kislev25(year);
                (start, end) = (kislev25.AddDays(-1), kislev25.AddDays(7));
                return true;
            default:
                return false;
        }
    }

    /// <summary>True when the offsets of an offset rule are in order and within <see cref="MaxOffsetDays"/>.</summary>
    /// <param name="rule">An Easter or Canadian Thanksgiving rule.</param>
    /// <returns>True when usable.</returns>
    public static bool HasValidOffsets(CalendarRule rule) =>
        rule.FromOffset <= rule.ToOffset && Math.Abs(rule.FromOffset) <= MaxOffsetDays && Math.Abs(rule.ToOffset) <= MaxOffsetDays;

    private static DateOnly NthWeekday(int year, int month, DayOfWeek weekday, int n)
    {
        var first = new DateOnly(year, month, 1);
        int offset = ((int)weekday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + (7 * (n - 1)));
    }
}
