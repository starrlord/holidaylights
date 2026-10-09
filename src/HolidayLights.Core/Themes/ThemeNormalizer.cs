using HolidayLights.Core.Settings;

namespace HolidayLights.Core.Themes;

/// <summary>
/// Puts a theme into its canonical stored form: the current schema, the trimmed name, the shipped kind derived from the
/// name, and every value that cannot be used treated as missing (it then takes its default when the theme is loaded,
/// PRODUCT-SPEC 6.4.2).
/// </summary>
internal static class ThemeNormalizer
{
    /// <summary>Normalizes a theme.</summary>
    /// <param name="theme">The theme as read or given.</param>
    /// <param name="name">The name to store (valid; see <see cref="ThemeNames"/>).</param>
    /// <returns>A normalized copy.</returns>
    public static ThemeDefinition Normalize(ThemeDefinition theme, string name)
    {
        string trimmed = name.Trim();
        return new ThemeDefinition
        {
            Schema = ThemeDefinition.SchemaId,
            Name = trimmed,
            Shipped = ShippedThemes.KindOf(trimmed),
            Arrangement = theme.Arrangement is null ? null : ValueRules.CleanArrangement(theme.Arrangement),
            Flash = theme.Flash is null ? null : Normalize(theme.Flash),
            Music = theme.Music is null ? null : Normalize(theme.Music),
            Saver = theme.Saver is null ? null : Normalize(theme.Saver),
        };
    }

    private static ThemeFlash Normalize(ThemeFlash flash) => new()
    {
        Pattern = flash.Pattern is { } pattern && Enum.IsDefined(pattern) ? pattern : null,
        Interval = flash.Interval is { } interval && ValueRules.IsValidInterval(interval) ? interval : null,
    };

    private static ThemeMusic Normalize(ThemeMusic music) => new()
    {
        EnabledSongs = music.EnabledSongs is null ? null : ValueRules.CleanIdSet(music.EnabledSongs, ValueRules.IsMediaId, MediaIds.Comparer),
        Mode = music.Mode is { } mode && Enum.IsDefined(mode) ? mode : null,
    };

    private static ThemeSaver Normalize(ThemeSaver saver) => new()
    {
        Animation = ValueRules.CanonicalAnimation(saver.Animation),
        Style = saver.Style is { } style && Enum.IsDefined(style) ? style : null,
        Message = saver.Message is null ? null : ValueRules.CleanMessage(saver.Message),
        Font = saver.Font is null ? null : ValueRules.CleanFont(saver.Font),
        Color = saver.Color,
        Background = saver.Background,
        Picture = saver.Picture is null ? null : ValueRules.IsValidPicture(saver.Picture) ? saver.Picture : SaverPictures.None,
        Placement = saver.Placement is { } placement && Enum.IsDefined(placement) ? placement : null,
    };
}
