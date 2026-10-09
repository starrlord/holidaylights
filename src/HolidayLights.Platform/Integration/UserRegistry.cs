using System.Security;
using Microsoft.Win32;

namespace HolidayLights.Platform.Integration;

/// <summary>
/// The registry key that plays <c>HKEY_CURRENT_USER</c> for the integration services. Production uses the real hive;
/// tests pass a temporary key (<c>HKCU\Software\HolidayLightsTests\&lt;guid&gt;</c>) so nothing of the user's is touched.
/// Paths are relative to the root, e.g. <c>Software\Classes\.bul</c>; the default value is named "".
/// </summary>
internal sealed class UserRegistry
{
    private readonly RegistryKey root;

    /// <summary>Creates the wrapper.</summary>
    /// <param name="root">The key that stands for HKCU.</param>
    public UserRegistry(RegistryKey root) => this.root = root;

    /// <summary>The real <c>HKEY_CURRENT_USER</c>.</summary>
    public static UserRegistry CurrentUser { get; } = new(Registry.CurrentUser);

    /// <summary>Reads a value.</summary>
    /// <param name="path">The key path.</param>
    /// <param name="name">The value name ("" = default).</param>
    /// <returns>The value (environment variables not expanded), or null when the key or value is missing or unreadable.</returns>
    public object? Read(string path, string name)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(path);
            return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Reads a string value (REG_SZ or REG_EXPAND_SZ).</summary>
    /// <param name="path">The key path.</param>
    /// <param name="name">The value name ("" = default).</param>
    /// <returns>The string, or null.</returns>
    public string? ReadString(string path, string name) => Read(path, name) as string;

    /// <summary>Writes a value, creating the key when needed.</summary>
    /// <param name="path">The key path.</param>
    /// <param name="name">The value name ("" = default).</param>
    /// <param name="value">The value.</param>
    /// <param name="kind">The value type.</param>
    public void Write(string path, string name, object value, RegistryValueKind kind = RegistryValueKind.String)
    {
        using RegistryKey key = root.CreateSubKey(path, writable: true);
        key.SetValue(name, value, kind);
    }

    /// <summary>Deletes a value (no-op when missing).</summary>
    /// <param name="path">The key path.</param>
    /// <param name="name">The value name ("" = default).</param>
    public void DeleteValue(string path, string name)
    {
        using RegistryKey? key = root.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>Deletes a key and everything below it (no-op when missing).</summary>
    /// <param name="path">The key path.</param>
    public void DeleteTree(string path) => root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);

    /// <summary>Deletes a key when it holds no values and no subkeys.</summary>
    /// <param name="path">The key path.</param>
    public void DeleteIfEmpty(string path)
    {
        bool empty;
        using (RegistryKey? key = root.OpenSubKey(path))
        {
            empty = key is not null && key.ValueCount == 0 && key.SubKeyCount == 0;
        }

        if (empty)
        {
            root.DeleteSubKey(path, throwOnMissingSubKey: false);
        }
    }
}
