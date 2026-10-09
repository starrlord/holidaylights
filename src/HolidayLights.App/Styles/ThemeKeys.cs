namespace HolidayLights.App.Styles;

/// <summary>
/// Keys of the settings-ui brushes in <c>Styles/Theme.xaml</c> that code sets with <c>SetResourceReference</c> and that
/// <see cref="HighContrastResources"/> replaces in High Contrast. The shared keys used by other owners are listed in
/// <see cref="AppResourceKeys"/>; the other styles are referenced by their names in XAML.
/// </summary>
public static class ThemeKeys
{
    /// <summary>Brush: text and glyphs drawn on a night well (light in every mode; WindowText in High Contrast).</summary>
    public const string OnNightWellBrush = "HL.Brush.OnNightWell";

    /// <summary>Brush: secondary text on a night well (GrayText in High Contrast).</summary>
    public const string OnNightWellSecondaryBrush = "HL.Brush.OnNightWellSecondary";

    /// <summary>Brush: the dark pill drawn over stages ("Trying On: ...", "Preview: ...").</summary>
    public const string StagePillBrush = "HL.Brush.StagePill";

    /// <summary>Brush: the filled favorite star (warm gold; the system Highlight colour in High Contrast).</summary>
    public const string FavoriteStarBrush = "HL.Brush.WarmGold";
}
