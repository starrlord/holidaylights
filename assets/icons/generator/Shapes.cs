using System.Windows;
using System.Windows.Media;

namespace HolidayLights.Branding;

/// <summary>
/// Builds path figures with a transform baked into their points, so geometry written to XAML is plain path data
/// (no nested transforms) and many shapes of one colour can share a single <see cref="GeometryDrawing"/>.
/// </summary>
internal sealed class Shapes(Matrix transform)
{
    private const double Kappa = 0.5522847498;

    /// <summary>A builder without a transform.</summary>
    public static Shapes Identity { get; } = new(Matrix.Identity);

    /// <summary>A builder that rotates around <paramref name="origin"/> (degrees, clockwise) and then translates.</summary>
    /// <param name="origin">The point that ends up at <paramref name="origin"/>.</param>
    /// <param name="degrees">Rotation, clockwise.</param>
    /// <returns>The builder.</returns>
    public static Shapes At(Point origin, double degrees)
    {
        var matrix = Matrix.Identity;
        matrix.Rotate(degrees);
        matrix.Translate(origin.X, origin.Y);
        return new Shapes(matrix);
    }

    /// <summary>Joins figures into one geometry.</summary>
    /// <param name="figures">The figures.</param>
    /// <returns>The geometry (non-zero fill).</returns>
    public static PathGeometry Join(IEnumerable<PathFigure> figures)
    {
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
        foreach (PathFigure figure in figures)
        {
            geometry.Figures.Add(figure);
        }

        return geometry;
    }

    /// <summary>A rectangle with optionally rounded corners.</summary>
    /// <param name="r">The rectangle (in local coordinates).</param>
    /// <param name="radius">Corner radius (0 for square corners).</param>
    /// <returns>The figure.</returns>
    public PathFigure Rect(Rect r, double radius = 0)
    {
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (radius <= 0)
        {
            return Polygon(r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft);
        }

        double k = radius * (1 - Kappa);
        var figure = Start(new Point(r.Left + radius, r.Top));
        Line(figure, new Point(r.Right - radius, r.Top));
        Bezier(figure, new Point(r.Right - k, r.Top), new Point(r.Right, r.Top + k), new Point(r.Right, r.Top + radius));
        Line(figure, new Point(r.Right, r.Bottom - radius));
        Bezier(figure, new Point(r.Right, r.Bottom - k), new Point(r.Right - k, r.Bottom), new Point(r.Right - radius, r.Bottom));
        Line(figure, new Point(r.Left + radius, r.Bottom));
        Bezier(figure, new Point(r.Left + k, r.Bottom), new Point(r.Left, r.Bottom - k), new Point(r.Left, r.Bottom - radius));
        Line(figure, new Point(r.Left, r.Top + radius));
        Bezier(figure, new Point(r.Left, r.Top + k), new Point(r.Left + k, r.Top), new Point(r.Left + radius, r.Top));
        figure.IsClosed = true;
        return figure;
    }

    /// <summary>An ellipse.</summary>
    /// <param name="center">Centre (local coordinates).</param>
    /// <param name="rx">Horizontal radius.</param>
    /// <param name="ry">Vertical radius.</param>
    /// <returns>The figure.</returns>
    public PathFigure Ellipse(Point center, double rx, double ry)
    {
        double kx = rx * Kappa;
        double ky = ry * Kappa;
        double cx = center.X;
        double cy = center.Y;
        var figure = Start(new Point(cx, cy - ry));
        Bezier(figure, new Point(cx + kx, cy - ry), new Point(cx + rx, cy - ky), new Point(cx + rx, cy));
        Bezier(figure, new Point(cx + rx, cy + ky), new Point(cx + kx, cy + ry), new Point(cx, cy + ry));
        Bezier(figure, new Point(cx - kx, cy + ry), new Point(cx - rx, cy + ky), new Point(cx - rx, cy));
        Bezier(figure, new Point(cx - rx, cy - ky), new Point(cx - kx, cy - ry), new Point(cx, cy - ry));
        figure.IsClosed = true;
        return figure;
    }

    /// <summary>A closed polygon.</summary>
    /// <param name="points">Corner points (local coordinates).</param>
    /// <returns>The figure.</returns>
    public PathFigure Polygon(params Point[] points)
    {
        PathFigure figure = Start(points[0]);
        foreach (Point point in points.Skip(1))
        {
            Line(figure, point);
        }

        figure.IsClosed = true;
        return figure;
    }

    /// <summary>An open polyline (for strokes).</summary>
    /// <param name="points">The points (local coordinates).</param>
    /// <returns>The figure.</returns>
    public PathFigure Polyline(params Point[] points)
    {
        PathFigure figure = Polygon(points);
        figure.IsClosed = false;
        figure.IsFilled = false;
        return figure;
    }

    /// <summary>An open curve through cubic Bezier segments (for strokes): start, then triples of control/control/end.</summary>
    /// <param name="start">The first point.</param>
    /// <param name="points">Control points and end points, three per segment.</param>
    /// <returns>The figure.</returns>
    public PathFigure Curve(Point start, params Point[] points)
    {
        PathFigure figure = Start(start);
        for (int i = 0; i + 2 < points.Length; i += 3)
        {
            Bezier(figure, points[i], points[i + 1], points[i + 2]);
        }

        figure.IsFilled = false;
        return figure;
    }

    /// <summary>
    /// The glass of a C9 bulb pointing down (local +y) around the local y axis: a flat neck, a rounded shoulder and a
    /// rounded tip. The one outline of every bulb the generator draws.
    /// </summary>
    /// <param name="neckY">Local y of the neck (where the screw base meets the glass).</param>
    /// <param name="neckHalf">Half width of the neck.</param>
    /// <param name="shoulderY">Local y of the widest part.</param>
    /// <param name="shoulderHalf">Half width of the widest part.</param>
    /// <param name="tipY">Local y of the tip.</param>
    /// <returns>The figure.</returns>
    public PathFigure Glass(double neckY, double neckHalf, double shoulderY, double shoulderHalf, double tipY)
    {
        double upper = shoulderY - neckY;
        double lower = tipY - shoulderY;
        double tipRound = shoulderHalf * 0.30 * 1.55;
        var figure = Start(new Point(-neckHalf, neckY));
        Line(figure, new Point(neckHalf, neckY));
        Bezier(figure, new Point(neckHalf + (shoulderHalf - neckHalf) * 0.10, neckY + upper * 0.38), new Point(shoulderHalf, shoulderY - upper * 0.52), new Point(shoulderHalf, shoulderY));
        Bezier(figure, new Point(shoulderHalf, shoulderY + lower * 0.50), new Point(tipRound, tipY - lower * 0.03), new Point(0, tipY));
        Bezier(figure, new Point(-tipRound, tipY - lower * 0.03), new Point(-shoulderHalf, shoulderY + lower * 0.50), new Point(-shoulderHalf, shoulderY));
        Bezier(figure, new Point(-shoulderHalf, shoulderY - upper * 0.52), new Point(-neckHalf - (shoulderHalf - neckHalf) * 0.10, neckY + upper * 0.38), new Point(-neckHalf, neckY));
        figure.IsClosed = true;
        return figure;
    }

    /// <summary>The glass of a small bulb in the proportions of the icon (neck 48 % of the width, shoulder at 31 %).</summary>
    /// <param name="neckY">Local y of the neck.</param>
    /// <param name="length">Neck-to-tip length.</param>
    /// <param name="width">Width at the shoulder.</param>
    /// <returns>The figure.</returns>
    public PathFigure Glass(double neckY, double length, double width) =>
        Glass(neckY, width * 0.24, neckY + length * 0.31, width / 2, neckY + length);

    private Point T(Point p) => transform.Transform(p);

    private PathFigure Start(Point p) => new() { StartPoint = T(p), IsClosed = false, IsFilled = true };

    private void Line(PathFigure figure, Point p) => figure.Segments.Add(new LineSegment(T(p), true));

    private void Bezier(PathFigure figure, Point c1, Point c2, Point end) => figure.Segments.Add(new BezierSegment(T(c1), T(c2), T(end), true));
}
