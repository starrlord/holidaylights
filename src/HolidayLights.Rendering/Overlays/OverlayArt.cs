namespace HolidayLights.Rendering.Overlays;

/// <summary>
/// Draws the on-screen pill (PRODUCT-SPEC 3.12: 36 DIP high, #202020 at 92 %, 18 DIP radius, icon and white text, an optional
/// second line) and the Identify number card into premultiplied pictures, at the DPI of their display.
/// </summary>
internal static class OverlayArt
{
    private const string TextFace = "Segoe UI";
    private const int SemiBold = 600;
    private const int Regular = 400;
    private const double PillHeight = 36;
    private const double PillSecondLineHeight = 20;
    private const double PillRadius = 18;
    private const double PillPaddingLeft = 14;
    private const double PillPaddingRight = 18;
    private const double PillIconSize = 16;
    private const double PillIconGap = 10;
    private const double PillTextSize = 14;
    private const double PillSecondTextSize = 12;
    private const double IdentifySize = 160;
    private const double IdentifyRadius = 8;
    private const double IdentifyTextSize = 96;
    private const double BackgroundOpacity = 0.92;
    private const byte BackgroundGray = 0x20;
    private const double BorderOpacity = 0.10;
    private const double SecondLineOpacity = 0.85;
    private const double DimmedIconOpacity = 0.6;

    private static readonly string[] IconFaces = ["Segoe Fluent Icons", "Segoe MDL2 Assets"];

    /// <summary>The icon of a pill glyph (Segoe Fluent Icons code points: Pin, TVMonitor, Lightbulb).</summary>
    public static string IconOf(PillGlyph glyph) => glyph switch
    {
        PillGlyph.OnTop => "",
        PillGlyph.OnDesktop => "",
        _ => "",
    };

    /// <summary>Draws the pill.</summary>
    /// <param name="request">Glyph and texts.</param>
    /// <param name="showSecondLine">Include the second line.</param>
    /// <param name="dpi">The DPI of the display.</param>
    public static PremultipliedImage Pill(PillRequest request, bool showSecondLine, int dpi)
    {
        double scale = dpi / 96.0;
        int Px(double dip) => (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);

        var textFont = new FontSpec(TextFace, Px(PillTextSize), SemiBold);
        var secondFont = new FontSpec(TextFace, Px(PillSecondTextSize), Regular);
        string? secondLine = showSecondLine ? request.SecondLine : null;
        string icon = IconOf(request.Glyph);
        FontSpec? iconFont = FindIconFont(icon, Px(PillIconSize));

        SizeI text = GdiText.Measure(request.Text, textFont);
        SizeI second = secondLine is null ? default : GdiText.Measure(secondLine, secondFont);
        int iconWidth = iconFont is null ? 0 : Px(PillIconSize) + Px(PillIconGap);
        int height = Px(PillHeight + (secondLine is null ? 0 : PillSecondLineHeight));
        int width = Px(PillPaddingLeft) + iconWidth + Math.Max(text.Width, second.Width) + Px(PillPaddingRight);
        int firstLineTop = (Px(PillHeight) - text.Height) / 2;
        int textLeft = Px(PillPaddingLeft) + iconWidth;

        var runs = new List<TextRun> { new(request.Text, textFont, textLeft, firstLineTop) };
        if (secondLine is not null)
        {
            runs.Add(new TextRun(secondLine, secondFont, textLeft, Px(PillHeight) - Px(6)));
        }

        byte[] textCoverage = GdiText.RenderCoverage(width, height, runs);
        byte[]? iconCoverage = null;
        if (iconFont is { } font)
        {
            SizeI iconSize = GdiText.Measure(icon, font);
            iconCoverage = GdiText.RenderCoverage(width, height, [new TextRun(icon, font, Px(PillPaddingLeft), (Px(PillHeight) - iconSize.Height) / 2)]);
        }

        double iconOpacity = request.Glyph == PillGlyph.LightsOff ? DimmedIconOpacity : 1.0;
        int secondLineTop = Px(PillHeight) - Px(6);
        return Compose(width, height, Px(PillRadius), textCoverage, iconCoverage, iconOpacity, y => y >= secondLineTop ? SecondLineOpacity : 1.0);
    }

    /// <summary>Draws the Identify card with a display number.</summary>
    public static PremultipliedImage Identify(int number, int dpi)
    {
        double scale = dpi / 96.0;
        int Px(double dip) => (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);

        string text = number.ToString(System.Globalization.CultureInfo.CurrentCulture);
        var font = new FontSpec(TextFace, Px(IdentifyTextSize), SemiBold);
        SizeI size = GdiText.Measure(text, font);
        int height = Px(IdentifySize);
        int width = Math.Max(height, size.Width + Px(64));
        byte[] coverage = GdiText.RenderCoverage(width, height, [new TextRun(text, font, (width - size.Width) / 2, (height - size.Height) / 2)]);
        return Compose(width, height, Px(IdentifyRadius), coverage, null, 1.0, _ => 1.0);
    }

    /// <summary>Coverage of a rounded rectangle filling <paramref name="width"/> x <paramref name="height"/> at a pixel centre (signed distance).</summary>
    internal static double RoundedRectangleDistance(double x, double y, int width, int height, double radius)
    {
        double qx = Math.Abs(x - width / 2.0) - (width / 2.0 - radius);
        double qy = Math.Abs(y - height / 2.0) - (height / 2.0 - radius);
        double outside = Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2));
        return outside + Math.Min(Math.Max(qx, qy), 0) - radius;
    }

    private static FontSpec? FindIconFont(string icon, int pixelHeight)
    {
        foreach (string face in IconFaces)
        {
            var font = new FontSpec(face, pixelHeight, Regular);
            if (GdiText.HasGlyphs(icon, font))
            {
                return font;
            }
        }

        return null;
    }

    private static PremultipliedImage Compose(
        int width, int height, int radius, byte[] textCoverage, byte[]? iconCoverage, double iconOpacity, Func<int, double> textOpacityAtRow)
    {
        var image = new PremultipliedImage(width, height);
        double radiusPx = Math.Min(radius, Math.Min(width, height) / 2.0);
        for (int y = 0; y < height; y++)
        {
            double textOpacity = textOpacityAtRow(y);
            for (int x = 0; x < width; x++)
            {
                double distance = RoundedRectangleDistance(x + 0.5, y + 0.5, width, height, radiusPx);
                double fill = Math.Clamp(0.5 - distance, 0, 1);
                if (fill <= 0)
                {
                    continue;
                }

                double border = fill - Math.Clamp(0.5 - (distance + 1), 0, 1);

                // Premultiplied source-over, bottom to top: background, border, icon, text.
                double alpha = BackgroundOpacity * fill;
                double color = BackgroundGray / 255.0 * alpha;
                (color, alpha) = Over(color, alpha, 1.0, BorderOpacity * border);
                int index = y * width + x;
                if (iconCoverage is not null)
                {
                    (color, alpha) = Over(color, alpha, 1.0, iconCoverage[index] / 255.0 * iconOpacity * fill);
                }

                (color, alpha) = Over(color, alpha, 1.0, textCoverage[index] / 255.0 * textOpacity * fill);
                byte a = (byte)Math.Round(alpha * 255);
                byte c = (byte)Math.Round(Math.Min(color, alpha) * 255);
                image.Pixels[index] = Bgra32.Pack(c, c, c, a);
            }
        }

        return image;
    }

    /// <summary>Premultiplied gray <paramref name="sourceColor"/> at <paramref name="sourceAlpha"/> over the destination.</summary>
    private static (double Color, double Alpha) Over(double destinationColor, double destinationAlpha, double sourceColor, double sourceAlpha) =>
        (sourceColor * sourceAlpha + destinationColor * (1 - sourceAlpha), sourceAlpha + destinationAlpha * (1 - sourceAlpha));
}
