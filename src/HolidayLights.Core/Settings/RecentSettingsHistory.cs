namespace HolidayLights.Core.Settings;

/// <summary>"Recent Settings" (PRODUCT-SPEC 3.6.6): the values replaced by the last five theme changes, newest first.</summary>
internal static class RecentSettingsHistory
{
    /// <summary>Adds an entry in front and keeps the newest five.</summary>
    /// <param name="recent">The current list.</param>
    /// <param name="label">The label, e.g. "Before Halloween (automatic)".</param>
    /// <param name="date">When the values were replaced.</param>
    /// <param name="values">The replaced 13 values.</param>
    /// <returns>The new list.</returns>
    public static IReadOnlyList<RecentSettingsEntry> Record(IReadOnlyList<RecentSettingsEntry> recent, string label, DateTimeOffset date, ThemeableSettings values) =>
        [new RecentSettingsEntry { Label = label, Date = date, Values = values }, .. recent.Take(SettingsSanitizer.MaxRecentSettings - 1)];
}
