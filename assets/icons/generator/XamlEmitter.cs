using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>
/// Writes WPF drawings as readable XAML (<c>DrawingImage</c> resources). Supports exactly what the illustrations use:
/// drawing groups with a clip, geometry drawings with solid or gradient brushes and pens, and path geometry with line and
/// cubic Bezier segments. Coordinates are rounded to 0.001 DIP.
/// </summary>
internal sealed class XamlEmitter
{
    private readonly StringBuilder text = new();

    /// <summary>Appends a line at an indentation level.</summary>
    /// <param name="indent">Indentation level (4 spaces each).</param>
    /// <param name="line">The text.</param>
    public void Line(int indent, string line) => text.Append(' ', indent * 4).Append(line).Append("\r\n");

    /// <summary>Appends a <c>DrawingImage</c> resource with a comment above it.</summary>
    /// <param name="indent">Indentation level.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="comment">What the picture shows.</param>
    /// <param name="drawing">The drawing.</param>
    public void DrawingImage(int indent, string key, string comment, Drawing drawing)
    {
        Line(indent, $"<!-- {comment} -->");
        Line(indent, $"<DrawingImage x:Key=\"{key}\">");
        Line(indent + 1, "<DrawingImage.Drawing>");
        Drawing(indent + 2, drawing);
        Line(indent + 1, "</DrawingImage.Drawing>");
        Line(indent, "</DrawingImage>");
    }

    /// <inheritdoc/>
    public override string ToString() => text.ToString();

    /// <summary>Formats path geometry as path markup (non-zero fill rule).</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>Markup such as <c>F1 M0,0 L10,0 Z</c>.</returns>
    public static string PathData(Geometry geometry)
    {
        PathGeometry path = geometry as PathGeometry ?? throw new NotSupportedException("Illustrations use path geometry only.");
        var data = new StringBuilder(path.FillRule == FillRule.Nonzero ? "F1" : "F0");
        foreach (PathFigure figure in path.Figures)
        {
            data.Append(" M").Append(P(figure.StartPoint));
            foreach (PathSegment segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        data.Append(" L").Append(P(line.Point));
                        break;
                    case BezierSegment bezier:
                        data.Append(" C").Append(P(bezier.Point1)).Append(' ').Append(P(bezier.Point2)).Append(' ').Append(P(bezier.Point3));
                        break;
                    default:
                        throw new NotSupportedException($"Segment {segment.GetType().Name} is not supported.");
                }
            }

            if (figure.IsClosed)
            {
                data.Append(" Z");
            }
        }

        return data.ToString();
    }

    private void Drawing(int indent, Drawing drawing)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                string clip = group.ClipGeometry is null ? "" : $" ClipGeometry=\"{PathData(group.ClipGeometry)}\"";
                Line(indent, $"<DrawingGroup{clip}>");
                foreach (Drawing child in group.Children)
                {
                    Drawing(indent + 1, child);
                }

                Line(indent, "</DrawingGroup>");
                break;
            case GeometryDrawing geometryDrawing:
                GeometryDrawingXaml(indent, geometryDrawing);
                break;
            default:
                throw new NotSupportedException($"Drawing {drawing.GetType().Name} is not supported.");
        }
    }

    private void GeometryDrawingXaml(int indent, GeometryDrawing drawing)
    {
        string geometry = $"Geometry=\"{PathData(drawing.Geometry)}\"";
        string brush = drawing.Brush is SolidColorBrush solid ? $" Brush=\"{C(solid.Color)}\"" : "";
        bool complexBrush = drawing.Brush is not null and not SolidColorBrush;
        if (!complexBrush && drawing.Pen is null)
        {
            Line(indent, $"<GeometryDrawing{brush} {geometry} />");
            return;
        }

        Line(indent, $"<GeometryDrawing{brush} {geometry}>");
        if (complexBrush)
        {
            Line(indent + 1, "<GeometryDrawing.Brush>");
            BrushXaml(indent + 2, drawing.Brush!);
            Line(indent + 1, "</GeometryDrawing.Brush>");
        }

        if (drawing.Pen is Pen pen)
        {
            Line(indent + 1, "<GeometryDrawing.Pen>");
            Line(indent + 2, PenXaml(pen));
            Line(indent + 1, "</GeometryDrawing.Pen>");
        }

        Line(indent, "</GeometryDrawing>");
    }

    private void BrushXaml(int indent, Brush brush)
    {
        switch (brush)
        {
            case LinearGradientBrush linear:
                Line(indent, $"<LinearGradientBrush StartPoint=\"{P(linear.StartPoint)}\" EndPoint=\"{P(linear.EndPoint)}\"{Mapping(linear)}>");
                Stops(indent + 1, linear.GradientStops);
                Line(indent, "</LinearGradientBrush>");
                break;
            case RadialGradientBrush radial:
                Line(indent, $"<RadialGradientBrush Center=\"{P(radial.Center)}\" GradientOrigin=\"{P(radial.GradientOrigin)}\" RadiusX=\"{N(radial.RadiusX)}\" RadiusY=\"{N(radial.RadiusY)}\"{Mapping(radial)}>");
                Stops(indent + 1, radial.GradientStops);
                Line(indent, "</RadialGradientBrush>");
                break;
            default:
                throw new NotSupportedException($"Brush {brush.GetType().Name} is not supported.");
        }
    }

    private void Stops(int indent, GradientStopCollection stops)
    {
        foreach (GradientStop stop in stops)
        {
            Line(indent, $"<GradientStop Color=\"{C(stop.Color)}\" Offset=\"{N(stop.Offset)}\" />");
        }
    }

    private static string Mapping(GradientBrush brush) =>
        brush.MappingMode == BrushMappingMode.Absolute ? " MappingMode=\"Absolute\"" : "";

    private static string PenXaml(Pen pen)
    {
        string color = pen.Brush is SolidColorBrush solid ? C(solid.Color) : throw new NotSupportedException("Pens use solid colours.");
        var xaml = new StringBuilder($"<Pen Brush=\"{color}\" Thickness=\"{N(pen.Thickness)}\"");
        if (pen.StartLineCap != PenLineCap.Flat)
        {
            xaml.Append($" StartLineCap=\"{pen.StartLineCap}\"");
        }

        if (pen.EndLineCap != PenLineCap.Flat)
        {
            xaml.Append($" EndLineCap=\"{pen.EndLineCap}\"");
        }

        if (pen.LineJoin != PenLineJoin.Miter)
        {
            xaml.Append($" LineJoin=\"{pen.LineJoin}\"");
        }

        return xaml.Append(" />").ToString();
    }

    private static string C(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string P(Point point) => $"{N(point.X)},{N(point.Y)}";

    private static string N(double value)
    {
        double rounded = Math.Round(value, 3);
        return (rounded == 0 ? 0 : rounded).ToString("0.###", CultureInfo.InvariantCulture);
    }
}
