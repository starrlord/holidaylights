using System.IO;
using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>
/// Writes the pictures of the Help topics into <c>src/HolidayLights.App/HelpContent/images</c>. They are drawn at twice
/// their display size and saved at 192 DPI, so WPF shows them at their intended size and sharp at 150 % and 200 %.
/// Pictures of the interface follow PRODUCT-SPEC exactly (labels, order, glyph names); bulb pictures use the 2003 art.
/// </summary>
internal static class HelpImageGenerator
{
    /// <summary>Pixels per DIP of every help picture.</summary>
    public const int Scale = 2;

    /// <summary>Height of a menu item and of a separator in the tray menu picture (Windows 11 menu metrics).</summary>
    private const double MenuItemHeight = 30, MenuSeparatorHeight = 9;

    private static readonly Color Ink = BulbPalette.Hex("#1A1A1A");
    private static readonly Color InkSecondary = BulbPalette.Hex("#5F5F5F");
    private static readonly Color InkDisabled = BulbPalette.Hex("#A0A0A0");
    private static readonly Color CornerGreen = BulbPalette.Hex("#1F8B4C");
    private static readonly Color EdgeBlue = BulbPalette.Hex("#2F6FDB");

    /// <summary>Writes every picture.</summary>
    /// <param name="paths">Repository locations.</param>
    public static void Run(RepoPaths paths)
    {
        string folder = Path.Combine(paths.HelpContent, "images");
        ResourceDictionary illustrations = IllustrationGenerator.Load(Path.Combine(paths.AppAssets, "Illustrations.xaml"));
        var art = new BulbCells(Path.Combine(paths.Root, "assets", "icons", "generator", "cells"));
        Save(TaskbarCorner(illustrations), folder, "taskbar-corner.png");
        Save(LayerModes(illustrations), folder, "layer-modes.png");
        Save(TrayMenu(art), folder, "tray-menu.png");
        Save(BulbFactory(art), folder, "bulb-factory.png");
        Save(LookComparison(art), folder, "look-comparison.png");
        Console.WriteLine($"Help pictures written to {folder}.");
    }

    private static void Save((Drawing Drawing, Size Size) picture, string folder, string file) =>
        Raster.SavePng(
            Raster.RenderImage(picture.Drawing, (int)(picture.Size.Width * Scale), (int)(picture.Size.Height * Scale), Scale),
            Path.Combine(folder, file),
            96 * Scale);

    private static (Drawing, Size) TaskbarCorner(ResourceDictionary illustrations) =>
        (((DrawingImage)illustrations["HL.Illustration.TaskbarCorner.Light"]).Drawing, IllustrationArt.CornerSize);

    private static (Drawing, Size) LayerModes(ResourceDictionary illustrations)
    {
        string[] keys = ["HL.Illustration.BehindIcons", "HL.Illustration.InFrontOfIcons", "HL.Illustration.OnTop"];
        const double gap = 16;
        Size one = IllustrationArt.LayerSize;
        var group = new DrawingGroup();
        for (int i = 0; i < keys.Length; i++)
        {
            var image = (DrawingImage)illustrations[keys[i]];
            group.Children.Add(new ImageDrawing(image, new Rect(i * (one.Width + gap), 0, one.Width, one.Height)));
        }

        return (group, new Size(3 * one.Width + 2 * gap, one.Height));
    }

    /// <summary>The tray menu of PRODUCT-SPEC 2.2 (light mode), Christmas 1 chosen by hand, the pointer on Settings.</summary>
    private static (Drawing, Size) TrayMenu(BulbCells art)
    {
        const double width = 380, margin = 10, header = 48;
        var group = new DrawingGroup();
        double height = 4 + header + (5 * MenuSeparatorHeight) + (10 * MenuItemHeight) + 4;
        var menu = new Rect(margin, margin - 2, width, height);
        Shadow(group, menu, 8, 0x18);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(BulbPalette.Hex("#F9F9F9")), new Pen(new SolidColorBrush(BulbPalette.Hex("#DCDCDC")), 1), Shapes.Join([Shapes.Identity.Rect(menu, 8)])));

        // Header: the top edge of Christmas 1 (Jolly Holly corners, Standard Bulbs) on a night well, then the theme.
        double y = menu.Top + 4;
        var well = new Rect(menu.Left + 12, y + 8, 132, 30);
        group.Children.Add(NightWell(well, 4));
        string[] cells = ["jolly-holly/c04", "standard-bulbs/c01", "standard-bulbs/c02", "standard-bulbs/c03", "standard-bulbs/c04", "jolly-holly/c08"];
        for (int i = 0; i < cells.Length; i++)
        {
            group.Children.Add(art.Drawing(cells[i], new Rect(well.Left + 6 + i * 20, well.Top + 5, 20, 20), Scale));
        }

        group.Children.Add(TextArt.Line("Christmas 1", new Point(well.Right + 14, y + 22), 14, Ink, FontWeights.SemiBold));
        group.Children.Add(TextArt.Line("Chosen by you", new Point(well.Right + 14, y + 39), 12, InkSecondary, FontWeights.Normal));
        y += header;

        y = Separator(group, menu, y);
        y = MenuItem(group, menu, y, "Show Lights", Glyph.Check);
        y = Separator(group, menu, y);
        y = MenuItem(group, menu, y, "Bulbs On Desktop", Glyph.Radio);
        y = MenuItem(group, menu, y, "Bulbs On Top of All Windows", Glyph.None, shortcut: "Ctrl+Alt+Shift+B");
        y = Separator(group, menu, y);
        y = MenuItem(group, menu, y, "Themes", Glyph.None, submenu: true);
        y = MenuItem(group, menu, y, "Play Holiday Music", Glyph.None);
        y = MenuItem(group, menu, y, "Next Song", Glyph.None, disabled: true);
        y = Separator(group, menu, y);
        double settingsTop = y;
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(BulbPalette.Hex("#EAEAEA")), null, Shapes.Join([Shapes.Identity.Rect(new Rect(menu.Left + 4, y + 1, menu.Width - 8, MenuItemHeight - 2), 4)])));
        y = MenuItem(group, menu, y, "Holiday Lights Settings…", Glyph.None, bold: true);
        y = MenuItem(group, menu, y, "Holiday Lights Help", Glyph.None);
        y = MenuItem(group, menu, y, "About Holiday Lights", Glyph.None);
        y = Separator(group, menu, y);
        MenuItem(group, menu, y, "Exit Holiday Lights", Glyph.None);
        group.Children.Add(Pointer(new Point(menu.Left + 236, settingsTop + 17)));
        return (group, new Size(width + 2 * margin, height + 2 * margin));

        static double Separator(DrawingGroup g, Rect menu, double top)
        {
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(BulbPalette.Hex("#E3E3E3")), null, Shapes.Join([Shapes.Identity.Rect(new Rect(menu.Left + 1, top + 4, menu.Width - 2, 1))])));
            return top + MenuSeparatorHeight;
        }
    }

    private enum Glyph
    {
        None,
        Check,
        Radio,
    }

    private static double MenuItem(DrawingGroup group, Rect menu, double top, string label, Glyph glyph, string? shortcut = null, bool submenu = false, bool disabled = false, bool bold = false)
    {
        double baseline = top + 20;
        Color ink = disabled ? InkDisabled : Ink;
        Shapes s = Shapes.Identity;
        switch (glyph)
        {
            case Glyph.Check:
                group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(ink), 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
                    Shapes.Join([s.Polyline(new Point(menu.Left + 15, top + 15.5), new Point(menu.Left + 18.5, top + 19), new Point(menu.Left + 25, top + 12))])));
                break;
            case Glyph.Radio:
                group.Children.Add(new GeometryDrawing(new SolidColorBrush(ink), null, Shapes.Join([s.Ellipse(new Point(menu.Left + 20, top + 15), 3.2, 3.2)])));
                break;
        }

        group.Children.Add(TextArt.Line(label, new Point(menu.Left + 40, baseline), 14, ink, bold ? FontWeights.SemiBold : FontWeights.Normal));
        if (shortcut is not null)
        {
            group.Children.Add(TextArt.Line(shortcut, new Point(menu.Right - 16, baseline), 12, InkSecondary, FontWeights.Normal, alignRight: true));
        }

        if (submenu)
        {
            group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(ink), 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
                Shapes.Join([s.Polyline(new Point(menu.Right - 20, top + 10.5), new Point(menu.Right - 15.5, top + 15), new Point(menu.Right - 20, top + 19.5))])));
        }

        return top + MenuItemHeight;
    }

    /// <summary>Bulb Factory: the eight boxes around the preview of the screen, and the Bulb List beside them.</summary>
    private static (Drawing, Size) BulbFactory(BulbCells art)
    {
        var size = new Size(560, 312);
        var group = new DrawingGroup();
        Shapes s = Shapes.Identity;
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(BulbPalette.Hex("#F3F3F3")), new Pen(new SolidColorBrush(BulbPalette.Hex("#D6D6D6")), 1), Shapes.Join([s.Rect(new Rect(0.5, 0.5, size.Width - 1, size.Height - 1), 8)])));

        // Corners in green and edges in blue, as on the 2003 help picture; each box shows the bulb's art for its place.
        (Rect Box, string Cell)[] corners =
        [
            (new(14, 14, 56, 56), "jolly-holly/c04"), (new(298, 14, 56, 56), "jolly-holly/c08"),
            (new(14, 214, 56, 56), "jolly-holly/c14"), (new(298, 214, 56, 56), "jolly-holly/c10"),
        ];
        foreach ((Rect corner, string cell) in corners)
        {
            Box(group, corner, CornerGreen);
            group.Children.Add(NightWell(new Rect(corner.Left + 8, corner.Top + 8, 40, 40), 4));
            group.Children.Add(art.Drawing(cell, new Rect(corner.Left + 8, corner.Top + 8, 40, 40), Scale));
        }

        var top = new Rect(78, 14, 212, 56);
        var bottom = new Rect(78, 214, 212, 56);
        var left = new Rect(14, 78, 56, 128);
        var right = new Rect(298, 78, 56, 128);
        Box(group, top, EdgeBlue);
        Box(group, bottom, EdgeBlue);
        Box(group, left, EdgeBlue);
        Box(group, right, EdgeBlue);
        Chips(group, art, top, 40, horizontal: true, "standard-bulbs/c01", "snow-family/c01");
        Chips(group, art, bottom, 40, horizontal: true, "snow-family/c01", "jolly-holly/c09");
        Chips(group, art, left, 34, horizontal: false, "standard-bulbs/c21", "snow-family/c01");
        Chips(group, art, right, 34, horizontal: false, "standard-bulbs/c31", "snow-family/c01");

        // The preview of the screen in the middle.
        var preview = new Rect(78, 78, 212, 128);
        group.Children.Add(new GeometryDrawing(new LinearGradientBrush(BulbPalette.Hex("#0B1530"), BulbPalette.Hex("#1D3466"), 90), new Pen(new SolidColorBrush(BulbPalette.Hex("#3A3A3A")), 1), Shapes.Join([s.Rect(preview, 6)])));
        MiniFrame(group, new Rect(preview.Left + 4, preview.Top + 4, preview.Width - 8, preview.Height - 16));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0xD9, 0x20, 0x20, 0x20)), null, Shapes.Join([s.Rect(new Rect(preview.Left + 0.5, preview.Bottom - 9, preview.Width - 1, 8.5), 0)])));

        // The Bulb List.
        var list = new Rect(370, 14, 176, 256);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Colors.White), new Pen(new SolidColorBrush(BulbPalette.Hex("#D6D6D6")), 1), Shapes.Join([s.Rect(list, 6)])));
        group.Children.Add(TextArt.Line("Bulb List", new Point(list.Left + 10, list.Top + 20), 12, Ink, FontWeights.SemiBold));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(BulbPalette.Hex("#F6F6F6")), new Pen(new SolidColorBrush(BulbPalette.Hex("#C9C9C9")), 1), Shapes.Join([s.Rect(new Rect(list.Left + 10, list.Top + 28, list.Width - 20, 20), 4)])));
        group.Children.Add(TextArt.Line("Search 1,550 bulbs", new Point(list.Left + 18, list.Top + 42), 10, InkSecondary, FontWeights.Normal));
        (string Cell, string Name)[] tiles =
        [
            ("standard-bulbs/c01", "Standard Bulbs"), ("jolly-holly/c01", "Jolly Holly"),
            ("snow-family/c01", "Snow Family"), ("candy-canes/c01", "Candy Canes"),
            ("shiny-baubles/c01", "Shiny Baubles"), ("gifts/c01", "Gifts"),
        ];
        for (int i = 0; i < tiles.Length; i++)
        {
            double x = list.Left + 10 + (i % 2) * 80;
            double y = list.Top + 58 + (i / 2) * 64;
            var tile = new Rect(x, y, 76, 60);
            if (i == 0)
            {
                group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(BulbPalette.Hex("#0067C0")), 2), Shapes.Join([s.Rect(new Rect(tile.Left - 1, tile.Top - 1, tile.Width + 2, tile.Height + 2), 5)])));
            }

            group.Children.Add(NightWell(new Rect(tile.Left + 2, tile.Top + 2, tile.Width - 4, 40), 4));
            group.Children.Add(art.Drawing(tiles[i].Cell, new Rect(tile.Left + 20, tile.Top + 6, 36, 36), Scale));
            group.Children.Add(TextArt.Line(tiles[i].Name, new Point(tile.Left + tile.Width / 2 - TextArt.Format(tiles[i].Name, 10, Ink, FontWeights.Normal).Width / 2, tile.Bottom - 4), 10, Ink, FontWeights.Normal));
        }

        // Legend.
        double legendY = 296;
        group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(CornerGreen), 2), Shapes.Join([s.Rect(new Rect(16, legendY - 10, 12, 12), 2)])));
        group.Children.Add(TextArt.Line("Corners", new Point(34, legendY), 12, Ink, FontWeights.Normal));
        group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(EdgeBlue), 2), Shapes.Join([s.Rect(new Rect(96, legendY - 10, 12, 12), 2)])));
        group.Children.Add(TextArt.Line("Edges", new Point(114, legendY), 12, Ink, FontWeights.Normal));
        group.Children.Add(TextArt.Line("Double-click a bulb to use it, or drag it into a box.", new Point(172, legendY), 12, InkSecondary, FontWeights.Normal));
        return (group, size);
    }

    private static void Box(DrawingGroup group, Rect box, Color outline) =>
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Colors.White), new Pen(new SolidColorBrush(outline), 2), Shapes.Join([Shapes.Identity.Rect(box, 6)])));

    private static void Chips(DrawingGroup group, BulbCells art, Rect box, double chip, bool horizontal, params string[] cells)
    {
        const double gap = 6;
        double inset = (Math.Min(box.Width, box.Height) - chip) / 2;
        for (int i = 0; i <= cells.Length; i++)
        {
            Rect slot = horizontal
                ? new Rect(box.Left + inset + i * (chip + gap), box.Top + inset, chip, chip)
                : new Rect(box.Left + inset, box.Top + inset + i * (chip + gap), chip, chip);
            if (i < cells.Length)
            {
                group.Children.Add(NightWell(slot, 4));
                group.Children.Add(art.Drawing(cells[i], slot, Scale));
                continue;
            }

            // The dashed "+" placeholder that follows the last chip.
            var dashed = new Pen(new SolidColorBrush(BulbPalette.Hex("#8A8A8A")), 1) { DashStyle = new DashStyle([3, 2], 0) };
            group.Children.Add(new GeometryDrawing(null, dashed, Shapes.Join([Shapes.Identity.Rect(slot, 4)])));
            Point c = new(slot.Left + slot.Width / 2, slot.Top + slot.Height / 2);
            double arm = chip * 0.15;
            group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(BulbPalette.Hex("#6A6A6A")), 1.4),
                Shapes.Join([Shapes.Identity.Polyline(new Point(c.X - arm, c.Y), new Point(c.X + arm, c.Y)), Shapes.Identity.Polyline(new Point(c.X, c.Y - arm), new Point(c.X, c.Y + arm))])));
        }
    }

    /// <summary>Tiny lights along the edges of the preview.</summary>
    private static void MiniFrame(DrawingGroup group, Rect area)
    {
        Color[] colors = [BulbPalette.Hex("#FF4B3E"), BulbPalette.Hex("#3ACB6E"), BulbPalette.Hex("#5AA0FF"), BulbPalette.Hex("#F4C542")];
        var figures = colors.Select(_ => new List<PathFigure>()).ToArray();
        int n = 0;
        void Dot(double x, double y) => figures[n++ % colors.Length].Add(Shapes.Identity.Ellipse(new Point(x, y), 2.1, 2.6));
        for (double x = area.Left + 4; x <= area.Right - 4; x += 9)
        {
            Dot(x, area.Top + 3);
        }

        for (double y = area.Top + 12; y <= area.Bottom - 8; y += 9)
        {
            Dot(area.Right - 3, y);
        }

        for (double x = area.Right - 4; x >= area.Left + 4; x -= 9)
        {
            Dot(x, area.Bottom - 3);
        }

        for (double y = area.Bottom - 12; y >= area.Top + 8; y -= 9)
        {
            Dot(area.Left + 3, y);
        }

        for (int c = 0; c < colors.Length; c++)
        {
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(colors[c]), null, Shapes.Join(figures[c])));
        }
    }

    /// <summary>Classic 2003 (crisp, no glow), Modern Glow (smooth, soft glow) and Bright Glow, side by side.</summary>
    private static (Drawing, Size) LookComparison(BulbCells art)
    {
        const double panelWidth = 196, panelHeight = 84, gap = 14, artScale = 1.25;
        var group = new DrawingGroup();
        (bool Smooth, double Glow)[] looks = [(false, 0), (true, 0.55), (true, 1.0)];
        for (int i = 0; i < looks.Length; i++)
        {
            var panel = new Rect(i * (panelWidth + gap), 0, panelWidth, panelHeight);
            PixelImage picture = LookPanel(art, (int)(panelWidth * Scale), (int)(panelHeight * Scale), artScale * Scale, looks[i].Smooth, looks[i].Glow);
            var clip = Shapes.Join([Shapes.Identity.Rect(panel, 6)]);
            group.Children.Add(new DrawingGroup { ClipGeometry = clip, Children = { new ImageDrawing(picture.ToBitmapSource(96 * Scale), panel) } });
        }

        return (group, new Size(3 * panelWidth + 2 * gap, panelHeight));
    }

    private static PixelImage LookPanel(BulbCells art, int width, int height, double scale, bool smooth, double glow)
    {
        // Night sky, as on the light stage when no wallpaper is available (PRODUCT-SPEC 4.2).
        var sky = new DrawingGroup { Children = { new GeometryDrawing(new LinearGradientBrush(BulbPalette.Hex("#0B1530"), BulbPalette.Hex("#1D3466"), 90), null, new RectangleGeometry(new Rect(0, 0, width, height))) } };
        PixelImage image = Raster.RenderImage(sky, width, height);
        string[] lit = ["jolly-holly/c01", "standard-bulbs/c01", "standard-bulbs/c02", "standard-bulbs/c03", "standard-bulbs/c04"];
        string?[] unlit = [null, "standard-bulbs/c06", "standard-bulbs/c07", "standard-bulbs/c08", "standard-bulbs/c09"];
        int cell = (int)Math.Round(32 * scale);
        int left = (width - cell * lit.Length) / 2;
        for (int i = 0; i < lit.Length; i++)
        {
            PixelImage source = art.Load(lit[i]);
            if (glow > 0 && unlit[i] is string off)
            {
                (float[] light, int w, int h, int margin) = PixelArt.Glow(source, art.Load(off), scale, glow);
                PixelArt.AddLight(image, light, w, h, left + i * cell - margin, -margin);
            }
        }

        for (int i = 0; i < lit.Length; i++)
        {
            PixelImage source = art.Load(lit[i]);
            PixelImage sprite = smooth ? PixelArt.Smooth(source, scale) : PixelArt.Crisp(source, scale);
            Raster.Over(image, sprite, left + i * cell, 0);
        }

        return image;
    }

    private static Drawing NightWell(Rect rect, double radius) =>
        new GeometryDrawing(new LinearGradientBrush(BulbPalette.Hex("#14203A"), BulbPalette.Hex("#1F3157"), 90), null, Shapes.Join([Shapes.Identity.Rect(rect, radius)]));

    private static void Shadow(DrawingGroup group, Rect rect, double radius, byte alpha)
    {
        for (int layer = 1; layer <= 3; layer++)
        {
            var spread = new Rect(rect.X - layer, rect.Y + layer, rect.Width + 2 * layer, rect.Height + 2 * layer);
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb((byte)(alpha / layer), 0, 0, 0)), null, Shapes.Join([Shapes.Identity.Rect(spread, radius + layer)])));
        }
    }

    private static Drawing Pointer(Point tip)
    {
        Shapes p = Shapes.At(tip, 0);
        PathFigure arrow = p.Polygon(new Point(0, 0), new Point(0, 15), new Point(3.6, 11.5), new Point(6.3, 17.3), new Point(8.8, 16.2), new Point(6.2, 10.8), new Point(11, 10.8));
        return new GeometryDrawing(Brushes.White, new Pen(Brushes.Black, 1) { LineJoin = PenLineJoin.Round }, Shapes.Join([arrow]));
    }

    /// <summary>Cells of the built-in bulbs (<c>cells/&lt;slug&gt;/cNN.png</c>, cut from the 5.4 sheets), smoothed for pictures.</summary>
    private sealed class BulbCells(string root)
    {
        private readonly Dictionary<string, PixelImage> cache = [];

        /// <summary>Loads a cell, e.g. <c>standard-bulbs/c01</c>.</summary>
        public PixelImage Load(string cell)
        {
            if (!cache.TryGetValue(cell, out PixelImage? image))
            {
                string[] parts = cell.Split('/');
                image = Raster.Load(Path.Combine(root, parts[0], "cells", parts[1] + ".png"));
                cache[cell] = image;
            }

            return image;
        }

        /// <summary>A cell drawn into a rectangle with the "Smooth" rule at the picture's pixel scale.</summary>
        public Drawing Drawing(string cell, Rect bounds, int pixelsPerDip)
        {
            PixelImage source = Load(cell);
            double scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height) * pixelsPerDip;
            PixelImage sprite = PixelArt.Smooth(source, scale);
            double w = sprite.Width / (double)pixelsPerDip;
            double h = sprite.Height / (double)pixelsPerDip;
            var placed = new Rect(bounds.Left + (bounds.Width - w) / 2, bounds.Top + (bounds.Height - h) / 2, w, h);
            return new ImageDrawing(sprite.ToBitmapSource(96 * pixelsPerDip), placed);
        }
    }
}
