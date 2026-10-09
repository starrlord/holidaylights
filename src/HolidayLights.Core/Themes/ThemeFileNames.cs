using System.Buffers;

namespace HolidayLights.Core.Themes;

/// <summary>
/// File names of theme files (<c>&lt;name&gt;.json</c>, PRODUCT-SPEC 6.4.4). The name inside the file is authoritative;
/// the file name only has to be valid and unique, so names Windows reserves (CON, NUL, COM1 ...) get a "_" suffix and a
/// taken file name gets " (2)", " (3)" ...
/// </summary>
internal static class ThemeFileNames
{
    /// <summary>The extension of theme files.</summary>
    public const string Extension = ".json";

    private static readonly HashSet<string> ReservedNames = BuildReservedNames();

    private static readonly SearchValues<char> InvalidFileNameCharacters = SearchValues.Create(Path.GetInvalidFileNameChars());

    /// <summary>Chooses a free file path for a theme name.</summary>
    /// <param name="folder">The themes folder.</param>
    /// <param name="name">A valid theme name.</param>
    /// <param name="isTaken">Tells whether a path is used by another theme (files on disk count as taken too).</param>
    /// <returns>The path.</returns>
    public static string ChoosePath(string folder, string name, Func<string, bool> isTaken)
    {
        string stem = StemFor(name);
        string path = Path.Combine(folder, stem + Extension);
        for (int n = 2; isTaken(path) || File.Exists(path); n++)
        {
            path = Path.Combine(folder, $"{stem} ({n}){Extension}");
        }

        return path;
    }

    /// <summary>True when <paramref name="path"/> is the natural file name of <paramref name="name"/> (ignoring case).</summary>
    /// <param name="path">A theme file.</param>
    /// <param name="name">A theme name.</param>
    /// <returns>True when no rename is needed.</returns>
    public static bool IsNaturalPathFor(string path, string name) =>
        string.Equals(Path.GetFileName(path), StemFor(name) + Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The file name stem for a theme name.</summary>
    /// <param name="name">A valid theme name.</param>
    /// <returns>The stem.</returns>
    public static string StemFor(string name)
    {
        string stem = string.Concat(name.Select(c => InvalidFileNameCharacters.Contains(c) ? '-' : c));
        return ReservedNames.Contains(stem.Split('.')[0].TrimEnd()) ? stem + "_" : stem;
    }

    private static HashSet<string> BuildReservedNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" };
        foreach (string device in new[] { "COM", "LPT" })
        {
            for (int n = 0; n <= 9; n++)
            {
                names.Add($"{device}{n}");
            }

            names.Add($"{device}¹");
            names.Add($"{device}²");
            names.Add($"{device}³");
        }

        return names;
    }
}
