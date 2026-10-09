using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.App.ScreenSaver.Simulation;

namespace HolidayLights.App.ScreenSaver.Rendering;

/// <summary>The message of the main display, laid out: its text, resolved font, colour, range and wrapped height (DIPs).</summary>
/// <param name="Text">The message.</param>
/// <param name="Font">The font to draw with (a missing family already replaced by its look-alike).</param>
/// <param name="Color">The message colour.</param>
/// <param name="Range">Where it bounces.</param>
/// <param name="TextHeight">The height of the wrapped text.</param>
internal sealed record SaverMessage(string Text, SaverFont Font, RgbColor Color, TextRange Range, int TextHeight);

/// <summary>
/// The message text (PRODUCT-SPEC 6.2.2, 6.2.3; 5.4 <c>Saver_DrawMessage</c>): WPF text in the chosen font, centred lines
/// wrapped at the text box, drawn in black and again in the message colour 2 DIPs up and left (a 2-DIP shadow).
/// </summary>
/// <remarks>Uses WPF text formatting: call from an STA thread.</remarks>
internal static class SaverText
{
    /// <summary>The colour copy sits this far up and left of the black copy (5.4: 2 pixels).</summary>
    public const int ShadowOffset = 2;

    /// <summary>DIPs per point: 36 pt = 48 DIPs (5.4 stored -48 px at 96 DPI).</summary>
    public const double DipsPerPoint = 96.0 / 72.0;

    /// <summary>The font to draw with: the family itself when installed, else its look-alike, else Arial Bold 36 pt (6.2.3).</summary>
    /// <param name="font">The configured font.</param>
    /// <returns>The font in effect.</returns>
    public static SaverFont ResolveFont(SaverFont font) => SaverFontSubstitutes.Resolve(font, IsInstalled);

    /// <summary>
    /// True when a font is installed (case-insensitive, any language's name), including GDI faces that WPF files under another
    /// family, such as Arial Black (<see cref="InstalledFonts"/>).
    /// </summary>
    /// <param name="family">The family name.</param>
    /// <returns>True when installed.</returns>
    public static bool IsInstalled(string family) => InstalledFonts.IsInstalled(family);

    /// <summary>Lays the message out on a screen: resolves the font, wraps at the text box and computes the bounce range.</summary>
    /// <param name="look">The saver look (message, font, colour).</param>
    /// <param name="field">The simulated screen.</param>
    /// <param name="centeredPicture">The rectangle of a centred background picture, or null.</param>
    /// <returns>The message, or null when it is empty.</returns>
    public static SaverMessage? Layout(SaverLook look, SaverField field, RectI? centeredPicture)
    {
        ArgumentNullException.ThrowIfNull(look);
        if (string.IsNullOrEmpty(look.Message))
        {
            return null;
        }

        SaverFont font = ResolveFont(look.Font);
        int boxWidth = SaverLayoutRules.MessageRange(field, 0, null).Width;
        int height = (int)Math.Ceiling(Format(look.Message, font, Brushes.Black, boxWidth, 1.0).Height);
        TextRange range = SaverLayoutRules.MessageRange(field, height, centeredPicture);
        return new SaverMessage(look.Message, font, look.Color, range, height);
    }

    /// <summary>
    /// Draws the message for a scale: the black copy at (2, 2) and the coloured copy at (0, 0), each clipped to its text box
    /// as GDI did. The image's top-left belongs at (Left - 2, Top - 2) of the text.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="scale">Target pixels per DIP.</param>
    /// <returns>The premultiplied image.</returns>
    public static PremultipliedImage Render(SaverMessage message, double scale)
    {
        ArgumentNullException.ThrowIfNull(message);
        int boxWidth = message.Range.Width;
        double width = boxWidth + ShadowOffset;
        double height = message.TextHeight + ShadowOffset;
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));

        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
        var color = new SolidColorBrush(Color.FromRgb(message.Color.R, message.Color.G, message.Color.B));
        color.Freeze();
        using (DrawingContext context = visual.RenderOpen())
        {
            DrawClipped(context, Format(message.Text, message.Font, Brushes.Black, boxWidth, scale), ShadowOffset, boxWidth, message.TextHeight);
            DrawClipped(context, Format(message.Text, message.Font, color, boxWidth, scale), 0, boxWidth, message.TextHeight);
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var image = new PremultipliedImage(pixelWidth, pixelHeight);
        bitmap.CopyPixels(image.Pixels, pixelWidth * 4, 0);
        return image;
    }

    private static void DrawClipped(DrawingContext context, FormattedText text, double offset, double width, double height)
    {
        context.PushClip(new RectangleGeometry(new Rect(offset, offset, width, height)));
        context.DrawText(text, new Point(offset, offset));
        context.Pop();
    }

    private static FormattedText Format(string text, SaverFont font, Brush brush, double maxWidth, double pixelsPerDip)
    {
        var typeface = new Typeface(
            new FontFamily(font.Family),
            font.Italic ? FontStyles.Italic : FontStyles.Normal,
            font.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        var formatted = new FormattedText(
            text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, font.SizePt * DipsPerPoint, brush, pixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            TextAlignment = TextAlignment.Center,
        };
        var decorations = new TextDecorationCollection();
        if (font.Underline)
        {
            decorations.Add(TextDecorations.Underline);
        }

        if (font.Strikeout)
        {
            decorations.Add(TextDecorations.Strikethrough);
        }

        if (decorations.Count > 0)
        {
            formatted.SetTextDecorations(decorations);
        }

        return formatted;
    }
}
