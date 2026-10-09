namespace HolidayLights.Core.Seasons;

/// <summary>The region rules of the Theme Calendar (PRODUCT-SPEC 5.11).</summary>
internal static class CalendarRegions
{
    /// <summary>The United States: July 4th and US Thanksgiving, Christmas from the day after Thanksgiving.</summary>
    public const string UnitedStates = "US";

    /// <summary>Canada: Canadian Thanksgiving.</summary>
    public const string Canada = "CA";

    private static readonly HashSet<string> SouthernHemisphere = new(StringComparer.Ordinal)
    {
        "AR", "AU", "BO", "BR", "CL", "LS", "MG", "MZ", "NA", "NZ", "PE", "PY", "SZ", "UY", "ZA", "ZM", "ZW",
    };

    private static readonly HashSet<string> OrthodoxEaster = new(StringComparer.Ordinal)
    {
        "GR", "CY", "RU", "UA", "RS", "BG", "RO", "GE", "MK", "ME", "BY", "MD",
    };

    /// <summary>
    /// Normalizes a region code: two letters (ISO 3166) are upper-cased, three digits (UN M.49 areas such as "419") are
    /// kept; anything else is the default region, the United States.
    /// </summary>
    /// <param name="region">The stored or Windows region.</param>
    /// <returns>The normalized code.</returns>
    public static string Normalize(string? region)
    {
        string code = region?.Trim().ToUpperInvariant() ?? "";
        bool letters = code.Length == 2 && code.All(char.IsAsciiLetterUpper);
        bool digits = code.Length == 3 && code.All(char.IsAsciiDigit);
        return letters || digits ? code : UnitedStates;
    }

    /// <summary>True for the regions whose seasons are those of the southern hemisphere.</summary>
    /// <param name="region">A normalized region.</param>
    /// <returns>True for the southern hemisphere.</returns>
    public static bool IsSouthernHemisphere(string region) => SouthernHemisphere.Contains(region);

    /// <summary>True for the regions that celebrate Orthodox Easter (Julian computus).</summary>
    /// <param name="region">A normalized region.</param>
    /// <returns>True for the Julian computus.</returns>
    public static bool UsesOrthodoxEaster(string region) => OrthodoxEaster.Contains(region);
}
