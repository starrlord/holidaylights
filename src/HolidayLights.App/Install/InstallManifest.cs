using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HolidayLights.App.Install;

/// <summary>
/// The marker the installer leaves in the program folder (<c>HolidayLights.install.json</c>): the uninstaller removes
/// program files only from a folder that has it, so running <c>--uninstall</c> from a build folder never deletes it.
/// </summary>
public sealed record InstallManifest
{
    /// <summary>The marker file name.</summary>
    public const string FileName = "HolidayLights.install.json";

    /// <summary>The installed version.</summary>
    public string Version { get; set; } = "";

    /// <summary>When it was installed.</summary>
    public DateTimeOffset InstalledAt { get; set; }

    /// <summary>The installed files, relative to the program folder.</summary>
    public IReadOnlyList<string> Files { get; set; } = [];

    /// <summary>Reads the marker of a folder.</summary>
    /// <param name="folder">The program folder.</param>
    /// <returns>The manifest, or null when the folder was not installed by the installer.</returns>
    public static InstallManifest? Read(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string path = Path.Combine(folder, FileName);
        try
        {
            using FileStream stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, InstallManifestJsonContext.Default.InstallManifest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the marker into a folder.</summary>
    /// <param name="folder">The program folder.</param>
    public void Write(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, InstallManifestJsonContext.Default.InstallManifest));
    }
}

/// <summary>Source-generated JSON metadata of <see cref="InstallManifest"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(InstallManifest))]
internal sealed partial class InstallManifestJsonContext : JsonSerializerContext;
