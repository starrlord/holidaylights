namespace HolidayLights.Platform.Legacy;

/// <summary>Where 5.4 kept its state; tests point these at a private registry key and folder.</summary>
/// <param name="UserKeyPath">The main key under HKCU.</param>
/// <param name="MachineKeyPath">The key under HKLM (32-bit view) that holds the installer's <c>Path</c>, or null to skip HKLM.</param>
/// <param name="StartupFolder">The Startup folder, or null for the user's own.</param>
internal sealed record LegacyRegistryLocations(string UserKeyPath, string? MachineKeyPath, string? StartupFolder)
{
    /// <summary>The 5.4 key path.</summary>
    public const string KeyPath = @"Software\Tiger Technologies\Holiday Lights";

    /// <summary>The real locations.</summary>
    public static LegacyRegistryLocations Default { get; } = new(KeyPath, KeyPath, null);

    /// <summary>The Startup folder to search.</summary>
    public string Startup => StartupFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
}
