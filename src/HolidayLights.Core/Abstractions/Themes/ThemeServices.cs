namespace HolidayLights.Core.Abstractions;

/// <summary>A theme file that could not be read (listed in an InfoBar on Themes; never deleted).</summary>
/// <param name="FilePath">The file.</param>
/// <param name="Reason">Why it could not be read.</param>
public sealed record ThemeFileProblem(string FilePath, string Reason);

/// <summary>
/// The theme files in <c>%APPDATA%\Holiday Lights\Themes</c> plus the embedded originals of the 19 shipped themes
/// (PRODUCT-SPEC 6.4). Implemented by core-settings. UI thread for mutations; <see cref="Changed"/> is raised on the
/// calling thread.
/// </summary>
public interface IThemeLibrary
{
    /// <summary>Every readable theme, A-Z by name (current culture, case-insensitive).</summary>
    IReadOnlyList<ThemeDefinition> Themes { get; }

    /// <summary>Theme files that could not be read.</summary>
    IReadOnlyList<ThemeFileProblem> Problems { get; }

    /// <summary>The embedded originals of the 11 classic and 8 new themes (for Restore and the "Changed" badge).</summary>
    IReadOnlyList<ThemeDefinition> ShippedOriginals { get; }

    /// <summary>Raised after themes were saved, deleted, renamed, restored or reloaded.</summary>
    event EventHandler? Changed;

    /// <summary>Reads the folder (and on first run, before the folder exists, seeds the 19 shipped themes into it).</summary>
    void Load();

    /// <summary>Finds a theme by name (case-insensitive).</summary>
    /// <param name="name">The name.</param>
    /// <returns>The theme, or null.</returns>
    ThemeDefinition? Find(string name);

    /// <summary>Creates or replaces a theme (a same-named theme, compared case-insensitively, is replaced).</summary>
    /// <param name="theme">The theme; its name must be valid (<see cref="IThemeService.ValidateName"/>).</param>
    void Save(ThemeDefinition theme);

    /// <summary>Deletes a theme: its file moves to the holding folder.</summary>
    /// <param name="name">The theme name.</param>
    /// <returns>The held file, for undo.</returns>
    HeldItem Delete(string name);

    /// <summary>Brings back a deleted theme from the holding folder.</summary>
    /// <param name="item">The held file returned by <see cref="Delete"/>.</param>
    void Restore(HeldItem item);

    /// <summary>Renames a theme (same rules as Save Theme).</summary>
    /// <param name="oldName">The current name.</param>
    /// <param name="newName">The new name.</param>
    void Rename(string oldName, string newName);

    /// <summary>True when a shipped theme's values differ from its embedded original (the "Changed" badge).</summary>
    /// <param name="theme">A theme.</param>
    /// <returns>True for a changed shipped theme; false for unchanged or user themes.</returns>
    bool IsChangedFromOriginal(ThemeDefinition theme);

    /// <summary>The shipped themes that are missing or changed (the "Restore Built-In Themes" list).</summary>
    /// <returns>Their original definitions.</returns>
    IReadOnlyList<ThemeDefinition> GetMissingOrChangedShipped();

    /// <summary>Writes the originals of the named shipped themes, replacing same-named themes.</summary>
    /// <param name="names">Shipped theme names.</param>
    void RestoreShipped(IEnumerable<string> names);
}

/// <summary>Result of validating a theme name (Save Theme, Rename).</summary>
public enum ThemeNameCheck
{
    /// <summary>Valid.</summary>
    Valid,

    /// <summary>Empty after trimming.</summary>
    Empty,

    /// <summary>Longer than 63 characters after trimming.</summary>
    TooLong,

    /// <summary>Contains one of \ / : * ? " &lt; &gt; |.</summary>
    InvalidCharacters,
}

/// <summary>Options of <see cref="IThemeService.Apply"/>.</summary>
/// <param name="RecentLabel">The Recent Settings label of the replaced values, e.g. "Before Halloween" or "Before Halloween (automatic)".</param>
/// <param name="Now">The time recorded with the Recent Settings entry.</param>
/// <param name="TurnOffAutomaticThemes">True when loading by hand (Home, Themes, tray, Welcome): Automatic themes turn off.</param>
public sealed record ThemeApplyOptions(string RecentLabel, DateTimeOffset Now, bool TurnOffAutomaticThemes);

/// <summary>
/// Loading, capturing and comparing themes (PRODUCT-SPEC 6.4.2). Implemented by core-settings; pure functions over
/// <see cref="AppSettings"/> (the caller applies the result through <see cref="ISettingsStore.Update"/>).
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Loads a theme into settings: copies the 13 values (missing ones get their defaults; songs not named are unchecked),
    /// records the replaced values in Recent Settings (newest first, at most 5), sets the Save Theme pre-fill to the theme's
    /// name and, when asked, turns Automatic themes off.
    /// </summary>
    /// <param name="settings">The current settings.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="options">Recent Settings label and time; Automatic themes rule.</param>
    /// <returns>The new settings.</returns>
    AppSettings Apply(AppSettings settings, ThemeDefinition theme, ThemeApplyOptions options);

    /// <summary>Restores a Recent Settings entry (recording the values it replaces as a new entry).</summary>
    /// <param name="settings">The current settings.</param>
    /// <param name="entry">The entry to restore.</param>
    /// <param name="now">The time recorded with the new entry.</param>
    /// <returns>The new settings.</returns>
    AppSettings RestoreRecent(AppSettings settings, RecentSettingsEntry entry, DateTimeOffset now);

    /// <summary>Captures the 13 current values as a theme (checked songs = every known song not unchecked).</summary>
    /// <param name="name">The theme name.</param>
    /// <param name="settings">The current settings.</param>
    /// <returns>The theme (not saved).</returns>
    ThemeDefinition Capture(string name, AppSettings settings);

    /// <summary>True when all 13 values are equal (songs compared as sets, font family case-insensitively, missing theme values as their defaults).</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="settings">The current settings.</param>
    /// <returns>True for a match (the "Current" badge, the tray radio).</returns>
    bool Matches(ThemeDefinition theme, AppSettings settings);

    /// <summary>The library theme that matches the current settings (A-Z first match), or null ("Custom Settings").</summary>
    /// <param name="settings">The current settings.</param>
    /// <returns>The matching theme, or null.</returns>
    ThemeDefinition? FindMatching(AppSettings settings);

    /// <summary>Names of bulbs a theme uses that are not installed ("This theme uses 2 bulbs that aren't installed: ...").</summary>
    /// <param name="theme">The theme.</param>
    /// <returns>The missing bulbs' ids.</returns>
    IReadOnlyList<string> FindMissingBulbs(ThemeDefinition theme);

    /// <summary>Validates a theme name (1-63 characters after trimming; none of \ / : * ? " &lt; &gt; |).</summary>
    /// <param name="name">The candidate name.</param>
    /// <returns>The verdict.</returns>
    ThemeNameCheck ValidateName(string name);
}

/// <summary>The calendar entry active on a date.</summary>
/// <param name="EntryId">The active entry, or null between holidays.</param>
/// <param name="ThemeName">The theme to show (the entry's, or the between-holidays theme).</param>
/// <param name="Start">First day of the active entry's occurrence (null between holidays).</param>
/// <param name="End">Last day of the active entry's occurrence ("until Oct 31"; null between holidays).</param>
public sealed record CalendarResolution(string? EntryId, string ThemeName, DateOnly? Start, DateOnly? End);

/// <summary>One occurrence of a calendar entry.</summary>
/// <param name="EntryId">The entry.</param>
/// <param name="Start">First day (inclusive).</param>
/// <param name="End">Last day (inclusive).</param>
public sealed record HolidayOccurrence(string EntryId, DateOnly Start, DateOnly End);

/// <summary>
/// The Theme Calendar (PRODUCT-SPEC 5.11): holiday dates by region, overlaps (the shorter range wins; equal length: the
/// later start), the between-holidays theme. Implemented by core-settings (<c>Seasons\</c>). Pure and thread-safe.
/// </summary>
public interface ISeasonCalendar
{
    /// <summary>Returns the entry and theme for a date (checked entries only).</summary>
    /// <param name="calendar">The calendar settings.</param>
    /// <param name="date">A local date.</param>
    /// <returns>The resolution.</returns>
    CalendarResolution Resolve(CalendarSettings calendar, DateOnly date);

    /// <summary>The next occurrence of an entry that has not ended on or after a date ("This Year" column).</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="region">The region (affects Easter computus and US/CA rules).</param>
    /// <param name="date">A local date.</param>
    /// <returns>The occurrence.</returns>
    HolidayOccurrence GetOccurrence(CalendarEntry entry, string region, DateOnly date);

    /// <summary>The next day after <paramref name="date"/> on which the resolution changes ("Next: Thanksgiving on Nov 1").</summary>
    /// <param name="calendar">The calendar settings.</param>
    /// <param name="date">A local date.</param>
    /// <returns>The date and the resolution from then on, or null when nothing changes within a year.</returns>
    (DateOnly Date, CalendarResolution Resolution)? GetNextChange(CalendarSettings calendar, DateOnly date);

    /// <summary>The default rows for a region (check states, US/CA Thanksgiving, July 4th, Orthodox Easter, southern-hemisphere seasons).</summary>
    /// <param name="region">A two-letter region.</param>
    /// <returns>The rows in display order.</returns>
    IReadOnlyList<CalendarEntry> CreateDefaultEntries(string region);

    /// <summary>The display name of an entry ("Valentine's Day").</summary>
    /// <param name="entryId">An entry id.</param>
    /// <returns>The name.</returns>
    string GetDisplayName(string entryId);

    /// <summary>The bulb categories of the "For &lt;Holiday&gt;" filter (Halloween: Halloween, Autumn; Christmas: Christmas, Winter; ...).</summary>
    /// <param name="entryId">An entry id.</param>
    /// <returns>Category names (empty for the seasons).</returns>
    IReadOnlyList<string> GetBulbCategories(string entryId);
}
