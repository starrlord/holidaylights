namespace HolidayLights.Core.Layout;

/// <summary>One straight edge of a clockwise rectilinear outline (screen coordinates, y grows downwards).</summary>
/// <param name="From">The start vertex.</param>
/// <param name="To">The end vertex.</param>
/// <param name="StartsConvex">True when the outline turns right at <paramref name="From"/> (a convex corner).</param>
internal readonly record struct OutlineSegment(PointI From, PointI To, bool StartsConvex)
{
    /// <summary>
    /// The outward direction: walking clockwise keeps the inside on the right, so an east-going edge faces up (Top), a
    /// south-going edge Right, a west-going edge Bottom and a north-going edge Left.
    /// </summary>
    public Side Facing =>
        To.X > From.X ? Side.Top :
        To.Y > From.Y ? Side.Right :
        To.X < From.X ? Side.Bottom :
        Side.Left;
}

/// <summary>
/// Traces the outer boundary of a union of axis-aligned rectangles (PRODUCT-SPEC 5.2.4 step 3: a rectilinear polygon,
/// holes ignored) on the grid of the rectangles' own coordinates.
/// </summary>
internal static class RectilinearOutline
{
    private const int East = 0;
    private const int South = 1;
    private const int West = 2;
    private const int North = 3;

    /// <summary>Traces the outline of a connected union of rectangles.</summary>
    /// <param name="rects">The rectangles; together they must form one region connected through shared edges.</param>
    /// <returns>
    /// The edges in clockwise order, starting with the top edge that leaves the top-left-most corner (always convex), with
    /// collinear pieces merged; empty when every rectangle is empty.
    /// </returns>
    public static IReadOnlyList<OutlineSegment> Trace(IReadOnlyList<RectI> rects)
    {
        RectI[] solid = [.. rects.Where(r => !r.IsEmpty)];
        if (solid.Length == 0)
        {
            return [];
        }

        int[] xs = [.. solid.SelectMany(r => new[] { r.Left, r.Right }).Distinct().Order()];
        int[] ys = [.. solid.SelectMany(r => new[] { r.Top, r.Bottom }).Distinct().Order()];
        bool[,] filled = Fill(solid, xs, ys);
        byte[,] outgoing = BoundaryEdges(filled, out (int X, int Y) start);
        List<(int X, int Y, int Direction)> moves = Walk(outgoing, start);
        return Merge(moves, xs, ys);
    }

    private static bool[,] Fill(RectI[] rects, int[] xs, int[] ys)
    {
        var filled = new bool[xs.Length - 1, ys.Length - 1];
        foreach (RectI rect in rects)
        {
            int x0 = Array.BinarySearch(xs, rect.Left);
            int x1 = Array.BinarySearch(xs, rect.Right);
            int y0 = Array.BinarySearch(ys, rect.Top);
            int y1 = Array.BinarySearch(ys, rect.Bottom);
            for (int cx = x0; cx < x1; cx++)
            {
                for (int cy = y0; cy < y1; cy++)
                {
                    filled[cx, cy] = true;
                }
            }
        }

        return filled;
    }

    // Every cell side between a filled and an empty cell is a boundary edge, oriented clockwise (inside on the right) and
    // stored as a direction bit on its start vertex. The start is the top-left vertex of the first filled cell in reading
    // order: only the cell below-right of it is filled, so it is a simple convex corner on the outer boundary.
    private static byte[,] BoundaryEdges(bool[,] filled, out (int X, int Y) start)
    {
        int columns = filled.GetLength(0);
        int rows = filled.GetLength(1);
        var outgoing = new byte[columns + 1, rows + 1];
        start = (-1, -1);
        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < columns; cx++)
            {
                if (!filled[cx, cy])
                {
                    continue;
                }

                if (start.X < 0)
                {
                    start = (cx, cy);
                }

                if (cy == 0 || !filled[cx, cy - 1])
                {
                    outgoing[cx, cy] |= 1 << East;
                }

                if (cx == columns - 1 || !filled[cx + 1, cy])
                {
                    outgoing[cx + 1, cy] |= 1 << South;
                }

                if (cy == rows - 1 || !filled[cx, cy + 1])
                {
                    outgoing[cx + 1, cy + 1] |= 1 << West;
                }

                if (cx == 0 || !filled[cx - 1, cy])
                {
                    outgoing[cx, cy + 1] |= 1 << North;
                }
            }
        }

        return outgoing;
    }

    // Follows the boundary from the start corner. Where two boundary edges leave one vertex (regions touching at a point)
    // the leftmost turn is taken: it keeps the outside on the left, so the walk stays on the outer boundary.
    private static List<(int X, int Y, int Direction)> Walk(byte[,] outgoing, (int X, int Y) start)
    {
        var moves = new List<(int X, int Y, int Direction)>();
        int x = start.X;
        int y = start.Y;
        int direction = East;
        while (true)
        {
            outgoing[x, y] &= (byte)~(1 << direction);
            moves.Add((x, y, direction));
            (x, y) = direction switch
            {
                East => (x + 1, y),
                South => (x, y + 1),
                West => (x - 1, y),
                _ => (x, y - 1),
            };
            if ((x, y) == start)
            {
                return moves;
            }

            direction = NextDirection(outgoing[x, y], direction);
        }
    }

    private static int NextDirection(byte available, int heading)
    {
        foreach (int candidate in (ReadOnlySpan<int>)[(heading + 3) % 4, heading, (heading + 1) % 4])
        {
            if ((available & (1 << candidate)) != 0)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The outline is not closed.");
    }

    private static OutlineSegment[] Merge(List<(int X, int Y, int Direction)> moves, int[] xs, int[] ys)
    {
        var runs = new List<(int X, int Y, int Direction)>();
        foreach ((int X, int Y, int Direction) move in moves)
        {
            if (runs.Count == 0 || runs[^1].Direction != move.Direction)
            {
                runs.Add(move);
            }
        }

        var segments = new OutlineSegment[runs.Count];
        for (int k = 0; k < runs.Count; k++)
        {
            (int X, int Y, int Direction) run = runs[k];
            (int X, int Y, int Direction) next = runs[(k + 1) % runs.Count];
            int previousDirection = runs[(k + runs.Count - 1) % runs.Count].Direction;
            segments[k] = new OutlineSegment(
                new PointI(xs[run.X], ys[run.Y]),
                new PointI(xs[next.X], ys[next.Y]),
                StartsConvex: run.Direction == (previousDirection + 1) % 4);
        }

        return segments;
    }
}
