using System.Globalization;
using Microsoft.Win32;

namespace HolidayLights.App.Install;

/// <summary>
/// The per-user entry of Windows Settings &gt; Apps (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\HolidayLights</c>;
/// PRODUCT-SPEC 6.10): name, version, publisher, the project's home page, icon, location, size and the <c>--uninstall</c>
/// command.
/// </summary>
public sealed class UninstallEntry
{
    private readonly RegistryKey root;
    private readonly string keyPath;

    /// <summary>The entry under <c>HKCU</c>.</summary>
    public UninstallEntry()
        : this(Registry.CurrentUser, InstallLocations.UninstallKeyPath)
    {
    }

    /// <summary>The entry under any key (tests use a private key they delete).</summary>
    /// <param name="root">The root key.</param>
    /// <param name="keyPath">The entry's path below it.</param>
    internal UninstallEntry(RegistryKey root, string keyPath)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(keyPath);
        this.root = root;
        this.keyPath = keyPath;
    }

    /// <summary>Writes the entry.</summary>
    /// <param name="programFolder">The program folder.</param>
    /// <param name="version">The installed version.</param>
    /// <param name="sizeInBytes">The size of the installed files.</param>
    /// <param name="installedOn">The installation date.</param>
    public void Write(string programFolder, string version, long sizeInBytes, DateOnly installedOn)
    {
        ArgumentException.ThrowIfNullOrEmpty(programFolder);
        ArgumentNullException.ThrowIfNull(version);
        string program = System.IO.Path.Combine(programFolder, InstallLocations.ProgramFileName);
        using RegistryKey key = root.CreateSubKey(keyPath, writable: true);
        key.SetValue("DisplayName", InstallLocations.DisplayName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", ProjectInfo.Author);
        key.SetValue("URLInfoAbout", ProjectInfo.HomePage);
        key.SetValue("HelpLink", ProjectInfo.HomePage);
        key.SetValue("DisplayIcon", $"\"{program}\",0");
        key.SetValue("InstallLocation", programFolder);
        key.SetValue("UninstallString", $"\"{program}\" --uninstall");
        key.SetValue("InstallDate", installedOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeInBytes / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>The program folder the entry names, or null when there is no entry.</summary>
    /// <returns>The <c>InstallLocation</c> value.</returns>
    public string? ReadLocation()
    {
        using RegistryKey? key = root.OpenSubKey(keyPath);
        return key?.GetValue("InstallLocation") as string;
    }

    /// <summary>Removes the entry.</summary>
    public void Delete() => root.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
}
