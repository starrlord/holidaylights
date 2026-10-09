using System.Text.Json;
using System.Text.Json.Nodes;
using HolidayLights.Core.Settings;

namespace HolidayLights.Core.Themes;

/// <summary>What reading a theme file produced.</summary>
/// <param name="Theme">The normalized theme, or null when the file is unreadable.</param>
/// <param name="Problem">Why the file is unreadable, else null.</param>
/// <param name="RemovedValues">JSON paths of values that could not be read and take their defaults.</param>
internal sealed record ThemeReadResult(ThemeDefinition? Theme, string? Problem, IReadOnlyList<string> RemovedValues);

/// <summary>Reads and writes one theme file (<c>holidaylights.theme/1</c>, PRODUCT-SPEC Appendix C).</summary>
internal static class ThemeFile
{
    private const string SchemaPrefix = "holidaylights.theme/";

    /// <summary>Reads a theme file leniently: values that cannot be read are dropped (they take their defaults).</summary>
    /// <param name="path">The file.</param>
    /// <returns>The result.</returns>
    public static ThemeReadResult Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ThemeReadResult(null, e.Message, []);
        }

        if (!JsonSalvage.TryParseObject(text, out JsonObject? root, out string? problem))
        {
            return new ThemeReadResult(null, problem, []);
        }

        var removed = new List<string>();
        ThemeDefinition read;
        try
        {
            read = JsonSalvage.Deserialize(root, HolidayLightsJsonContext.Default.ThemeDefinition, removed);
        }
        catch (JsonException e)
        {
            return new ThemeReadResult(null, e.Message, removed);
        }

        if (read.Schema is null || !read.Schema.StartsWith(SchemaPrefix, StringComparison.Ordinal))
        {
            return new ThemeReadResult(null, "it is not a Holiday Lights theme", removed);
        }

        string name = ThemeNames.Validate(read.Name) == ThemeNameCheck.Valid
            ? read.Name
            : ThemeNames.MakeValid(string.IsNullOrWhiteSpace(read.Name) ? Path.GetFileNameWithoutExtension(path) : read.Name);
        return new ThemeReadResult(ThemeNormalizer.Normalize(read, name), null, removed);
    }

    /// <summary>Writes a theme atomically.</summary>
    /// <param name="path">The file.</param>
    /// <param name="theme">A normalized theme.</param>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public static void Write(string path, ThemeDefinition theme) => AtomicFile.WriteAllText(path, HolidayLightsJson.Serialize(theme));
}
