namespace HolidayLights.Core.Abstractions;

/// <summary>
/// The screen saver font look-alikes (PRODUCT-SPEC 6.2.3): a theme font that is not installed uses the first installed
/// look-alike; any other missing font uses Arial Bold 36 pt (the 5.4 fallback). Shared by the Screen Saver page (the
/// "Creepy (not installed - using Chiller)" label) and the saver.
/// </summary>
public static class SaverFontSubstitutes
{
    private static readonly Dictionary<string, string[]> LookAlikes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Creepy"] = ["Chiller", "Ink Free"],
        ["Lucida Handwriting"] = ["Segoe Script"],
        ["Mistral"] = ["Segoe Script"],
        ["Footlight MT Light"] = ["Georgia"],
        ["Georgia Ref"] = ["Georgia"],
        ["Figaro MT"] = ["Georgia"],
    };

    /// <summary>The look-alikes of a family, in order of preference (empty when it has none).</summary>
    /// <param name="family">A font family.</param>
    /// <returns>Families to try.</returns>
    public static IReadOnlyList<string> LookAlikesOf(string family) =>
        LookAlikes.TryGetValue(family, out string[]? candidates) ? candidates : [];

    /// <summary>The font to draw with.</summary>
    /// <param name="font">The configured font.</param>
    /// <param name="isInstalled">Tells whether a family is installed (case-insensitive).</param>
    /// <returns>The font itself when installed; the first installed look-alike with the same size and effects; else Arial, 36 pt, bold.</returns>
    public static SaverFont Resolve(SaverFont font, Func<string, bool> isInstalled)
    {
        if (isInstalled(font.Family))
        {
            return font;
        }

        foreach (string candidate in LookAlikesOf(font.Family))
        {
            if (isInstalled(candidate))
            {
                return font with { Family = candidate };
            }
        }

        return new SaverFont { Family = "Arial", SizePt = 36, Bold = true };
    }
}
