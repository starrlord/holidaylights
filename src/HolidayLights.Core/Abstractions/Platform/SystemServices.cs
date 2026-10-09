namespace HolidayLights.Core.Abstractions;

/// <summary>A file (or folder) moved to the holding folder so Undo and Cancel can bring it back (PRODUCT-SPEC 2.3, D17).</summary>
/// <param name="OriginalPath">Where the file was.</param>
/// <param name="HeldPath">Where it is now (<c>%LOCALAPPDATA%\Holiday Lights\Removed\&lt;session&gt;\...</c>).</param>
/// <param name="HeldAt">When it was moved.</param>
public sealed record HeldItem(string OriginalPath, string HeldPath, DateTimeOffset HeldAt);

/// <summary>
/// The removal holding folder. Implemented by platform (the Recycle Bin needs <c>IFileOperation</c>). Thread-safe.
/// </summary>
/// <remarks>
/// Removed or undone files move into <see cref="DataPaths.RemovedFolder"/> under a per-session subfolder. When the Settings
/// window closes, <see cref="CommitSession"/> sends them to the Recycle Bin; files left behind by a crash are recycled at
/// the next start (<see cref="RecycleLeftovers"/>).
/// </remarks>
public interface IHoldingFolder
{
    /// <summary>Files currently held in this session.</summary>
    IReadOnlyList<HeldItem> Items { get; }

    /// <summary>Moves a file into the holding folder.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The held item.</returns>
    HeldItem Hold(string path);

    /// <summary>Moves a held file back to its original place (a file that appeared there meanwhile is kept: the restored file gets a " (2)" name).</summary>
    /// <param name="item">The held item.</param>
    /// <returns>The path the file was restored to.</returns>
    string Restore(HeldItem item);

    /// <summary>Sends every file held in this session to the Recycle Bin and starts a new session.</summary>
    void CommitSession();

    /// <summary>Sends files left by earlier sessions (crashes) to the Recycle Bin.</summary>
    void RecycleLeftovers();
}

/// <summary>Shell helpers (Explorer, URIs, shortcuts, Recycle Bin). Implemented by platform; callable from any thread.</summary>
public interface IShellOperations
{
    /// <summary>Opens a folder in Explorer, creating it when missing ("Open My Bulbs Folder").</summary>
    /// <param name="path">The folder.</param>
    void OpenFolder(string path);

    /// <summary>Opens Explorer with a file selected ("Show in Folder").</summary>
    /// <param name="filePath">The file.</param>
    void ShowInFolder(string filePath);

    /// <summary>Opens a local settings URI (<c>ms-settings:apps-volume</c>, <c>ms-settings:startupapps</c>); web URIs are refused (offline product).</summary>
    /// <param name="uri">The URI.</param>
    void OpenSettingsUri(string uri);

    /// <summary>
    /// Opens the project's home page (<see cref="ProjectInfo.HomePage"/>) in the default browser, only when the user asks
    /// (the link in About). No other web page is ever opened: Holiday Lights itself works offline.
    /// </summary>
    void OpenProjectHomePage();

    /// <summary>Runs a Control Panel item (<c>desk.cpl,,@screensaver</c>).</summary>
    /// <param name="arguments">The arguments of <c>control.exe</c>.</param>
    void OpenControlPanel(string arguments);

    /// <summary>Moves a file to the Recycle Bin (<c>IFileOperation</c> with recycle-on-delete).</summary>
    /// <param name="path">The file.</param>
    /// <returns>True when it was recycled.</returns>
    bool MoveToRecycleBin(string path);

    /// <summary>Resolves a <c>.lnk</c> shortcut (<c>IShellLink</c>).</summary>
    /// <param name="shortcutPath">The shortcut file.</param>
    /// <returns>The target path, or null when it cannot be resolved.</returns>
    string? ResolveShortcut(string shortcutPath);
}

/// <summary>Facts about the user's Windows (PRODUCT-SPEC 4.5, 5.11, 3.2.10). Implemented by platform.</summary>
public interface ISystemInfo
{
    /// <summary>Windows "Animation effects" (<c>SPI_GETCLIENTAREAANIMATION</c>); false = reduced motion.</summary>
    bool AnimationsEnabled { get; }

    /// <summary>High Contrast is on.</summary>
    bool HighContrast { get; }

    /// <summary>The taskbar uses the light theme (<c>Personalize\SystemUsesLightTheme</c>; tray icon variant).</summary>
    bool TaskbarUsesLightTheme { get; }

    /// <summary>The Windows region as a two-letter code (GeoInfo), e.g. "US".</summary>
    string RegionCode { get; }

    /// <summary>The Windows account display name (Bulb Editing author default).</summary>
    string UserDisplayName { get; }

    /// <summary>The session is a Remote Desktop session (<c>SM_REMOTESESSION</c>).</summary>
    bool IsRemoteSession { get; }

    /// <summary>The Windows build number (Copy Version Info).</summary>
    int OsBuild { get; }

    /// <summary>Raised (on the creating thread) when one of the values changed (<c>WM_SETTINGCHANGE</c>, e.g. "ImmersiveColorSet").</summary>
    event EventHandler? Changed;
}

/// <summary>
/// The per-user Run value (<c>HKCU\...\Run\Holiday Lights</c> = <c>"&lt;install&gt;\HolidayLights.exe" --autostart</c>;
/// PRODUCT-SPEC 6.6.1). Implemented by platform; writes are skipped when system changes are not allowed.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>True when the Run value exists and points at this installation.</summary>
    bool IsEnabled { get; }

    /// <summary>True when the user turned startup off in Windows Settings (<c>StartupApproved\Run</c> first byte odd; never written).</summary>
    bool IsDisabledByWindows { get; }

    /// <summary>Writes the Run value.</summary>
    void Enable();

    /// <summary>Removes the Run value.</summary>
    void Disable();
}

/// <summary>State of the per-user <c>.bul</c> association (PRODUCT-SPEC 6.9).</summary>
public enum FileAssociationState
{
    /// <summary>No association.</summary>
    NotRegistered,

    /// <summary>Registered to this installation.</summary>
    Registered,

    /// <summary>Registered to a missing exe (repaired at start).</summary>
    Broken,

    /// <summary>The user chose another program for <c>.bul</c> (never taken over).</summary>
    OwnedByAnotherProgram,
}

/// <summary>
/// The per-user <c>.bul</c> association: ProgID <c>TigerTech.HolidayLights.Bulb</c>, <c>open</c> =
/// <c>"&lt;install&gt;\HolidayLights.exe" --open "%1"</c>. Implemented by platform; writes are skipped when system changes
/// are not allowed.
/// </summary>
public interface IFileAssociation
{
    /// <summary>Reads the state.</summary>
    /// <returns>The state.</returns>
    FileAssociationState GetState();

    /// <summary>Writes the association and notifies the shell (<c>SHCNE_ASSOCCHANGED</c>).</summary>
    void Register();

    /// <summary>Removes the association (only when it is ours).</summary>
    void Unregister();
}

/// <summary>Which screen saver Windows uses, from Holiday Lights' point of view (PRODUCT-SPEC 3.5.2).</summary>
public enum ScreenSaverState
{
    /// <summary><c>SCRNSAVE.EXE</c> is another saver or empty.</summary>
    NotOurs,

    /// <summary>It is our <c>.scr</c> and the screen saver is active.</summary>
    Ours,

    /// <summary>It is ours but <c>ScreenSaveActive</c> = 0.</summary>
    OursButTurnedOff,

    /// <summary>It points at Holiday Lights 5.4 (string 2 "TigerTechHolidayLights", or a missing <c>HOLIDA~1.SCR</c> / <c>Holiday Lights.scr</c>).</summary>
    Legacy54,
}

/// <summary>The Windows screen saver configuration.</summary>
/// <param name="State">The state.</param>
/// <param name="ScrnsaveExe">The current <c>SCRNSAVE.EXE</c> value (null when empty).</param>
/// <param name="TimeoutSeconds"><c>ScreenSaveTimeOut</c>.</param>
/// <param name="PolicyManaged">A policy value exists under <c>HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop</c>.</param>
public sealed record ScreenSaverStatus(ScreenSaverState State, string? ScrnsaveExe, int TimeoutSeconds, bool PolicyManaged);

/// <summary>
/// Reads and changes the Windows screen saver (only the Screen Saver page and the Welcome card call the changing members;
/// "Preview Screen Saver" never does). Implemented by platform; writes go through
/// <c>SystemParametersInfo(SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)</c> and are skipped when system changes are not allowed.
/// </summary>
public interface IScreenSaverRegistration
{
    /// <summary>Reads the current configuration (our <c>.scr</c> is <see cref="DataPaths.InstalledScreenSaverPath"/>).</summary>
    /// <returns>The status.</returns>
    ScreenSaverStatus GetStatus();

    /// <summary>"Use Holiday Lights as My Screen Saver": sets <c>SCRNSAVE.EXE</c> to the full long path, <c>ScreenSaveActive</c> = 1, keeps the timeout (10 minutes if none).</summary>
    /// <returns>The previous values, to store in <see cref="SaverDeviceSettings.Previous"/>.</returns>
    ScreenSaverPrevious Use();

    /// <summary>"Stop Using It": restores remembered values (a remembered broken 5.4 path is not restored; "(None)" is used).</summary>
    /// <param name="previous">The remembered values, or null for "(None)".</param>
    void StopUsing(ScreenSaverPrevious? previous);

    /// <summary>"Start After": writes <c>SPI_SETSCREENSAVETIMEOUT</c>.</summary>
    /// <param name="timeout">The idle time before the saver starts.</param>
    void SetTimeout(TimeSpan timeout);

    /// <summary>"Turn It On": <c>ScreenSaveActive</c> = 1.</summary>
    void TurnOn();

    /// <summary>"Windows Screen Saver Settings...": opens <c>control desk.cpl,,@screensaver</c>.</summary>
    void OpenWindowsSettings();
}
