using System.Security;
using HolidayLights.Core.Legacy;
using Microsoft.Win32;

namespace HolidayLights.Platform.Legacy;

/// <summary>
/// Reads <c>HKCU\Software\Tiger Technologies\Holiday Lights</c> (values, <c>Themes</c>, <c>Included Bulb Categories</c>),
/// the HKLM (WOW6432Node) <c>Path</c> and the Startup-folder shortcuts, strictly read-only (opening keys with read access
/// only, unlike 5.4). Owner: core-settings.
/// </summary>
/// <remarks>
/// REG_SZ text is read as Unicode (the registry converted 5.4's ANSI strings when they were written); binary values are
/// passed on as stored. A theme whose name held a "\" became nested keys in 5.4; it is read back under the joined name.
/// The 5.4 program folder is the folder of the HKCU <c>Path</c>, else of the HKLM one, in its long form.
/// </remarks>
public sealed class LegacyRegistryReader : ILegacyRegistrySource
{
    private const string LogSource = "Legacy";
    private const string PathValue = "Path";

    private readonly IShellOperations shell;
    private readonly IAppLog log;
    private readonly LegacyRegistryLocations locations;

    /// <summary>Creates the reader.</summary>
    /// <param name="shell">Resolves Startup-folder shortcuts.</param>
    /// <param name="log">The log.</param>
    public LegacyRegistryReader(IShellOperations shell, IAppLog log)
        : this(shell, log, LegacyRegistryLocations.Default)
    {
    }

    /// <summary>Creates a reader of other locations (tests).</summary>
    /// <param name="shell">Resolves Startup-folder shortcuts.</param>
    /// <param name="log">The log.</param>
    /// <param name="locations">Where to read.</param>
    internal LegacyRegistryReader(IShellOperations shell, IAppLog log, LegacyRegistryLocations locations)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(log);
        this.shell = shell;
        this.log = log;
        this.locations = locations;
    }

    /// <inheritdoc />
    /// <remarks>A key that exists but cannot be read is logged and treated as absent.</remarks>
    public LegacyRegistrySnapshot? Read()
    {
        try
        {
            using RegistryKey? main = Registry.CurrentUser.OpenSubKey(locations.UserKeyPath, writable: false);
            if (main is null)
            {
                return null;
            }

            Dictionary<string, LegacyValue> values = ReadValues(main);
            string? machinePath = ReadMachinePath();
            return new LegacyRegistrySnapshot
            {
                Values = values,
                Themes = ReadThemes(main),
                IncludedBulbCategories = ReadCategories(main),
                MachinePath = machinePath,
                StartupShortcuts = LegacyStartupShortcuts.Find(locations.Startup, shell),
                ProgramFolder = ProgramFolder(values.GetValueOrDefault(PathValue)?.Text) ?? ProgramFolder(machinePath),
            };
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            log.Warn(LogSource, "The Holiday Lights 5.4 registry could not be read.", e);
            return null;
        }
    }

    /// <summary>The folder of a 5.4 program path in its long form, when it exists.</summary>
    /// <param name="exePath">The stored <c>Path</c> (e.g. <c>C:\PROGRA~1\HOLIDA~1\HOLIDA~1.EXE</c>).</param>
    /// <returns>The folder (e.g. <c>C:\Program Files\Holiday Lights</c>), or null.</returns>
    internal static string? ProgramFolder(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || Path.GetDirectoryName(exePath.Trim().Trim('"')) is not { Length: > 0 } folder)
        {
            return null;
        }

        return Directory.Exists(folder) ? LegacyNativeMethods.GetLongPath(folder) ?? folder : null;
    }

    private static Dictionary<string, LegacyValue> ReadValues(RegistryKey key)
    {
        var values = new Dictionary<string, LegacyValue>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in key.GetValueNames())
        {
            object? raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            LegacyValue? value = (key.GetValueKind(name), raw) switch
            {
                (RegistryValueKind.String or RegistryValueKind.ExpandString, string text) => LegacyValue.OfString(text),
                (RegistryValueKind.DWord, int number) => LegacyValue.OfDWord(unchecked((uint)number)),
                (_, byte[] bytes) => LegacyValue.OfBinary(bytes),
                _ => null,
            };
            if (value is not null)
            {
                values[name] = value;
            }
        }

        return values;
    }

    private static List<KeyValuePair<string, IReadOnlyDictionary<string, LegacyValue>>> ReadThemes(RegistryKey main)
    {
        var themes = new List<KeyValuePair<string, IReadOnlyDictionary<string, LegacyValue>>>();
        using RegistryKey? themesKey = main.OpenSubKey("Themes", writable: false);
        if (themesKey is not null)
        {
            AddThemes(themesKey, "", themes);
        }

        return themes;
    }

    /// <summary>Adds every theme key: a key with values, or a leaf; keys that only hold nested keys are path parts.</summary>
    private static void AddThemes(RegistryKey parent, string prefix, List<KeyValuePair<string, IReadOnlyDictionary<string, LegacyValue>>> themes)
    {
        foreach (string name in parent.GetSubKeyNames())
        {
            using RegistryKey? key = parent.OpenSubKey(name, writable: false);
            if (key is null)
            {
                continue;
            }

            string fullName = prefix + name;
            if (key.ValueCount > 0 || key.SubKeyCount == 0)
            {
                themes.Add(new(fullName, ReadValues(key)));
            }

            AddThemes(key, fullName + @"\", themes);
        }
    }

    private static Dictionary<string, string> ReadCategories(RegistryKey main)
    {
        var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using RegistryKey? key = main.OpenSubKey("Included Bulb Categories", writable: false);
        foreach (string name in key?.GetValueNames() ?? [])
        {
            if (key!.GetValue(name) is string list)
            {
                categories[name] = list;
            }
        }

        return categories;
    }

    private string? ReadMachinePath()
    {
        if (locations.MachineKeyPath is null)
        {
            return null;
        }

        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using RegistryKey? key = machine.OpenSubKey(locations.MachineKeyPath, writable: false);
        return key?.GetValue(PathValue) as string;
    }
}
