using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>Where the lights are drawn, as pictured by the three option cards of General > Where Bulbs Are Drawn.</summary>
internal enum LayerScene
{
    /// <summary>On the desktop, behind the icons (the bulbs pass under the icons and every window).</summary>
    BehindIcons,

    /// <summary>On the desktop, in front of the icons (over the icons, under every window).</summary>
    InFrontOfIcons,

    /// <summary>On top of all windows (only the taskbar stays above them).</summary>
    OnTop,
}

/// <summary>
/// The vector illustrations of <c>Illustrations.xaml</c> (PRODUCT-SPEC 3.7 layer-mode cards, 160 x 96; 3.11 taskbar
/// corner, 320 x 120, light and dark). Palette: PRODUCT-SPEC 4.2.
/// </summary>
internal static class IllustrationArt
{
    /// <summary>Size of a layer-mode illustration.</summary>
    public static readonly Size LayerSize = new(160, 96);

    /// <summary>Size of the taskbar-corner illustration.</summary>
    public static readonly Size CornerSize = new(320, 120);

    private static readonly Color[] BulbColors =
    [
        BulbPalette.Hex("#E3342F"),
        BulbPalette.Hex("#F4B942"),
        BulbPalette.Hex("#2FBF63"),
        BulbPalette.Hex("#4C8DFF"),
    ];

    private static readonly Color SocketGreen = BulbPalette.Hex("#1F8B4C");
    private static readonly Color WireGreen = BulbPalette.Hex("#17643A");

    /// <summary>A layer-mode illustration: wallpaper, two desktop icons, a window, the taskbar and a frame of bulbs, stacked as in the chosen mode.</summary>
    /// <param name="scene">The mode.</param>
    /// <returns>The drawing, 160 x 96.</returns>
    public static DrawingGroup Layer(LayerScene scene)
    {
        var picture = new Rect(LayerSize);
        var group = new DrawingGroup { ClipGeometry = Shapes.Join([Shapes.Identity.Rect(picture, 6)]) };
        group.Children.Add(Wallpaper(picture));
        Drawing icons = DesktopIcons();
        Drawing window = AppWindow();
        Drawing bulbs = BulbFrame();
        Drawing[] order = scene switch
        {
            LayerScene.BehindIcons => [bulbs, icons, window],
            LayerScene.InFrontOfIcons => [icons, bulbs, window],
            _ => [icons, window, bulbs],
        };
        foreach (Drawing layer in order)
        {
            group.Children.Add(layer);
        }

        group.Children.Add(Taskbar());
        return group;
    }

    /// <summary>The Windows 11 taskbar corner with the "^" flyout open and the red bulb highlighted (Welcome card).</summary>
    /// <param name="dark">True for dark mode.</param>
    /// <returns>The drawing, 320 x 120.</returns>
    public static DrawingGroup TaskbarCorner(bool dark)
    {
        CornerColors c = dark ? CornerColors.Dark : CornerColors.Light;
        var frame = new Rect(CornerSize);
        var group = new DrawingGroup { ClipGeometry = Shapes.Join([Shapes.Identity.Rect(frame, 8)]) };
        group.Children.Add(new GeometryDrawing(
            new LinearGradientBrush(c.DesktopTop, c.DesktopBottom, 90),
            null,
            Shapes.Join([Shapes.Identity.Rect(frame)])));

        // Taskbar band with its top edge.
        group.Children.Add(Fill(c.Taskbar, Shapes.Identity.Rect(new Rect(0, 86, 320, 34))));
        group.Children.Add(Fill(c.TaskbarEdge, Shapes.Identity.Rect(new Rect(0, 86, 320, 1))));

        // The "^" button, shown pressed because its flyout is open.
        group.Children.Add(Fill(c.ButtonPressed, Shapes.Identity.Rect(new Rect(166, 91, 24, 24), 4)));
        group.Children.Add(Stroke(c.Glyph, 1.6, Shapes.Identity.Polyline(new Point(173, 105), new Point(178, 100), new Point(183, 105))));
        group.Children.Add(TrayGlyphs(c));

        // The overflow flyout above the button, with a soft shadow.
        var flyout = new Rect(140, 14, 76, 60);
        for (int layer = 1; layer <= 3; layer++)
        {
            byte alpha = (byte)(c.Shadow.A / (layer + 1));
            var spread = new Rect(flyout.X - layer, flyout.Y - layer + 2 * layer, flyout.Width + 2 * layer, flyout.Height + 2 * layer);
            group.Children.Add(Fill(Color.FromArgb(alpha, c.Shadow.R, c.Shadow.G, c.Shadow.B), Shapes.Identity.Rect(spread, 8 + layer)));
        }

        group.Children.Add(new GeometryDrawing(new SolidColorBrush(c.Flyout), new Pen(new SolidColorBrush(c.FlyoutBorder), 1), Shapes.Join([Shapes.Identity.Rect(flyout, 8)])));
        group.Children.Add(FlyoutIcons(c));

        // Our bulb: highlighted tile, warm light and the pointer about to click it.
        var tile = new Rect(167, 21, 22, 22);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(c.HighlightFill), new Pen(new SolidColorBrush(c.Accent), 1.2), Shapes.Join([Shapes.Identity.Rect(tile, 4)])));
        group.Children.Add(WarmLight(new Point(178, 33), 11));
        group.Children.Add(TrayBulb(new Point(178, 24)));
        group.Children.Add(Pointer(new Point(184, 38)));
        return group;
    }

    private static Drawing Wallpaper(Rect picture)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new LinearGradientBrush(BulbPalette.Hex("#0B1530"), BulbPalette.Hex("#1D3466"), 90),
            null,
            Shapes.Join([Shapes.Identity.Rect(picture)])));
        Shapes s = Shapes.Identity;
        var far = new PathFigure { StartPoint = new Point(0, 72), IsClosed = true };
        far.Segments.Add(new BezierSegment(new Point(30, 62), new Point(55, 64), new Point(80, 70), true));
        far.Segments.Add(new BezierSegment(new Point(105, 76), new Point(130, 60), new Point(160, 66), true));
        far.Segments.Add(new LineSegment(new Point(160, 96), true));
        far.Segments.Add(new LineSegment(new Point(0, 96), true));
        group.Children.Add(Fill(BulbPalette.Hex("#284B82"), far));
        var near = new PathFigure { StartPoint = new Point(0, 80), IsClosed = true };
        near.Segments.Add(new BezierSegment(new Point(40, 72), new Point(70, 78), new Point(100, 80), true));
        near.Segments.Add(new BezierSegment(new Point(125, 82), new Point(145, 76), new Point(160, 77), true));
        near.Segments.Add(new LineSegment(new Point(160, 96), true));
        near.Segments.Add(new LineSegment(new Point(0, 96), true));
        group.Children.Add(Fill(BulbPalette.Hex("#3A64A8"), near));
        group.Children.Add(Fill(
            Color.FromArgb(0xB0, 0xF5, 0xF8, 0xFF),
            s.Ellipse(new Point(40, 30), 0.7, 0.7),
            s.Ellipse(new Point(62, 18), 0.6, 0.6),
            s.Ellipse(new Point(54, 44), 0.5, 0.5),
            s.Ellipse(new Point(74, 34), 0.7, 0.7),
            s.Ellipse(new Point(34, 55), 0.5, 0.5),
            s.Ellipse(new Point(70, 56), 0.6, 0.6)));
        return group;
    }

    private static Drawing DesktopIcons()
    {
        Shapes s = Shapes.Identity;
        var group = new DrawingGroup();

        // A folder, right where the top-left bulbs hang, so the picture shows which of the two is in front.
        group.Children.Add(Fill(BulbPalette.Hex("#D99A2B"), s.Polygon(new Point(5, 6), new Point(12, 6), new Point(14.5, 8.5), new Point(25, 8.5), new Point(25, 21), new Point(5, 21))));
        group.Children.Add(Fill(BulbPalette.Hex("#F4C55C"), s.Rect(new Rect(5, 10.5, 20, 10.5), 1.2)));

        // A computer, crossed by the second bulb of the left edge.
        group.Children.Add(Fill(BulbPalette.Hex("#CFE8FF"), s.Rect(new Rect(5, 28, 20, 13), 1.4), s.Rect(new Rect(12, 41, 6, 2)), s.Rect(new Rect(9.5, 42.6, 11, 1.4), 0.6)));
        group.Children.Add(Fill(BulbPalette.Hex("#3F7FD6"), s.Rect(new Rect(6.8, 29.8, 16.4, 9.4))));

        // Their labels.
        group.Children.Add(Fill(
            Color.FromArgb(0xD8, 0xF5, 0xF8, 0xFF),
            s.Rect(new Rect(4, 23.5, 22, 1.8), 0.9),
            s.Rect(new Rect(8, 26, 14, 1.4), 0.7),
            s.Rect(new Rect(4, 46, 22, 1.8), 0.9)));
        return group;
    }

    private static Drawing AppWindow()
    {
        // A window across the top-right corner, under the top and right strings of bulbs.
        Shapes s = Shapes.Identity;
        var window = new Rect(84, 5, 74, 60);
        double right = window.Right;
        var group = new DrawingGroup();
        group.Children.Add(Fill(Color.FromArgb(0x55, 0, 0, 0), s.Rect(new Rect(window.X - 1, window.Y + 1.8, window.Width + 1, window.Height), 3)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(BulbPalette.Hex("#F7F9FC")), new Pen(new SolidColorBrush(BulbPalette.Hex("#8C98AA")), 0.6), Shapes.Join([s.Rect(window, 3)])));
        group.Children.Add(Fill(BulbPalette.Hex("#E3E9F1"), s.Rect(new Rect(window.X + 0.3, window.Y + 0.3, window.Width - 0.6, 7.5), 2.7), s.Rect(new Rect(window.X + 0.3, window.Y + 4, window.Width - 0.6, 3.8))));
        group.Children.Add(Fill(BulbPalette.Hex("#EDF1F7"), s.Rect(new Rect(window.X + 0.3, window.Y + 7.8, 16, window.Height - 8.1))));
        double captionY = window.Y + 4;
        group.Children.Add(Stroke(BulbPalette.Hex("#5B6577"), 0.7,
            s.Polyline(new Point(right - 21, captionY), new Point(right - 18, captionY)),
            s.Polyline(new Point(right - 14.6, captionY - 1.3), new Point(right - 12, captionY - 1.3), new Point(right - 12, captionY + 1.3), new Point(right - 14.6, captionY + 1.3), new Point(right - 14.6, captionY - 1.3)),
            s.Polyline(new Point(right - 8.4, captionY - 1.4), new Point(right - 5.6, captionY + 1.4)),
            s.Polyline(new Point(right - 5.6, captionY - 1.4), new Point(right - 8.4, captionY + 1.4))));
        group.Children.Add(Fill(
            BulbPalette.Hex("#C5CFDC"),
            s.Rect(new Rect(105, 19, 44, 2), 1),
            s.Rect(new Rect(105, 24, 34, 2), 1),
            s.Rect(new Rect(105, 29, 40, 2), 1),
            s.Rect(new Rect(88, 19, 10, 1.6), 0.8),
            s.Rect(new Rect(88, 23, 8, 1.6), 0.8),
            s.Rect(new Rect(88, 27, 9, 1.6), 0.8)));
        group.Children.Add(Fill(BulbPalette.Hex("#D6E3F3"), s.Rect(new Rect(105, 35, 46, 24), 1.5)));
        group.Children.Add(Fill(BulbPalette.Hex("#9EB9DD"), s.Polygon(new Point(105, 59), new Point(119, 44), new Point(128, 52), new Point(137, 46), new Point(151, 59))));
        return group;
    }

    private static Drawing Taskbar()
    {
        Shapes s = Shapes.Identity;
        var group = new DrawingGroup();
        group.Children.Add(Fill(Color.FromArgb(0xD9, 0x20, 0x20, 0x20), s.Rect(new Rect(0, 88, 160, 8))));
        group.Children.Add(Fill(
            Color.FromArgb(0xE6, 0xC9, 0xD1, 0xDD),
            s.Rect(new Rect(66, 90.2, 3.6, 3.6), 0.8),
            s.Rect(new Rect(72, 90.2, 3.6, 3.6), 0.8),
            s.Rect(new Rect(78, 90.2, 3.6, 3.6), 0.8),
            s.Rect(new Rect(84, 90.2, 3.6, 3.6), 0.8),
            s.Rect(new Rect(146, 90.8, 9, 1.2), 0.6),
            s.Rect(new Rect(147.5, 92.8, 7.5, 1.2), 0.6)));
        return group;
    }

    /// <summary>The string of lights around the picture's work area: top, right, bottom (above the taskbar) and left.</summary>
    private static Drawing BulbFrame()
    {
        var strings = new List<(Point Socket, double Degrees)>();
        double[] across = [.. Enumerable.Range(0, 11).Select(i => 12 + i * 13.6)];
        double[] down = [16, 30, 44, 58, 72];
        strings.AddRange(across.Select(x => (new Point(x, 2.6), 0.0)));
        strings.AddRange(down.Select(y => (new Point(157.4, y), 90.0)));
        strings.AddRange(across.Reverse().Select(x => (new Point(x, 85.4), 180.0)));
        strings.AddRange(down.Reverse().Select(y => (new Point(2.6, y), -90.0)));

        var group = new DrawingGroup();
        group.Children.Add(Stroke(WireGreen, 0.8,
            Wire(across.Select(x => new Point(x, 2.6)), new Vector(0, 1.3)),
            Wire(down.Select(y => new Point(157.4, y)), new Vector(-1.3, 0)),
            Wire(across.Select(x => new Point(x, 85.4)), new Vector(0, -1.3)),
            Wire(down.Select(y => new Point(2.6, y)), new Vector(1.3, 0))));

        var sockets = new List<PathFigure>();
        List<PathFigure>[] glass = [.. BulbColors.Select(_ => new List<PathFigure>())];
        var highlights = new List<PathFigure>();
        for (int i = 0; i < strings.Count; i++)
        {
            (Point socket, double degrees) = strings[i];
            Shapes b = Shapes.At(socket, degrees);
            sockets.Add(b.Rect(new Rect(-1.6, -0.2, 3.2, 3.0), 0.6));
            glass[i % BulbColors.Length].Add(b.Glass(2.8, 6.6, 5.2));
            highlights.Add(b.Ellipse(new Point(-1.0, 5.0), 0.45, 0.9));
        }

        for (int c = 0; c < BulbColors.Length; c++)
        {
            group.Children.Add(Fill(BulbColors[c], [.. glass[c]]));
        }

        group.Children.Add(Fill(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF), [.. highlights]));
        group.Children.Add(Fill(SocketGreen, [.. sockets]));
        return group;
    }

    private static PathFigure Wire(IEnumerable<Point> sockets, Vector sag)
    {
        Point[] points = [.. sockets];
        var figure = new PathFigure { StartPoint = points[0] - (points[1] - points[0]), IsFilled = false };
        Point previous = figure.StartPoint;
        foreach (Point point in points.Append(points[^1] + (points[^1] - points[^2])))
        {
            Vector step = point - previous;
            figure.Segments.Add(new BezierSegment(previous + step * 0.3 + sag, previous + step * 0.7 + sag, point, true));
            previous = point;
        }

        return figure;
    }

    private static Drawing TrayGlyphs(CornerColors c)
    {
        Shapes s = Shapes.Identity;
        var group = new DrawingGroup();

        // Wi-Fi: two arcs and a dot.
        group.Children.Add(Stroke(c.Glyph, 1.4,
            s.Curve(new Point(198, 101), new Point(201.5, 97.5), new Point(206.5, 97.5), new Point(210, 101)),
            s.Curve(new Point(200.8, 103.6), new Point(202.8, 101.6), new Point(205.2, 101.6), new Point(207.2, 103.6))));
        group.Children.Add(Fill(c.Glyph, s.Ellipse(new Point(204, 106), 1.3, 1.3)));

        // Speaker with one wave.
        group.Children.Add(Fill(c.Glyph, s.Polygon(new Point(217, 100), new Point(219.5, 100), new Point(223, 97), new Point(223, 108), new Point(219.5, 105), new Point(217, 105))));
        group.Children.Add(Stroke(c.Glyph, 1.3, s.Curve(new Point(225.5, 99.5), new Point(227.5, 101.5), new Point(227.5, 103.5), new Point(225.5, 105.5))));

        // Battery.
        group.Children.Add(Stroke(c.Glyph, 1.2, s.Polyline(new Point(234, 99), new Point(246, 99), new Point(246, 106), new Point(234, 106), new Point(234, 99))));
        group.Children.Add(Fill(c.Glyph, s.Rect(new Rect(246.6, 101, 1.4, 3)), s.Rect(new Rect(235.6, 100.6, 7.4, 3.8))));

        // The clock (time above the date), right-aligned.
        group.Children.Add(Fill(c.Glyph, s.Rect(new Rect(272, 95.5, 30, 3.2), 1.6)));
        group.Children.Add(Fill(c.GlyphSecondary, s.Rect(new Rect(264, 103.5, 38, 3.2), 1.6)));
        return group;
    }

    private static Drawing FlyoutIcons(CornerColors c)
    {
        Shapes s = Shapes.Identity;
        var group = new DrawingGroup();

        // Top row: a cloud, (the bulb), a shield.
        group.Children.Add(Fill(BulbPalette.Hex("#3B82F6"), s.Ellipse(new Point(153, 33.5), 4, 3.2), s.Ellipse(new Point(158.5, 31.5), 4.6, 4.2), s.Rect(new Rect(150, 32, 13, 5.2), 2.4)));
        group.Children.Add(Fill(BulbPalette.Hex("#22A06B"), s.Polygon(new Point(199, 26), new Point(205.5, 28.5), new Point(205, 34), new Point(199, 40), new Point(193, 34), new Point(192.5, 28.5))));
        group.Children.Add(Fill(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), s.Polygon(new Point(199, 28.5), new Point(203.4, 30.2), new Point(203, 33.8), new Point(199, 37.6))));

        // Bottom row: a speech bubble, a drop, a printer.
        group.Children.Add(Fill(BulbPalette.Hex("#8B5CF6"), s.Rect(new Rect(150, 50, 14, 10), 2.5), s.Polygon(new Point(153, 59), new Point(153, 63), new Point(157, 59))));
        group.Children.Add(Fill(BulbPalette.Hex("#14B8A6"), s.Ellipse(new Point(178, 59), 4.4, 4.4), s.Polygon(new Point(178, 49.5), new Point(181.9, 57), new Point(174.1, 57))));
        group.Children.Add(Fill(c.Printer, s.Rect(new Rect(193, 53, 13, 7), 1.5), s.Rect(new Rect(195.5, 49.5, 8, 4)), s.Rect(new Rect(195.5, 58, 8, 4.5))));
        group.Children.Add(Fill(c.Flyout, s.Rect(new Rect(196.5, 59, 6, 2.5))));
        return group;
    }

    /// <summary>The tray bulb (green socket, gold base, red glass) with its socket top at <paramref name="top"/>, about 17 units tall.</summary>
    private static Drawing TrayBulb(Point top)
    {
        Shapes b = Shapes.At(top, 0);
        var group = new DrawingGroup();
        Pen outline = new(new SolidColorBrush(BulbPalette.Hex("#6E0A10")), 0.8);
        PathFigure glass = b.Glass(5.2, 11.5, 9.2);
        group.Children.Add(new GeometryDrawing(
            new RadialGradientBrush(BulbPalette.Hex("#FF8A6E"), BulbPalette.Hex("#C4202A")) { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.42, 0.4), RadiusX = 0.75, RadiusY = 0.7 },
            outline,
            Shapes.Join([glass])));
        group.Children.Add(Fill(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF), b.Ellipse(new Point(-2.2, 9.0), 0.8, 1.5)));
        group.Children.Add(Fill(BulbPalette.Hex("#F4B942"), b.Rect(new Rect(-2.0, 4.0, 4.0, 1.6))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(SocketGreen), new Pen(new SolidColorBrush(BulbPalette.Hex("#0B3A1F")), 0.6), Shapes.Join([b.Rect(new Rect(-2.6, 0, 5.2, 4.4), 1)])));
        return group;
    }

    private static Drawing WarmLight(Point center, double radius) => new GeometryDrawing(
        new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x90, 0xFF, 0xC2, 0x4E), 0.0),
                new GradientStop(Color.FromArgb(0x40, 0xFF, 0x8A, 0x3A), 0.55),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0x6E, 0x34), 1.0),
            },
        },
        null,
        Shapes.Join([Shapes.Identity.Ellipse(center, radius, radius)]));

    private static Drawing Pointer(Point tip)
    {
        Shapes p = Shapes.At(tip, 0);
        PathFigure arrow = p.Polygon(
            new Point(0, 0), new Point(0, 14), new Point(3.4, 10.8), new Point(5.9, 16.2), new Point(8.2, 15.2), new Point(5.8, 10.1), new Point(10.3, 10.1));
        return new GeometryDrawing(Brushes.White, new Pen(Brushes.Black, 1) { LineJoin = PenLineJoin.Round }, Shapes.Join([arrow]));
    }

    private static GeometryDrawing Fill(Color color, params PathFigure[] figures) => new(new SolidColorBrush(color), null, Shapes.Join(figures));

    private static GeometryDrawing Stroke(Color color, double thickness, params PathFigure[] figures) =>
        new(null, new Pen(new SolidColorBrush(color), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Shapes.Join(figures));

    /// <summary>The colours of the taskbar-corner picture in one Windows mode.</summary>
    private sealed record CornerColors(
        Color DesktopTop,
        Color DesktopBottom,
        Color Taskbar,
        Color TaskbarEdge,
        Color ButtonPressed,
        Color Glyph,
        Color GlyphSecondary,
        Color Flyout,
        Color FlyoutBorder,
        Color Shadow,
        Color HighlightFill,
        Color Accent,
        Color Printer)
    {
        public static CornerColors Light { get; } = new(
            BulbPalette.Hex("#DCE8F5"), BulbPalette.Hex("#BFD3EC"), BulbPalette.Hex("#F3F3F3"), BulbPalette.Hex("#DADADA"),
            BulbPalette.Hex("#E2E2E2"), BulbPalette.Hex("#1B1B1B"), BulbPalette.Hex("#5C5C5C"), BulbPalette.Hex("#FBFBFB"),
            BulbPalette.Hex("#D5D5D5"), Color.FromArgb(0x24, 0, 0, 0), BulbPalette.Hex("#E3EEF9"), BulbPalette.Hex("#0067C0"),
            BulbPalette.Hex("#6B7280"));

        public static CornerColors Dark { get; } = new(
            BulbPalette.Hex("#1B2A44"), BulbPalette.Hex("#0E1729"), BulbPalette.Hex("#1C1C1C"), BulbPalette.Hex("#333333"),
            BulbPalette.Hex("#2E2E2E"), BulbPalette.Hex("#F3F3F3"), BulbPalette.Hex("#BDBDBD"), BulbPalette.Hex("#2B2B2B"),
            BulbPalette.Hex("#404040"), Color.FromArgb(0x59, 0, 0, 0), BulbPalette.Hex("#2A3B50"), BulbPalette.Hex("#4CC2FF"),
            BulbPalette.Hex("#A3AAB5"));
    }
}
