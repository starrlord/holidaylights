using HolidayLights.Core.Settings;

namespace HolidayLights.Core.Themes;

/// <summary>
/// The 13 values of a theme as they apply to settings (PRODUCT-SPEC 6.4.2): a missing value takes its 5.4 default (the
/// screen saver style is derived from the animation), and songs not named by the theme are unchecked. Also the comparisons
/// behind the "Current" badge (songs as sets, font family ignoring case) and the "Changed" badge.
/// </summary>
internal static class ThemeValues
{
    /// <summary>The values a theme gives the settings.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="known">The songs the Music Box knows.</param>
    /// <returns>The 13 values.</returns>
    public static ThemeableSettings Effective(ThemeDefinition theme, KnownSongs known)
    {
        HashSet<string> enabled = known.CheckedOfTheme(theme.Music?.EnabledSongs);
        return new ThemeableSettings
        {
            Arrangement = theme.Arrangement ?? SlotAssignment.Classic54Default,
            Flash = EffectiveFlash(theme.Flash),
            Music = new CurrentMusic
            {
                DisabledSongs = [.. known.Ordered.Where(id => !enabled.Contains(id))],
                Mode = theme.Music?.Mode ?? new CurrentMusic().Mode,
            },
            Saver = EffectiveSaver(theme.Saver),
        };
    }

    /// <summary>Captures 13 values as a theme (checked songs = known songs not unchecked).</summary>
    /// <param name="name">The theme name (valid).</param>
    /// <param name="values">The values.</param>
    /// <param name="known">The songs the Music Box knows.</param>
    /// <returns>The theme, not saved.</returns>
    public static ThemeDefinition Capture(string name, ThemeableSettings values, KnownSongs known)
    {
        HashSet<string> enabled = known.CheckedOfSettings(values.Music.DisabledSongs);
        SaverLook saver = values.Saver;
        return ThemeNormalizer.Normalize(
            new ThemeDefinition
            {
                Arrangement = values.Arrangement,
                Flash = new ThemeFlash { Pattern = values.Flash.Pattern, Interval = values.Flash.Interval },
                Music = new ThemeMusic { EnabledSongs = [.. known.Ordered.Where(enabled.Contains)], Mode = values.Music.Mode },
                Saver = new ThemeSaver
                {
                    Animation = saver.Animation,
                    Style = saver.Style,
                    Message = saver.Message,
                    Font = saver.Font,
                    Color = saver.Color,
                    Background = saver.Background,
                    Picture = saver.Picture,
                    Placement = saver.Placement,
                },
            },
            name);
    }

    /// <summary>True when two sets of 13 values are equal (songs compared as the sets of checked known songs).</summary>
    /// <param name="a">The first values.</param>
    /// <param name="b">The second values.</param>
    /// <param name="known">The songs the Music Box knows.</param>
    /// <returns>True when equal.</returns>
    public static bool Equal(ThemeableSettings a, ThemeableSettings b, KnownSongs known) =>
        a.Arrangement == b.Arrangement
        && a.Flash.Pattern == b.Flash.Pattern
        && a.Flash.Interval == b.Flash.Interval
        && a.Music.Mode == b.Music.Mode
        && known.CheckedOfSettings(a.Music.DisabledSongs).SetEquals(known.CheckedOfSettings(b.Music.DisabledSongs))
        && Equal(a.Saver, b.Saver);

    /// <summary>True when two themes hold the same values (missing values as their defaults; songs compared as stored sets).</summary>
    /// <param name="a">The first theme.</param>
    /// <param name="b">The second theme.</param>
    /// <returns>True when equal.</returns>
    public static bool Equal(ThemeDefinition a, ThemeDefinition b)
    {
        FlashSettings flashA = EffectiveFlash(a.Flash);
        FlashSettings flashB = EffectiveFlash(b.Flash);
        return (a.Arrangement ?? SlotAssignment.Classic54Default) == (b.Arrangement ?? SlotAssignment.Classic54Default)
            && flashA.Pattern == flashB.Pattern
            && flashA.Interval == flashB.Interval
            && (a.Music?.Mode ?? PlayMode.Always) == (b.Music?.Mode ?? PlayMode.Always)
            && SameSongs(a.Music?.EnabledSongs, b.Music?.EnabledSongs)
            && Equal(EffectiveSaver(a.Saver), EffectiveSaver(b.Saver));
    }

    /// <summary>The screen saver values of a theme with defaults for missing values.</summary>
    /// <param name="saver">The theme's screen saver values.</param>
    /// <returns>The values.</returns>
    public static SaverLook EffectiveSaver(ThemeSaver? saver)
    {
        var defaults = new SaverLook();
        string animation = saver?.Animation ?? defaults.Animation;
        return defaults with
        {
            Animation = animation,
            Style = saver?.Style ?? SaverAnimations.DefaultStyleFor(animation),
            Message = saver?.Message ?? defaults.Message,
            Font = saver?.Font ?? defaults.Font,
            Color = saver?.Color ?? defaults.Color,
            Background = saver?.Background ?? defaults.Background,
            Picture = saver?.Picture ?? defaults.Picture,
            Placement = saver?.Placement ?? defaults.Placement,
        };
    }

    private static FlashSettings EffectiveFlash(ThemeFlash? flash)
    {
        var defaults = new FlashSettings();
        return defaults with
        {
            Pattern = flash?.Pattern ?? defaults.Pattern,
            Interval = ValueRules.NormalizeInterval(flash?.Interval ?? defaults.Interval),
        };
    }

    private static bool SameSongs(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        (a is null && b is null)
        || (a is not null && b is not null && new HashSet<string>(a, MediaIds.Comparer).SetEquals(b));

    private static bool Equal(SaverLook a, SaverLook b) =>
        string.Equals(a.Animation, b.Animation, StringComparison.OrdinalIgnoreCase)
        && a.Style == b.Style
        && string.Equals(a.Message, b.Message, StringComparison.Ordinal)
        && Equal(a.Font, b.Font)
        && a.Color == b.Color
        && a.Background == b.Background
        && MediaIds.Comparer.Equals(a.Picture, b.Picture)
        && a.Placement == b.Placement;

    private static bool Equal(SaverFont a, SaverFont b) =>
        string.Equals(a.Family, b.Family, StringComparison.OrdinalIgnoreCase)
        && a.SizePt == b.SizePt && a.Bold == b.Bold && a.Italic == b.Italic && a.Underline == b.Underline && a.Strikeout == b.Strikeout;
}
