using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>The <c>.bul</c> document icon: a Windows 11 style page with a folded corner and the lit bulb on it.</summary>
internal static class DocumentArt
{
    private static readonly Color PageTop = BulbPalette.Hex("#FFFFFF");
    private static readonly Color PageBottom = BulbPalette.Hex("#EEF1F5");
    private static readonly Color PageBorder = BulbPalette.Hex("#8E99A6");
    private static readonly Color FoldFill = BulbPalette.Hex("#D9DFE7");

    /// <summary>Creates the icon drawing for one size.</summary>
    /// <param name="size">Pixel size.</param>
    /// <returns>The drawing.</returns>
    public static Drawing Create(int size)
    {
        double left = Math.Round(size * 0.14);
        double right = size - left;
        double top = size >= 32 ? Math.Round(size * 0.03) : 0;
        double bottom = size - top;
        double fold = Math.Round(size * 0.27);
        double stroke = size >= 128 ? Math.Round(size / 128.0) : 1;
        double inset = stroke / 2;
        double radius = size >= 32 ? size * 0.035 : 0;

        var group = new DrawingGroup();
        group.Children.Add(IconSet.Canvas(size));
        Geometry page = PageGeometry(left + inset, right - inset, top + inset, bottom - inset, fold, radius);
        if (size >= 48)
        {
            group.Children.Add(Shadow(page, size));
        }

        var pageBrush = new LinearGradientBrush(PageTop, PageBottom, 90);
        group.Children.Add(new GeometryDrawing(pageBrush, new Pen(new SolidColorBrush(PageBorder), stroke) { LineJoin = PenLineJoin.Round }, page));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(FoldFill), new Pen(new SolidColorBrush(PageBorder), stroke) { LineJoin = PenLineJoin.Round }, FoldGeometry(right - inset, top + inset, fold)));

        double pageHeight = bottom - top;
        double bulbTop = top + pageHeight * (size >= 32 ? 0.24 : 0.19);
        double bulbBottom = bottom - pageHeight * (size >= 32 ? 0.10 : 0.07) - stroke;
        var bulbBox = new Rect(left, bulbTop, right - left, bulbBottom - bulbTop);
        BulbMetrics bulb = BulbMetrics.Master.Fit(bulbBox);
        if (size >= 48)
        {
            group.Children.Add(SoftGlow(bulb));
        }

        group.Children.Add(BulbArt.Create(bulb, BulbPalette.LitOnLight, cord: false));
        return group;
    }

    private static Geometry PageGeometry(double left, double right, double top, double bottom, double fold, double radius)
    {
        var figure = new PathFigure { StartPoint = new Point(left + radius, top), IsClosed = true };
        figure.Segments.Add(new LineSegment(new Point(right - fold, top), true));
        figure.Segments.Add(new LineSegment(new Point(right, top + fold), true));
        figure.Segments.Add(new LineSegment(new Point(right, bottom - radius), true));
        if (radius > 0)
        {
            figure.Segments.Add(new ArcSegment(new Point(right - radius, bottom), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        }

        figure.Segments.Add(new LineSegment(new Point(left + radius, bottom), true));
        if (radius > 0)
        {
            figure.Segments.Add(new ArcSegment(new Point(left, bottom - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        }

        figure.Segments.Add(new LineSegment(new Point(left, top + radius), true));
        if (radius > 0)
        {
            figure.Segments.Add(new ArcSegment(new Point(left + radius, top), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        }

        return new PathGeometry { Figures = { figure } };
    }

    private static Geometry FoldGeometry(double right, double top, double fold)
    {
        var figure = new PathFigure { StartPoint = new Point(right - fold, top), IsClosed = true };
        figure.Segments.Add(new LineSegment(new Point(right - fold, top + fold), true));
        figure.Segments.Add(new LineSegment(new Point(right, top + fold), true));
        return new PathGeometry { Figures = { figure } };
    }

    private static Drawing Shadow(Geometry page, int size)
    {
        // The page outline itself, nudged down and right: a soft contact shadow that respects the folded corner.
        double offset = size / 96.0;
        Geometry shadow = page.Clone();
        shadow.Transform = new TranslateTransform(offset, offset * 2);
        return new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x26, 0x10, 0x18, 0x28)), null, shadow);
    }

    private static Drawing SoftGlow(BulbMetrics bulb)
    {
        Point center = BulbArt.GlassCenter(bulb);
        double radius = bulb.ShoulderHalf * 1.9;
        var brush = new RadialGradientBrush
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = radius,
            RadiusY = radius,
            MappingMode = BrushMappingMode.Absolute,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x70, 0xFF, 0xB8, 0x5C), 0.0),
                new GradientStop(Color.FromArgb(0x30, 0xFF, 0x86, 0x4A), 0.55),
                new GradientStop(Color.FromArgb(0x00, 0xE3, 0x34, 0x2F), 1.0),
            },
        };
        return new GeometryDrawing(brush, null, new EllipseGeometry(center, radius, radius));
    }
}
