using HolidayLights.Platform.Integration;
using HolidayLights.Tests.Shared;
using Microsoft.Win32;

namespace HolidayLights.Tests.Platform;

/// <summary>
/// A private stand-in for <c>HKEY_CURRENT_USER</c>: <c>HKCU\Software\HolidayLightsTests\&lt;guid&gt;</c>, deleted on dispose
/// (see <see cref="TestRegistryKeys"/>).
/// </summary>
internal sealed class TestRegistryRoot : IDisposable
{
    private readonly string path;
    private readonly RegistryKey key;

    public TestRegistryRoot()
    {
        path = TestRegistryKeys.NewPath();
        key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path, writable: true);
        Registry = new UserRegistry(key);
    }

    /// <summary>The registry the services under test see as HKCU.</summary>
    public UserRegistry Registry { get; }

    /// <summary>Writes a value below the root.</summary>
    public void Set(string subKey, string name, object value, RegistryValueKind kind = RegistryValueKind.String)
    {
        using RegistryKey target = key.CreateSubKey(subKey, writable: true);
        target.SetValue(name, value, kind);
    }

    /// <summary>Reads a value below the root (null when missing).</summary>
    public object? Get(string subKey, string name)
    {
        using RegistryKey? target = key.OpenSubKey(subKey);
        return target?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    /// <summary>The kind of a value below the root.</summary>
    public RegistryValueKind KindOf(string subKey, string name)
    {
        using RegistryKey? target = key.OpenSubKey(subKey);
        return target?.GetValueKind(name) ?? RegistryValueKind.Unknown;
    }

    /// <summary>True when the key exists below the root.</summary>
    public bool Exists(string subKey)
    {
        using RegistryKey? target = key.OpenSubKey(subKey);
        return target is not null;
    }

    public void Dispose()
    {
        key.Dispose();
        TestRegistryKeys.Delete(path);
    }
}
