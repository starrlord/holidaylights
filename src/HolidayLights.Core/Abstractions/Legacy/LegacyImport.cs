namespace HolidayLights.Core.Abstractions;

/// <summary>What the 5.4 import found before anything is changed (PRODUCT-SPEC 6.8).</summary>
public sealed record LegacyImportPreview
{
    /// <summary>True when every 5.4 value equals its factory default, "Disabled Music" and "Included Bulb Categories" are empty and every theme is an unchanged installer theme (2.5.2).</summary>
    public required bool IsFactoryDefault { get; init; }

    /// <summary>The 5.4 folder (HKCU <c>Path</c>, else HKLM, long form), or null when unknown.</summary>
    public string? LegacyFolder { get; init; }

    /// <summary>The 5.4 current settings mapped to 6.0 values (the "Holiday Lights 5.4 Settings" Recent Settings entry, "Use My 2003 Lights").</summary>
    public required ThemeableSettings LegacyValues { get; init; }

    /// <summary>Themes found under <c>Themes\</c>.</summary>
    public int ThemeCount { get; init; }

    /// <summary><c>.bul</c> files in <c>Holiday Lights Bulbs</c>.</summary>
    public int BulbFileCount { get; init; }

    /// <summary>Songs in <c>Holiday Lights Music</c> that are not bundled.</summary>
    public int SongFileCount { get; init; }

    /// <summary>Pictures in <c>Holiday Lights Pictures</c> that are not bundled.</summary>
    public int PictureFileCount { get; init; }
}

/// <summary>When the import runs.</summary>
public enum LegacyImportMode
{
    /// <summary>First start (before the first frame): newcomer look for factory defaults, else the customized path (6.8.4).</summary>
    FirstRun,

    /// <summary>General > "Import Again...": replaces the current settings with the 5.4 ones and adds missing 5.4 themes; existing 6.0 themes are kept.</summary>
    Again,
}

/// <summary>The result of an import: the caller stores <see cref="Settings"/> through the settings store.</summary>
/// <param name="Settings">The new settings (with <see cref="AppSettings.Import54"/> filled in).</param>
/// <param name="Record">The report (also in <see cref="AppSettings.Import54"/>).</param>
public sealed record LegacyImportResult(AppSettings Settings, Import54Record Record);

/// <summary>
/// The read-only Holiday Lights 5.4 import (PRODUCT-SPEC 6.8): registry values, themes, category overrides, custom
/// colours, hot key, files. Implemented by core-settings (mapping in <c>Core\Legacy\</c>, registry reading in
/// <c>Platform\Legacy\</c>). Nothing under the 5.4 key or its folders is ever changed.
/// </summary>
public interface ILegacyImporter
{
    /// <summary>True when <c>HKCU\Software\Tiger Technologies\Holiday Lights</c> exists.</summary>
    /// <returns>True when 5.4 settings are present.</returns>
    bool IsLegacyInstallPresent();

    /// <summary>Reads everything without changing anything.</summary>
    /// <returns>The preview, or null when 5.4 is not present.</returns>
    LegacyImportPreview? Analyze();

    /// <summary>
    /// Imports: copies files that are not bundled (bulbs by content identity, songs, pictures) into the user folders,
    /// saves 5.4 themes (equal installer themes are "Already included"), maps the current values and builds the report.
    /// </summary>
    /// <param name="preview">The preview from <see cref="Analyze"/>.</param>
    /// <param name="current">The current settings.</param>
    /// <param name="mode">First run or Import Again.</param>
    /// <returns>The new settings and the report.</returns>
    LegacyImportResult Import(LegacyImportPreview preview, AppSettings current, LegacyImportMode mode);
}

/// <summary>Holiday Lights 5.4 leftovers on this PC (PRODUCT-SPEC 6.8.3).</summary>
/// <param name="IsRunning"><c>FindWindow("Holiday Lights", "TigerTechHolidayLights")</c> succeeds.</param>
/// <param name="StartupShortcut">A <c>*Holiday Lights*.lnk</c> in the Startup folder that targets the 5.4 exe, or null.</param>
public sealed record LegacyLeftoverState(bool IsRunning, string? StartupShortcut);

/// <summary>
/// Detects and fixes 5.4 leftovers (the broken 5.4 screen saver is part of <see cref="IScreenSaverRegistration"/>).
/// Implemented by core-settings in <c>Platform\Legacy\</c>. Fixes honour <see cref="AppRuntimeOptions.AllowSystemChanges"/>.
/// </summary>
public interface ILegacyLeftovers
{
    /// <summary>Detects the leftovers.</summary>
    /// <returns>The current state.</returns>
    LegacyLeftoverState Detect();

    /// <summary>"Close It": sends the running 5.4 <c>WM_COMMAND 106</c> (its Exit command).</summary>
    /// <returns>True when 5.4 was running and the command was delivered.</returns>
    bool CloseRunningInstance();

    /// <summary>"Turn Off": moves the 5.4 Startup shortcut to the Recycle Bin.</summary>
    /// <returns>True when the shortcut was removed.</returns>
    bool RemoveStartupShortcut();
}
