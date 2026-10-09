using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>Text as vector drawings (Segoe UI, the Windows 11 UI face).</summary>
internal static class TextArt
{
    /// <summary>Lays out one line of text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">Font size in DIP.</param>
    /// <param name="color">Colour.</param>
    /// <param name="weight">Weight (Normal, SemiBold, Bold).</param>
    /// <returns>The formatted text.</returns>
    public static FormattedText Format(string text, double size, Color color, FontWeight weight) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, new SolidColorBrush(color), 1.0);

    /// <summary>One line of text whose baseline starts at <paramref name="baseline"/> (left-aligned) or ends there (right-aligned).</summary>
    /// <param name="text">The text.</param>
    /// <param name="baseline">Left (or right) end of the baseline.</param>
    /// <param name="size">Font size in DIP.</param>
    /// <param name="color">Colour.</param>
    /// <param name="weight">Weight.</param>
    /// <param name="alignRight">True to end the text at <paramref name="baseline"/>.</param>
    /// <returns>The drawing.</returns>
    public static Drawing Line(string text, Point baseline, double size, Color color, FontWeight weight, bool alignRight = false)
    {
        FormattedText formatted = Format(text, size, color, weight);
        double x = alignRight ? baseline.X - formatted.WidthIncludingTrailingWhitespace : baseline.X;
        return new GeometryDrawing(new SolidColorBrush(color), null, formatted.BuildGeometry(new Point(x, baseline.Y - formatted.Baseline)));
    }

    /// <summary>One line of text with its layout box's top-left corner at <paramref name="topLeft"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="topLeft">Top-left corner.</param>
    /// <param name="size">Font size in DIP.</param>
    /// <param name="color">Colour.</param>
    /// <returns>The drawing.</returns>
    public static Drawing At(string text, Point topLeft, double size, Color color) =>
        new GeometryDrawing(new SolidColorBrush(color), null, Format(text, size, color, FontWeights.Normal).BuildGeometry(topLeft));
}
