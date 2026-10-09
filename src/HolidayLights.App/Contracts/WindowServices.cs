using System.Windows;

namespace HolidayLights.App.Contracts;

/// <summary>Something to do when Settings opens (beyond showing a page).</summary>
public abstract record SettingsRequest;

/// <summary>Add <c>.bul</c>/<c>.gif</c> files (PRODUCT-SPEC 3.2.10) and select the new bulb on Bulb Factory: double-click in Explorer, <c>--open</c>, a dropped file.</summary>
/// <param name="Paths">The files.</param>
public sealed record OpenFilesRequest(IReadOnlyList<string> Paths) : SettingsRequest;

/// <summary>Automatic themes switched the theme: show the Themes InfoBar "Holiday Lights switched to &lt;theme&gt; on &lt;date&gt;." [Undo] [Turn Off Automatic Themes].</summary>
/// <param name="ThemeName">The theme that was applied.</param>
/// <param name="Date">When.</param>
public sealed record ThemeSwitchedRequest(string ThemeName, DateOnly Date) : SettingsRequest;

/// <summary><c>--reset</c> / legacy <c>reset</c>: open General and ask "Reset Holiday Lights?".</summary>
public sealed record ResetRequest : SettingsRequest;

/// <summary>"Show Import Details" from the Welcome card: open General and show the Import Details dialog.</summary>
public sealed record ImportDetailsRequest : SettingsRequest;

/// <summary>
/// The single Settings window (PRODUCT-SPEC 2.3; implemented by settings-ui). UI thread.
/// </summary>
/// <remarks>
/// Opening takes the Cancel snapshot and starts an empty Undo history; closing (OK, X, Alt+F4, tray Exit) keeps
/// everything and commits the holding folder to the Recycle Bin; Cancel restores the snapshot first. The first close ever
/// shows the "Your lights stay on" notification.
/// </remarks>
public interface ISettingsWindowService
{
    /// <summary>True while the window is open.</summary>
    bool IsOpen { get; }

    /// <summary>The open window (owner of dialogs), or null.</summary>
    Window? Window { get; }

    /// <summary>The page shown while the window is open, else null (for example "Music is waiting" shows only while Music Box is not open, PRODUCT-SPEC 3.12).</summary>
    SettingsPageId? CurrentPage { get; }

    /// <summary>The Undo history of the open window (a no-op history while closed).</summary>
    IUndoHistory Undo { get; }

    /// <summary>Raised after the window opened.</summary>
    event EventHandler? Opened;

    /// <summary>Raised after the window closed (a settings-only session exits then).</summary>
    event EventHandler? Closed;

    /// <summary>Opens the window or brings it forward, on a page.</summary>
    /// <param name="page">The page, or null for the last page used (first time: Home).</param>
    /// <param name="request">Something to do once the page shows, or null.</param>
    void Show(SettingsPageId? page = null, SettingsRequest? request = null);

    /// <summary>Closes the window keeping every change (tray "Exit Holiday Lights").</summary>
    void CloseKeepingChanges();
}

/// <summary>Result of Bulb Editing.</summary>
/// <param name="Saved">True when the user saved (the file was replaced; the previous file is in the holding folder and an Undo step was recorded).</param>
/// <param name="BulbId">The bulb.</param>
/// <param name="CharactersReplaced">True when characters outside Windows-1252 were replaced by "?" (show the InfoBar).</param>
public sealed record BulbEditingResult(bool Saved, string BulbId, bool CharactersReplaced);

/// <summary>Result of turning a GIF into a new bulb.</summary>
/// <param name="SourcePath">The GIF.</param>
/// <param name="BulbId">The new <c>user:</c> bulb, or null on failure.</param>
/// <param name="FilePath">The new <c>.bul</c> file, or null on failure.</param>
/// <param name="ErrorTitle">On failure: "Cannot Import GIF File".</param>
/// <param name="ErrorText">On failure: "Sorry, this GIF file can't be imported. It may be damaged in some way."</param>
public sealed record GifBulbCreation(string SourcePath, string? BulbId, string? FilePath, string? ErrorTitle, string? ErrorText)
{
    /// <summary>True when the bulb was created.</summary>
    public bool Succeeded => BulbId is not null;
}

/// <summary>
/// Bulb Editing, Edit Categories, New Category, Bulb Credits, Export Bulb File and GIF-to-bulb creation (PRODUCT-SPEC 3.3,
/// 3.2.10; implemented by bulb-factory). Dialogs are modal over their owner. UI thread, except
/// <see cref="CreateBulbFromGif"/>.
/// </summary>
public interface IBulbFactoryDialogs
{
    /// <summary>True while Bulb Editing is open with unsaved changes (tray Exit asks first).</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>"Edit Bulb...": Bulb Editing for a My Bulbs file with <c>locked</c> = 0 (<see cref="IBulb.IsEditable"/>).</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="bulbId">The bulb.</param>
    /// <returns>Whether it was saved.</returns>
    Task<BulbEditingResult> EditBulbAsync(Window owner, string bulbId);

    /// <summary>"Edit Categories...": for built-in, bundled and other people's bulbs; stores overrides in settings (one Undo step).</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="bulbId">The bulb.</param>
    /// <returns>True when saved.</returns>
    Task<bool> EditCategoriesAsync(Window owner, string bulbId);

    /// <summary>"Bulb Credits".</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="bulbId">The bulb.</param>
    void ShowCredits(Window owner, string bulbId);

    /// <summary>"Export Bulb File...": a Save As dialog, then a copy of the <c>.bul</c>.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="bulbId">An add-on bulb.</param>
    /// <returns>The exported path, or null when cancelled.</returns>
    Task<string?> ExportBulbFileAsync(Window owner, string bulbId);

    /// <summary>Creates a new bulb file in My Bulbs from a GIF with the 5.4 defaults (does not open the editor and records no Undo step; the caller does both).</summary>
    /// <param name="gifPath">The GIF.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// It writes a file and waits for the catalog's add-on index (<see cref="IBulbCatalog.ImportFile"/>); it does not touch
    /// the UI, is thread-safe, and should be called on the thread pool.
    /// </remarks>
    GifBulbCreation CreateBulbFromGif(string gifPath);

    /// <summary>Asks "Discard Changes?" when Bulb Editing has unsaved changes and closes the editor on Discard.</summary>
    /// <returns>True when the editor is closed (or was not open); false when the user chose to keep editing.</returns>
    Task<bool> ConfirmDiscardAsync();
}

/// <summary>The screen saver for the Settings window (implemented by screensaver). UI thread.</summary>
public interface IScreenSaverService
{
    /// <summary>True while "Preview Screen Saver" runs.</summary>
    bool IsPreviewRunning { get; }

    /// <summary>
    /// Creates the live miniature of the screen saver on the main display (PRODUCT-SPEC 3.5.3): the real simulation at the
    /// display's DIP size drawn scaled, following settings changes at once, running only while visible.
    /// </summary>
    /// <returns>A new element for the Screen Saver page.</returns>
    FrameworkElement CreatePreview();

    /// <summary>"Preview Screen Saver": runs the full-screen saver in-process on the displays of "Show On" without changing any Windows setting, until any key, button or pointer movement over 4 DIP.</summary>
    /// <returns>
    /// A task that completes when the preview ended, or fails with the error when the saver could not start (the session is
    /// then already reported as stopped).
    /// </returns>
    Task RunPreviewAsync();
}
