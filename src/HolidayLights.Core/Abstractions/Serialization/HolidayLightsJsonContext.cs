using System.Text.Json;
using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>
/// Source-generated System.Text.Json metadata for every persisted or transmitted contract type: <c>settings.json</c>
/// (<see cref="AppSettings"/>), theme files (<see cref="ThemeDefinition"/>) and the instance pipe
/// (<see cref="InstanceMessage"/>).
/// </summary>
/// <remarks>
/// Camel-case property names, enums as the camel-case names declared on each enum, indented output (<see cref="Compact"/>
/// for single-line output), comments and trailing commas tolerated on read, unknown properties ignored, missing
/// properties keep their initializer defaults.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ThemeDefinition))]
[JsonSerializable(typeof(InstanceMessage))]
public sealed partial class HolidayLightsJsonContext : JsonSerializerContext
{
    // The attribute's options without indentation. Built explicitly (and lazily) because the generated Default lives in
    // another part of this class, whose static initializers run in an unspecified order relative to this part.
    private static readonly Lazy<HolidayLightsJsonContext> CompactContext = new(() => new HolidayLightsJsonContext(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = false,
    }));

    /// <summary>The same metadata with single-line output (the instance pipe).</summary>
    public static HolidayLightsJsonContext Compact => CompactContext.Value;
}

/// <summary>Serialization entry points with the shared options.</summary>
public static class HolidayLightsJson
{
    /// <summary>Serializes settings (indented, as written to <c>settings.json</c>).</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, HolidayLightsJsonContext.Default.AppSettings);

    /// <summary>Parses settings; missing values keep their defaults.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="JsonException">The text is not valid settings JSON.</exception>
    public static AppSettings DeserializeSettings(string json) =>
        JsonSerializer.Deserialize(json, HolidayLightsJsonContext.Default.AppSettings) ?? throw new JsonException("The settings file is empty.");

    /// <summary>Serializes a theme (indented, as written to a theme file).</summary>
    /// <param name="theme">The theme.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(ThemeDefinition theme) => JsonSerializer.Serialize(theme, HolidayLightsJsonContext.Default.ThemeDefinition);

    /// <summary>Parses a theme file; missing values stay null (they take their defaults when the theme is loaded).</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The theme.</returns>
    /// <exception cref="JsonException">The text is not valid theme JSON.</exception>
    public static ThemeDefinition DeserializeTheme(string json) =>
        JsonSerializer.Deserialize(json, HolidayLightsJsonContext.Default.ThemeDefinition) ?? throw new JsonException("The theme file is empty.");
}
