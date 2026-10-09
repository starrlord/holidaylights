using System.Globalization;

namespace HolidayLights.Core.Seasons;

/// <summary>A day of the year without a year, stored as <c>"MM-DD"</c> in calendar rules.</summary>
/// <param name="Month">The month 1-12.</param>
/// <param name="Day">The day 1-31 (February 29 is allowed).</param>
internal readonly record struct MonthDay(int Month, int Day)
{
    /// <summary>Parses <c>"MM-DD"</c>.</summary>
    /// <param name="text">The stored text.</param>
    /// <param name="value">The day when the text is valid.</param>
    /// <returns>True for a valid day of a leap year.</returns>
    public static bool TryParse(string? text, out MonthDay value)
    {
        value = default;
        if (text is not { Length: 5 } || text[2] != '-'
            || !int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int month)
            || !int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int day)
            || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2000, month))
        {
            return false;
        }

        value = new MonthDay(month, day);
        return true;
    }

    /// <summary>The date in a year as the start of a range: February 29 falls back to February 28 in common years.</summary>
    /// <param name="year">The year.</param>
    /// <returns>The date.</returns>
    public DateOnly StartIn(int year) => new(year, Month, Math.Min(Day, DateTime.DaysInMonth(year, Month)));

    /// <summary>
    /// The date in a year as the end of a range. A range that ends on the last day of February ends on February 29 in leap
    /// years, so the default rows that meet at the end of February ("Winter" ends Feb 28, "Spring" starts Mar 1) leave no
    /// gap; February 29 falls back to February 28 in common years.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <returns>The date.</returns>
    public DateOnly EndIn(int year) =>
        Month == 2 && Day >= 28 ? new DateOnly(year, 2, DateTime.DaysInMonth(year, 2)) : StartIn(year);
}
