using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>The icons of PRODUCT-SPEC 4.7 and the sizes each file contains.</summary>
internal static class IconSet
{
    /// <summary>Sizes of the application icon (PRODUCT-SPEC 4.7).</summary>
    public static readonly int[] AppSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    /// <summary>Sizes of the tray icons (16 px at 100 % scaling up to 32 px at 200 %).</summary>
    public static readonly int[] TraySizes = [16, 20, 24, 32];

    /// <summary>Sizes of the <c>.bul</c> document icon (Explorer's list, details, tiles and large views).</summary>
    public static readonly int[] DocumentSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    /// <summary>The smallest application icon size that gets the warm glow ring.</summary>
    public const int GlowFromSize = 48;

    /// <summary>The application icon at one size: the lit bulb, with a cord from 40 px and a glow ring from 48 px.</summary>
    /// <param name="size">Pixel size.</param>
    /// <returns>The drawing.</returns>
    public static Drawing App(int size)
    {
        var group = new DrawingGroup();
        group.Children.Add(Canvas(size));
        if (size < GlowFromSize)
        {
            group.Children.Add(BulbArt.Create(BulbMetrics.ForSize(size), BulbPalette.LitOnLight, cord: false));
            return group;
        }

        BulbMetrics m = BulbMetrics.Master.Fit(new Rect(0, size * 0.12, size, size * 0.85));
        group.Children.Add(GlowRing(m, size));
        group.Children.Add(BulbArt.Create(m, BulbPalette.LitOnLight, cord: true, size));
        return group;
    }

    /// <summary>A tray icon: lit or unlit, for a light or a dark taskbar.</summary>
    /// <param name="size">Pixel size.</param>
    /// <param name="lit">True for "lights on".</param>
    /// <param name="darkTaskbar">True for a dark taskbar.</param>
    /// <returns>The drawing.</returns>
    public static Drawing Tray(int size, bool lit, bool darkTaskbar)
    {
        BulbPalette palette = (lit, darkTaskbar) switch
        {
            (true, false) => BulbPalette.LitOnLight,
            (true, true) => BulbPalette.LitOnDark,
            (false, false) => BulbPalette.UnlitOnLight,
            (false, true) => BulbPalette.UnlitOnDark,
        };
        var group = new DrawingGroup();
        group.Children.Add(Canvas(size));
        group.Children.Add(BulbArt.Create(BulbMetrics.ForSize(size), palette, cord: false));
        return group;
    }

    /// <summary>The <c>.bul</c> document icon: the lit bulb on a page with a folded corner (5.4 icon group 2, redrawn).</summary>
    /// <param name="size">Pixel size.</param>
    /// <returns>The drawing.</returns>
    public static Drawing Document(int size) => DocumentArt.Create(size);

    /// <summary>A transparent square that fixes the drawing's bounds to the canvas.</summary>
    /// <param name="size">Pixel size.</param>
    /// <returns>The drawing.</returns>
    public static Drawing Canvas(int size) => new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, size, size)));

    private static Drawing GlowRing(BulbMetrics m, int size)
    {
        // Wider than tall, so the ring fades out before the bottom edge of the icon instead of being cut off there.
        Point glass = BulbArt.GlassCenter(m);
        var center = new Point(glass.X, glass.Y - size * 0.03);
        double radius = size * 0.43;
        double radiusY = Math.Min(radius, size - center.Y);
        var brush = new RadialGradientBrush
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = radius,
            RadiusY = radiusY,
            MappingMode = BrushMappingMode.Absolute,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xB0, 0xFF, 0xC2, 0x4E), 0.0),
                new GradientStop(Color.FromArgb(0x80, 0xFF, 0x9A, 0x3A), 0.38),
                new GradientStop(Color.FromArgb(0x34, 0xFF, 0x6E, 0x34), 0.70),
                new GradientStop(Color.FromArgb(0x00, 0xF4, 0x52, 0x2F), 1.0),
            },
        };
        return new GeometryDrawing(brush, null, new EllipseGeometry(center, radius, radiusY));
    }
}
