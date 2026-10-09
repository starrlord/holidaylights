using System.Windows;

namespace HolidayLights.App.Controls;

/// <summary>
/// The glyph of a button whose content is a label shown with <c>HL.Template.GlyphLabel</c> (a Segoe Fluent Icons glyph
/// before the label). The template's text is created by the button's template, so it follows the Fluent foreground of
/// the button's states (hover, pressed, disabled), which content elements of the button itself would not.
/// </summary>
public static class ButtonContent
{
    /// <summary>Identifies the attached <c>Glyph</c> property.</summary>
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(ButtonContent), new PropertyMetadata(null));

    /// <summary>The glyph of a button.</summary>
    /// <param name="element">The button.</param>
    /// <returns>The glyph, or null.</returns>
    public static string? GetGlyph(DependencyObject element) => (string?)element.GetValue(GlyphProperty);

    /// <summary>Sets the glyph of a button.</summary>
    /// <param name="element">The button.</param>
    /// <param name="value">A <see cref="Glyphs"/> code point.</param>
    public static void SetGlyph(DependencyObject element, string? value) => element.SetValue(GlyphProperty, value);
}
