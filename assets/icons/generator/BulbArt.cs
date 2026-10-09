using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>The colours of one bulb rendition (lit or unlit; for a light or a dark background).</summary>
/// <param name="GlassLight">Glass colour where the light is strongest.</param>
/// <param name="GlassMid">The glass colour (the palette's bulb red when lit).</param>
/// <param name="GlassDark">Glass colour at the shadowed rim.</param>
/// <param name="Outline">Contour of the whole bulb.</param>
/// <param name="Specular">The highlight on the glass (white when lit, grey when unlit).</param>
/// <param name="Lit">True when the filament glows (warm core, glow ring).</param>
/// <param name="Halo">An optional light rim outside the contour that separates a dark bulb from a dark background.</param>
internal sealed record BulbPalette(Color GlassLight, Color GlassMid, Color GlassDark, Color Outline, Color Specular, bool Lit, Color? Halo)
{
    /// <summary>The lit bulb of the app icon and of the "lights on" tray icon on a light taskbar.</summary>
    public static BulbPalette LitOnLight { get; } = new(
        Hex("#FF8A6E"), Hex("#E3342F"), Hex("#A3141B"), Hex("#6E0A10"), Hex("#FFFFFF"), true, null);

    /// <summary>The lit bulb on a dark taskbar: same glass, a softer contour.</summary>
    public static BulbPalette LitOnDark { get; } = new(
        Hex("#FF9A7E"), Hex("#EE3B34"), Hex("#B0171F"), Hex("#5A0A0E"), Hex("#FFFFFF"), true, null);

    /// <summary>The unlit bulb ("lights off") on a light taskbar: dark red glass, grey highlight.</summary>
    public static BulbPalette UnlitOnLight { get; } = new(
        Hex("#9E3A3B"), Hex("#74191E"), Hex("#4A0C10"), Hex("#2E0608"), Hex("#C4C4C4"), false, null);

    /// <summary>The unlit bulb on a dark taskbar: a light rim keeps the dark glass visible.</summary>
    public static BulbPalette UnlitOnDark { get; } = new(
        Hex("#A84446"), Hex("#7E2025"), Hex("#521015"), Hex("#2A0507"), Hex("#C8C8C8"), false, Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));

    /// <summary>Parses <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    /// <param name="text">The colour text.</param>
    /// <returns>The colour.</returns>
    public static Color Hex(string text) => (Color)ColorConverter.ConvertFromString(text);
}

/// <summary>
/// Pixel metrics of the bulb at one size, in pixels of the target. Small bulbs keep their straight contours on pixel
/// centres so they stay crisp (<see cref="Fit"/>); the glass outline is always a smooth curve.
/// </summary>
/// <param name="CenterX">Horizontal axis of the bulb.</param>
/// <param name="SocketTop">Top of the green socket.</param>
/// <param name="SocketHalf">Half width of the socket.</param>
/// <param name="BaseTop">Top of the gold screw base (= bottom of the socket).</param>
/// <param name="BaseHalf">Half width of the screw base.</param>
/// <param name="GlassTop">Top of the glass (the neck; the base overlaps it slightly).</param>
/// <param name="NeckHalf">Half width of the glass neck.</param>
/// <param name="ShoulderY">Height of the widest part of the glass.</param>
/// <param name="ShoulderHalf">Half width at the shoulder.</param>
/// <param name="TipY">Bottom of the glass.</param>
/// <param name="Stroke">Contour width (0 for none).</param>
/// <param name="Detail">How much detail fits: 0 = 16-20 px, 1 = 24-32 px, 2 = 40 px and up.</param>
internal sealed record BulbMetrics(
    double CenterX,
    double SocketTop,
    double SocketHalf,
    double BaseTop,
    double BaseHalf,
    double GlassTop,
    double NeckHalf,
    double ShoulderY,
    double ShoulderHalf,
    double TipY,
    double Stroke,
    int Detail)
{
    /// <summary>The master proportions on a 256-unit canvas (the bulb alone, without room for a glow ring).</summary>
    public static BulbMetrics Master { get; } = new(128, 0, 34, 66, 28, 84, 30, 138, 62, 256, 5, 2);

    /// <summary>Below this many pixels of bulb height the 1 px contours are snapped to pixel centres.</summary>
    private const double CrispBelow = 40;

    /// <summary>Gets the metrics of a bulb that fills a square of <paramref name="size"/> pixels.</summary>
    /// <param name="size">Icon size in pixels.</param>
    /// <returns>The metrics.</returns>
    public static BulbMetrics ForSize(int size) => Master.Fit(new Rect(0, 0, size, size));

    /// <summary>
    /// Scales the master proportions into a box (keeping the aspect ratio, centred). Small bulbs get a 1 px contour whose
    /// straight edges lie on pixel centres, so the contour covers whole pixels instead of smearing over two.
    /// </summary>
    /// <param name="box">The box the bulb must fill.</param>
    /// <returns>The metrics.</returns>
    public BulbMetrics Fit(Rect box)
    {
        double scale = Math.Min(box.Height / (TipY - SocketTop), box.Width / (2 * ShoulderHalf));
        double height = (TipY - SocketTop) * scale;
        double top = box.Top + (box.Height - height) / 2;
        int detail = height >= 40 ? 2 : height >= 24 ? 1 : 0;
        bool crisp = height < CrispBelow;
        double centerX = Math.Round(box.Left + box.Width / 2);

        // Crisp: 1 px contours centred on pixel centres (k + 0.5); otherwise whole or half pixels.
        double Y(double v) => crisp ? Math.Floor(top + (v - SocketTop) * scale) + 0.5 : Math.Round(top + (v - SocketTop) * scale);
        double H(double v) => crisp ? Math.Max(1.5, Math.Round(v * scale - 0.5) + 0.5) : Math.Round(v * scale * 2) / 2;
        double tip = crisp ? Math.Ceiling(top + height) - 0.5 : Math.Round(top + height);
        double glassTop = Y(GlassTop);

        // Keep one row of gold visible between the socket's and the glass's contours.
        double baseTop = crisp ? Math.Min(Y(BaseTop), glassTop - 2) : Y(BaseTop);
        return new BulbMetrics(
            centerX,
            crisp ? Math.Floor(top) + 0.5 : Math.Round(top),
            H(SocketHalf),
            baseTop,
            H(BaseHalf),
            glassTop,
            H(NeckHalf),
            top + (ShoulderY - SocketTop) * scale,
            H(ShoulderHalf),
            tip,
            crisp ? 1 : Math.Max(1, Math.Round(Stroke * scale * 2) / 2),
            detail);
    }
}

/// <summary>Builds the red C9 bulb (green socket, gold screw base, glossy glass) as a WPF drawing.</summary>
internal static class BulbArt
{
    private static readonly Color SocketLight = BulbPalette.Hex("#45C47F");
    private static readonly Color SocketMid = BulbPalette.Hex("#1F8B4C");
    private static readonly Color SocketDark = BulbPalette.Hex("#0D5A2F");
    private static readonly Color SocketLine = BulbPalette.Hex("#0A3F21");
    private static readonly Color SocketOutline = BulbPalette.Hex("#0B3A1F");
    private static readonly Color GoldLight = BulbPalette.Hex("#FFE7A3");
    private static readonly Color GoldMid = BulbPalette.Hex("#F4B942");
    private static readonly Color GoldDark = BulbPalette.Hex("#A8701A");
    private static readonly Color GoldOutline = BulbPalette.Hex("#6B4510");
    private static readonly Color CordColor = BulbPalette.Hex("#17643A");

    /// <summary>Draws the bulb.</summary>
    /// <param name="m">Pixel metrics.</param>
    /// <param name="palette">Colours.</param>
    /// <param name="cord">True to draw the green cord the bulb hangs from.</param>
    /// <param name="canvasWidth">Width of the canvas the cord spans (ignored without a cord).</param>
    /// <returns>The drawing (not frozen).</returns>
    public static DrawingGroup Create(BulbMetrics m, BulbPalette palette, bool cord, double canvasWidth = 0)
    {
        var group = new DrawingGroup();
        Geometry glass = GlassGeometry(m);
        Geometry socket = SocketGeometry(m);
        Geometry screwBase = BaseGeometry(m);

        if (palette.Halo is Color halo)
        {
            // A one-pixel keyline just outside the contour (the inner half is covered by the bulb itself).
            var silhouette = new GeometryGroup { Children = { glass, socket, screwBase } };
            group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(halo), m.Stroke + 2) { LineJoin = PenLineJoin.Round }, silhouette));
        }

        if (cord)
        {
            group.Children.Add(CordDrawing(m, canvasWidth));
        }

        group.Children.Add(new GeometryDrawing(GoldBrush(), Outline(m, GoldOutline), screwBase));
        if (m.Detail >= 1)
        {
            group.Children.Add(ThreadDrawing(m));
        }

        group.Children.Add(new GeometryDrawing(GlassBrush(palette), Outline(m, palette.Outline), glass));
        if (palette.Lit && m.Detail >= 1)
        {
            group.Children.Add(new GeometryDrawing(WarmCoreBrush(), null, glass));
        }

        group.Children.Add(RimLight(m, palette, glass));
        group.Children.Add(Specular(m, palette));
        group.Children.Add(new GeometryDrawing(SocketBrush(), Outline(m, SocketOutline), socket));
        if (m.Detail >= 1)
        {
            group.Children.Add(SocketRibs(m));
        }

        return group;
    }

    /// <summary>The centre of the glass (where the glow ring is centred).</summary>
    /// <param name="m">Pixel metrics.</param>
    /// <returns>The point.</returns>
    public static Point GlassCenter(BulbMetrics m) => new(m.CenterX, (m.ShoulderY + m.TipY) / 2 - (m.TipY - m.GlassTop) * 0.08);

    private static double Overlap(BulbMetrics m) => m.Detail == 0 ? 0.5 : Math.Max(1, (m.TipY - m.SocketTop) * 0.02);

    private static Geometry GlassGeometry(BulbMetrics m) =>
        Shapes.Join([Shapes.At(new Point(m.CenterX, 0), 0).Glass(m.GlassTop, m.NeckHalf, m.ShoulderY, m.ShoulderHalf, m.TipY)]);

    private static Geometry BaseGeometry(BulbMetrics m) =>
        new RectangleGeometry(new Rect(m.CenterX - m.BaseHalf, m.BaseTop, 2 * m.BaseHalf, m.GlassTop - m.BaseTop + Overlap(m)));

    private static Geometry SocketGeometry(BulbMetrics m)
    {
        double height = m.BaseTop - m.SocketTop;
        double radius = m.Detail == 0 ? 0.75 : Math.Min(m.SocketHalf * 0.45, height * 0.35);
        return new RectangleGeometry(new Rect(m.CenterX - m.SocketHalf, m.SocketTop, 2 * m.SocketHalf, height), radius, radius);
    }

    private static Pen? Outline(BulbMetrics m, Color color) =>
        m.Stroke <= 0 ? null : new Pen(new SolidColorBrush(color), m.Stroke) { LineJoin = PenLineJoin.Round };

    private static Brush GlassBrush(BulbPalette p) => new RadialGradientBrush
    {
        GradientOrigin = new Point(0.34, 0.30),
        Center = new Point(0.42, 0.40),
        RadiusX = 0.78,
        RadiusY = 0.72,
        GradientStops =
        {
            new GradientStop(p.GlassLight, 0.0),
            new GradientStop(p.GlassMid, 0.48),
            new GradientStop(p.GlassDark, 1.0),
        },
    };

    private static Brush WarmCoreBrush() => new RadialGradientBrush
    {
        Center = new Point(0.5, 0.42),
        GradientOrigin = new Point(0.5, 0.40),
        RadiusX = 0.36,
        RadiusY = 0.30,
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0x9A, 0xFF, 0xD5, 0x80), 0.0),
            new GradientStop(Color.FromArgb(0x40, 0xFF, 0x9E, 0x5A), 0.55),
            new GradientStop(Color.FromArgb(0x00, 0xFF, 0x80, 0x50), 1.0),
        },
    };

    private static Brush SocketBrush() => new LinearGradientBrush
    {
        StartPoint = new Point(0, 0.5),
        EndPoint = new Point(1, 0.5),
        GradientStops =
        {
            new GradientStop(SocketMid, 0.0),
            new GradientStop(SocketLight, 0.28),
            new GradientStop(SocketMid, 0.62),
            new GradientStop(SocketDark, 1.0),
        },
    };

    private static Brush GoldBrush() => new LinearGradientBrush
    {
        StartPoint = new Point(0, 0.5),
        EndPoint = new Point(1, 0.5),
        GradientStops =
        {
            new GradientStop(GoldMid, 0.0),
            new GradientStop(GoldLight, 0.30),
            new GradientStop(GoldMid, 0.62),
            new GradientStop(GoldDark, 1.0),
        },
    };

    private static Drawing ThreadDrawing(BulbMetrics m)
    {
        double height = m.GlassTop + Overlap(m) - m.BaseTop;
        int lines = m.Detail >= 2 ? 3 : 1;
        var group = new GeometryGroup();
        for (int i = 1; i <= lines; i++)
        {
            double y = m.BaseTop + height * i / (lines + 1);
            double slant = m.Detail >= 2 ? height * 0.12 : 0;
            group.Children.Add(new LineGeometry(new Point(m.CenterX - m.BaseHalf, y + slant), new Point(m.CenterX + m.BaseHalf, y - slant)));
        }

        double thickness = m.Detail >= 2 ? Math.Max(1, height * 0.07) : 1;
        return new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(0xB0, GoldDark.R, GoldDark.G, GoldDark.B)), thickness), group);
    }

    private static Drawing SocketRibs(BulbMetrics m)
    {
        double height = m.BaseTop - m.SocketTop;
        var group = new GeometryGroup();
        int ribs = m.Detail >= 2 ? 2 : 1;
        for (int i = 1; i <= ribs; i++)
        {
            double y = m.SocketTop + height * (0.42 + 0.22 * (i - 1)) + (m.Detail >= 2 ? 0 : 0.5);
            group.Children.Add(new LineGeometry(new Point(m.CenterX - m.SocketHalf + 0.5, y), new Point(m.CenterX + m.SocketHalf - 0.5, y)));
        }

        double thickness = m.Detail >= 2 ? Math.Max(1, height * 0.05) : 1;
        return new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(0xA0, SocketLine.R, SocketLine.G, SocketLine.B)), thickness), group);
    }

    private static Drawing CordDrawing(BulbMetrics m, double canvasWidth)
    {
        // The light string: a green wire that sags into the top of the socket from both sides of the canvas.
        double s = (m.TipY - m.SocketTop) / 256;
        double thickness = Math.Max(1.5, 7 * s);
        double attach = m.SocketTop + 3 * s;
        double rise = 30 * s;
        var figure = new PathFigure { StartPoint = new Point(-thickness, attach - rise) };
        figure.Segments.Add(new BezierSegment(
            new Point(m.CenterX * 0.45, attach - rise * 0.95),
            new Point(m.CenterX * 0.80, attach - rise * 0.10),
            new Point(m.CenterX, attach),
            true));
        figure.Segments.Add(new BezierSegment(
            new Point(m.CenterX + (canvasWidth - m.CenterX) * 0.20, attach - rise * 0.10),
            new Point(m.CenterX + (canvasWidth - m.CenterX) * 0.55, attach - rise * 0.95),
            new Point(canvasWidth + thickness, attach - rise),
            true));
        var pen = new Pen(new SolidColorBrush(CordColor), thickness) { LineJoin = PenLineJoin.Round };
        return new GeometryDrawing(null, pen, new PathGeometry { Figures = { figure } });
    }

    private static Drawing RimLight(BulbMetrics m, BulbPalette palette, Geometry glass)
    {
        // A soft reflection along the lower right of the glass gives the glossy, rounded look.
        Color rim = palette.Lit ? Color.FromArgb(0x70, 0xFF, 0xB0, 0x9C) : Color.FromArgb(0x50, 0xC8, 0x7A, 0x7A);
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.30, 0.38),
            GradientOrigin = new Point(0.30, 0.38),
            RadiusX = 0.80,
            RadiusY = 0.72,
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0.0),
                new GradientStop(Colors.Transparent, 0.80),
                new GradientStop(rim, 0.93),
                new GradientStop(Colors.Transparent, 1.0),
            },
        };
        return new GeometryDrawing(m.Detail >= 1 ? brush : Brushes.Transparent, null, glass);
    }

    private static Drawing Specular(BulbMetrics m, BulbPalette palette)
    {
        Color color = palette.Specular;
        if (m.Detail == 0)
        {
            // Two pixels of light on the left shoulder, like the 2003 icon.
            double x = Math.Round(m.CenterX - m.ShoulderHalf * 0.62);
            double y = Math.Round(m.ShoulderY - 1.5);
            var pixels = new GeometryGroup
            {
                Children =
                {
                    new RectangleGeometry(new Rect(x, y, 1, 2)),
                    new RectangleGeometry(new Rect(x + 1, y - 1, 1, 1)),
                },
            };
            return new GeometryDrawing(new SolidColorBrush(Color.FromArgb(palette.Lit ? (byte)0xF2 : (byte)0xC0, color.R, color.G, color.B)), null, pixels);
        }

        double half = m.ShoulderHalf;
        double glassHeight = m.TipY - m.GlassTop;
        double left = m.CenterX - half * 0.80;
        double top = m.GlassTop + glassHeight * 0.16;
        double bottom = m.ShoulderY + glassHeight * 0.20;

        // A crescent that follows the left side of the glass.
        var figure = new PathFigure { StartPoint = new Point(left + half * 0.30, top), IsClosed = true };
        figure.Segments.Add(new BezierSegment(
            new Point(left - half * 0.02, top + (bottom - top) * 0.25),
            new Point(left - half * 0.04, top + (bottom - top) * 0.75),
            new Point(left + half * 0.10, bottom),
            true));
        figure.Segments.Add(new BezierSegment(
            new Point(left + half * 0.14, top + (bottom - top) * 0.70),
            new Point(left + half * 0.18, top + (bottom - top) * 0.28),
            new Point(left + half * 0.30, top),
            true));
        var streak = new PathGeometry { Figures = { figure } };
        var streakBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(palette.Lit ? (byte)0xF0 : (byte)0xA8, color.R, color.G, color.B), 0.0),
                new GradientStop(Color.FromArgb(palette.Lit ? (byte)0xA0 : (byte)0x70, color.R, color.G, color.B), 0.6),
                new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1.0),
            },
        };

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(streakBrush, null, streak));
        double dot = Math.Max(1, half * 0.13);
        var dotCenter = new Point(left + half * 0.52, top + glassHeight * 0.02);
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(palette.Lit ? (byte)0xE6 : (byte)0x96, color.R, color.G, color.B)),
            null,
            new EllipseGeometry(dotCenter, dot, dot * 0.8)));
        return group;
    }
}
