namespace HolidayLights.Core.Abstractions;

/// <summary>
/// Every folder and file Holiday Lights reads or writes (PRODUCT-SPEC 6.10, D24). Every component that touches user data
/// gets its paths from one instance of this class.
/// </summary>
/// <remarks>
/// <para>Normal locations: settings and themes in <c>%APPDATA%\Holiday Lights</c>; caches, logs and the holding folder in
/// <c>%LOCALAPPDATA%\Holiday Lights</c>; the user's bulbs, songs and pictures in <c>Documents\Holiday Lights</c>; bundled
/// content in <c>&lt;install&gt;\Content</c> (read-only).</para>
/// <para>Test isolation (PRODUCT-SPEC PO-3): when the environment variable <see cref="DataRootVariable"/>
/// (<c>HOLIDAYLIGHTS_DATA_ROOT</c>) is set, or the app is started with <c>--data-root &lt;dir&gt;</c>, the three user
/// locations move to <c>&lt;dir&gt;\Roaming</c>, <c>&lt;dir&gt;\Local</c> and <c>&lt;dir&gt;\Documents</c>. Bundled
/// content still comes from the install folder.</para>
/// </remarks>
public sealed class DataPaths
{
    /// <summary>The environment variable that relocates all user data (tests, <c>--data-root</c>).</summary>
    public const string DataRootVariable = "HOLIDAYLIGHTS_DATA_ROOT";

    /// <summary>The folder name used under AppData and Documents.</summary>
    public const string FolderName = "Holiday Lights";

    /// <summary>Creates paths from explicit roots.</summary>
    /// <param name="roamingRoot">Replaces <c>%APPDATA%\Holiday Lights</c>.</param>
    /// <param name="localRoot">Replaces <c>%LOCALAPPDATA%\Holiday Lights</c>.</param>
    /// <param name="documentsRoot">Replaces <c>Documents\Holiday Lights</c>.</param>
    /// <param name="installFolder">The folder of <c>HolidayLights.exe</c> (bundled content lives in its <c>Content</c> subfolder).</param>
    /// <param name="dataRoot">The data root these roots came from, or null for the normal locations.</param>
    public DataPaths(string roamingRoot, string localRoot, string documentsRoot, string installFolder, string? dataRoot = null)
    {
        RoamingRoot = Path.GetFullPath(roamingRoot);
        LocalRoot = Path.GetFullPath(localRoot);
        DocumentsRoot = Path.GetFullPath(documentsRoot);
        InstallFolder = Path.GetFullPath(installFolder);
        DataRoot = dataRoot is null ? null : Path.GetFullPath(dataRoot);
    }

    /// <summary>The data root when user data is relocated, else null.</summary>
    public string? DataRoot { get; }

    /// <summary><c>%APPDATA%\Holiday Lights</c> (or <c>&lt;data root&gt;\Roaming</c>).</summary>
    public string RoamingRoot { get; }

    /// <summary><c>%LOCALAPPDATA%\Holiday Lights</c> (or <c>&lt;data root&gt;\Local</c>).</summary>
    public string LocalRoot { get; }

    /// <summary><c>Documents\Holiday Lights</c> (or <c>&lt;data root&gt;\Documents</c>).</summary>
    public string DocumentsRoot { get; }

    /// <summary>The folder of <c>HolidayLights.exe</c> (and <c>Holiday Lights.scr</c>).</summary>
    public string InstallFolder { get; }

    /// <summary><c>settings.json</c>.</summary>
    public string SettingsFile => Path.Combine(RoamingRoot, "settings.json");

    /// <summary>Theme files (<c>&lt;name&gt;.json</c>).</summary>
    public string ThemesFolder => Path.Combine(RoamingRoot, "Themes");

    /// <summary>Caches (bulb index, scaled sprites, thumbnails).</summary>
    public string CacheFolder => Path.Combine(LocalRoot, "Cache");

    /// <summary>The bulb index cache (<c>Cache\index.json</c>).</summary>
    public string BulbIndexFile => Path.Combine(CacheFolder, "index.json");

    /// <summary>Rotating log files.</summary>
    public string LogsFolder => Path.Combine(LocalRoot, "Logs");

    /// <summary>The holding folder for removed files (<c>Removed\&lt;session&gt;\</c>).</summary>
    public string RemovedFolder => Path.Combine(LocalRoot, "Removed");

    /// <summary>My Bulbs.</summary>
    public string MyBulbsFolder => Path.Combine(DocumentsRoot, "Bulbs");

    /// <summary>My Music.</summary>
    public string MyMusicFolder => Path.Combine(DocumentsRoot, "Music");

    /// <summary>My Pictures (screen saver backgrounds).</summary>
    public string MyPicturesFolder => Path.Combine(DocumentsRoot, "Pictures");

    /// <summary>Bundled content root (<c>&lt;install&gt;\Content</c>, read-only).</summary>
    public string ContentFolder => Path.Combine(InstallFolder, "Content");

    /// <summary>The 1,501 bundled add-on bulbs.</summary>
    public string BundledBulbsFolder => Path.Combine(ContentFolder, "Bulbs");

    /// <summary>The 46 bundled songs.</summary>
    public string BundledMusicFolder => Path.Combine(ContentFolder, "Music");

    /// <summary>The 11 bundled pictures.</summary>
    public string BundledPicturesFolder => Path.Combine(ContentFolder, "Pictures");

    /// <summary>The installed program (Run value, file association).</summary>
    public string InstalledExePath => Path.Combine(InstallFolder, "HolidayLights.exe");

    /// <summary>The installed screen saver (a copy of the program; <c>SCRNSAVE.EXE</c> points here).</summary>
    public string InstalledScreenSaverPath => Path.Combine(InstallFolder, "Holiday Lights.scr");

    /// <summary>The <c>.bul</c> document icon next to the program (file association <c>DefaultIcon</c>).</summary>
    public string BulbDocumentIconPath => Path.Combine(InstallFolder, "Assets", "BulbDocument.ico");

    /// <summary>The normal locations, or the data root from <see cref="DataRootVariable"/> when it is set.</summary>
    /// <returns>The paths for this process (install folder = <see cref="AppContext.BaseDirectory"/>).</returns>
    public static DataPaths FromEnvironment()
    {
        string? dataRoot = Environment.GetEnvironmentVariable(DataRootVariable);
        if (!string.IsNullOrWhiteSpace(dataRoot))
        {
            return ForDataRoot(dataRoot);
        }

        return new DataPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), FolderName),
            AppContext.BaseDirectory);
    }

    /// <summary>Relocates all user data under one folder (tests, <c>--data-root</c>).</summary>
    /// <param name="dataRoot">The data root.</param>
    /// <param name="installFolder">The install folder, or null for <see cref="AppContext.BaseDirectory"/>.</param>
    /// <returns>The paths.</returns>
    public static DataPaths ForDataRoot(string dataRoot, string? installFolder = null) =>
        new(
            Path.Combine(dataRoot, "Roaming"),
            Path.Combine(dataRoot, "Local"),
            Path.Combine(dataRoot, "Documents"),
            installFolder ?? AppContext.BaseDirectory,
            dataRoot);

    /// <summary>Creates a folder if needed and returns it.</summary>
    /// <param name="folder">One of the folders above.</param>
    /// <returns><paramref name="folder"/>.</returns>
    public static string EnsureFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        return folder;
    }
}
