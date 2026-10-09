namespace HolidayLights.Core.Abstractions;

/// <summary>An integer point.</summary>
/// <param name="X">Horizontal coordinate (grows to the right).</param>
/// <param name="Y">Vertical coordinate (grows downwards).</param>
public readonly record struct PointI(int X, int Y);

/// <summary>An integer size.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct SizeI(int Width, int Height)
{
    /// <summary>True when either dimension is zero or negative.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// An integer rectangle with Win32 <c>RECT</c> semantics: <see cref="Right"/> and <see cref="Bottom"/> are exclusive.
/// </summary>
/// <remarks>
/// Used for physical-pixel areas (monitor rectangles, work areas, strips, bulb placements; virtual-screen coordinates,
/// which can be negative) and for art-pixel cell rectangles. It matches the <c>[x0, y0, x1, y1]</c> rectangles of the
/// golden test data (<c>tests/HolidayLights.Tests/Golden</c>).
/// </remarks>
/// <param name="Left">Left edge (inclusive).</param>
/// <param name="Top">Top edge (inclusive).</param>
/// <param name="Right">Right edge (exclusive).</param>
/// <param name="Bottom">Bottom edge (exclusive).</param>
public readonly record struct RectI(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Width (<see cref="Right"/> - <see cref="Left"/>).</summary>
    public int Width => Right - Left;

    /// <summary>Height (<see cref="Bottom"/> - <see cref="Top"/>).</summary>
    public int Height => Bottom - Top;

    /// <summary>True when the rectangle covers no pixel.</summary>
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    /// <summary>The top-left corner.</summary>
    public PointI TopLeft => new(Left, Top);

    /// <summary>The size of the rectangle.</summary>
    public SizeI Size => new(Width, Height);

    /// <summary>Creates a rectangle from its top-left corner and its size.</summary>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <returns>The rectangle <c>(x, y, x + width, y + height)</c>.</returns>
    public static RectI FromXYWH(int x, int y, int width, int height) => new(x, y, x + width, y + height);

    /// <summary>Tests whether a pixel lies inside the rectangle.</summary>
    /// <param name="point">The pixel.</param>
    /// <returns>True when <paramref name="point"/> is inside (right and bottom edges excluded).</returns>
    public bool Contains(PointI point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>Tests whether another rectangle lies completely inside this one.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>True when every pixel of <paramref name="other"/> is inside this rectangle.</returns>
    public bool Contains(RectI other) =>
        other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

    /// <summary>Tests whether two rectangles share at least one pixel.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>True when the rectangles overlap.</returns>
    public bool IntersectsWith(RectI other) =>
        other.Left < Right && other.Right > Left && other.Top < Bottom && other.Bottom > Top;

    /// <summary>Returns the overlapping part of two rectangles.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>The intersection, or <c>default</c> (empty) when they do not overlap.</returns>
    public RectI Intersect(RectI other)
    {
        var result = new RectI(
            Math.Max(Left, other.Left), Math.Max(Top, other.Top),
            Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
        return result.IsEmpty ? default : result;
    }

    /// <summary>Returns the smallest rectangle that contains both rectangles.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>The bounding rectangle; an empty operand is ignored.</returns>
    public RectI Union(RectI other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return new RectI(
            Math.Min(Left, other.Left), Math.Min(Top, other.Top),
            Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    /// <summary>Moves the rectangle.</summary>
    /// <param name="dx">Horizontal offset.</param>
    /// <param name="dy">Vertical offset.</param>
    /// <returns>The moved rectangle.</returns>
    public RectI Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);

    /// <summary>Formats the rectangle as <c>left,top,right,bottom</c> (the rectangle syntax of the golden layouts).</summary>
    /// <returns>The formatted rectangle.</returns>
    public override string ToString() => $"{Left},{Top},{Right},{Bottom}";
}
