using System.Buffers;

namespace HolidayLights.Core.Themes;

/// <summary>
/// Theme name rules (PRODUCT-SPEC 3.6.3): 1-63 characters after trimming, none of <c>\ / : * ? " &lt; &gt; |</c> (nor
/// control characters, which no file name can hold). Names compare case-insensitively.
/// </summary>
public static class ThemeNames
{
    /// <summary>The longest theme name (the 5.4 Save Theme edit control).</summary>
    public const int MaxLength = 63;

    /// <summary>The comparer for theme names.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create("\\/:*?\"<>|");

    /// <summary>Validates a candidate name.</summary>
    /// <param name="name">The name as typed.</param>
    /// <returns>The verdict.</returns>
    public static ThemeNameCheck Validate(string? name)
    {
        string trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return ThemeNameCheck.Empty;
        }

        if (trimmed.AsSpan().ContainsAny(InvalidCharacters) || trimmed.Any(char.IsControl))
        {
            return ThemeNameCheck.InvalidCharacters;
        }

        return trimmed.Length > MaxLength ? ThemeNameCheck.TooLong : ThemeNameCheck.Valid;
    }

    /// <summary>
    /// Makes any name valid, for themes that come from elsewhere (the 5.4 registry): characters not allowed in file names
    /// become "-", the name is trimmed and shortened to 63 characters, and an empty name becomes "Theme".
    /// </summary>
    /// <param name="name">The original name.</param>
    /// <returns>A valid name.</returns>
    public static string MakeValid(string? name)
    {
        var builder = new System.Text.StringBuilder(name?.Length ?? 0);
        foreach (char c in name ?? "")
        {
            builder.Append(InvalidCharacters.Contains(c) || char.IsControl(c) ? '-' : c);
        }

        string trimmed = builder.ToString().Trim();
        if (trimmed.Length > MaxLength)
        {
            trimmed = trimmed[..MaxLength].TrimEnd();
        }

        return trimmed.Length == 0 ? "Theme" : trimmed;
    }
}
