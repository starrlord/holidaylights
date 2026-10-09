using System.Text;

namespace HolidayLights.Core.Themes;

/// <summary>
/// The 19 shipped themes embedded from <c>assets/themes</c> (PRODUCT-SPEC 6.4.3, 7.2): the originals behind "Restore
/// Built-In Themes", "Restore Original" and the "Changed" badge, and the seed of a new themes folder.
/// </summary>
internal static class ShippedThemes
{
    private const string AssetFolder = "themes/";

    private static readonly Lazy<IReadOnlyList<ThemeDefinition>> Originals = new(LoadOriginals);

    /// <summary>The originals: the 11 classic themes A-Z, then the 8 new themes A-Z.</summary>
    public static IReadOnlyList<ThemeDefinition> All => Originals.Value;

    /// <summary>Finds the original of a shipped theme.</summary>
    /// <param name="name">A theme name (case-insensitive).</param>
    /// <returns>The original, or null when the name is not a shipped theme.</returns>
    public static ThemeDefinition? Find(string name) => All.FirstOrDefault(t => ThemeNames.Comparer.Equals(t.Name, name));

    /// <summary>The shipped set a theme name belongs to.</summary>
    /// <param name="name">A theme name (case-insensitive).</param>
    /// <returns>Classic, New, or null for the user's own names.</returns>
    public static ShippedThemeKind? KindOf(string name) =>
        ShippedThemeNames.Classic.Contains(name, ThemeNames.Comparer) ? ShippedThemeKind.Classic
        : ShippedThemeNames.New.Contains(name, ThemeNames.Comparer) ? ShippedThemeKind.New
        : null;

    private static IReadOnlyList<ThemeDefinition> LoadOriginals()
    {
        var themes = new List<ThemeDefinition>();
        foreach (string asset in EmbeddedAssets.List(AssetFolder))
        {
            if (asset.EndsWith(ThemeFileNames.Extension, StringComparison.OrdinalIgnoreCase))
            {
                ThemeDefinition theme = HolidayLightsJson.DeserializeTheme(Encoding.UTF8.GetString(EmbeddedAssets.ReadAllBytes(asset)));
                themes.Add(ThemeNormalizer.Normalize(theme, theme.Name));
            }
        }

        IReadOnlyList<string> order = [.. ShippedThemeNames.Classic, .. ShippedThemeNames.New];
        return [.. themes.Where(t => KindOf(t.Name) is not null).OrderBy(t => IndexOf(order, t.Name))];
    }

    private static int IndexOf(IReadOnlyList<string> order, string name)
    {
        for (int i = 0; i < order.Count; i++)
        {
            if (ThemeNames.Comparer.Equals(order[i], name))
            {
                return i;
            }
        }

        return order.Count;
    }
}
