using System.IO;

namespace HolidayLights.App.Install;

/// <summary>
/// Where the per-user installation lives (PRODUCT-SPEC 6.10): <c>%LOCALAPPDATA%\Programs\HolidayLights\</c>, one Start menu
/// entry "Holiday Lights", the uninstall entry <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\HolidayLights</c>.
/// With a data root (tests, <c>--data-root</c>) the program folder and the Start menu move under it.
/// </summary>
public static class InstallLocations
{
    /// <summary>The program folder name under <c>%LOCALAPPDATA%\Programs</c>.</summary>
    public const string ProgramFolderName = "HolidayLights";

    /// <summary>The Start menu entry (and the uninstall entry's display name).</summary>
    public const string DisplayName = "Holiday Lights";

    /// <summary>The uninstall key under <c>HKCU</c>.</summary>
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\HolidayLights";

    /// <summary>The program file.</summary>
    public const string ProgramFileName = "HolidayLights.exe";

    /// <summary>The screen saver copy of the program.</summary>
    public const string ScreenSaverFileName = "Holiday Lights.scr";

    /// <summary>The folder the installer installs into.</summary>
    /// <param name="paths">The data paths (a data root relocates the installation).</param>
    /// <returns><c>%LOCALAPPDATA%\Programs\HolidayLights</c>, or <c>&lt;data root&gt;\Programs\HolidayLights</c>.</returns>
    public static string ProgramFolder(DataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string programs = paths.DataRoot is { } root
            ? Path.Combine(root, "Programs")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        return Path.Combine(programs, ProgramFolderName);
    }

    /// <summary>The Start menu shortcut.</summary>
    /// <param name="paths">The data paths (a data root relocates the Start menu folder).</param>
    /// <returns>The <c>.lnk</c> path.</returns>
    public static string StartMenuShortcut(DataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        string programs = paths.DataRoot is { } root
            ? Path.Combine(root, "Start Menu", "Programs")
            : Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        return Path.Combine(programs, DisplayName + ".lnk");
    }

    /// <summary>The data paths of an installation in another folder (registrations point at the installed program).</summary>
    /// <param name="paths">The current data paths.</param>
    /// <param name="installFolder">The installation folder.</param>
    /// <returns>The same user locations with that install folder.</returns>
    public static DataPaths ForInstallFolder(DataPaths paths, string installFolder)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrEmpty(installFolder);
        return new DataPaths(paths.RoamingRoot, paths.LocalRoot, paths.DocumentsRoot, installFolder, paths.DataRoot);
    }
}
