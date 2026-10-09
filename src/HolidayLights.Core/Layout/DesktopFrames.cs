namespace HolidayLights.Core.Layout;

/// <summary>
/// The multi-monitor framing policy (PRODUCT-SPEC 5.2): which outlines get a frame and how their edges are cut into
/// strips, before any bulb is placed.
/// </summary>
/// <remarks>
/// <para><b>Each Display</b>: every display's area is its own outline, a rectangle whose four edges are the 5.4 strips.</para>
/// <para><b>All Displays Together</b> (5.2.4): areas that touch or overlap (gaps of 2 px or less count as touching, along
/// a shared stretch of edge; corner-to-corner contact does not) form one group; each group's union is outlined (holes
/// ignored); every outline edge is cut where it passes from one display to another, so no strip straddles two layer
/// windows. Convex outline corners are marked for corner bulbs; concave corners and seams are not.</para>
/// </remarks>
internal static class DesktopFrames
{
    /// <summary>Gaps of this many pixels or less between two areas count as touching.</summary>
    public const int TouchTolerance = 2;

    /// <summary>Plans the outlines of a set of targets.</summary>
    /// <param name="targets">The targets (areas in physical pixels).</param>
    /// <param name="mode">Each Display or All Displays Together.</param>
    /// <returns>One outline per framed group, ordered by the group's first target; targets with empty areas are skipped.</returns>
    public static IReadOnlyList<FrameOutline> Plan(IReadOnlyList<LayoutTarget> targets, FrameMode mode)
    {
        var outlines = new List<FrameOutline>();
        int edgeId = 0;
        foreach (int[] group in Groups(targets, mode))
        {
            var rects = new List<RectI>(group.Select(t => targets[t].Area));
            rects.AddRange(GapFillers(group, targets));
            IReadOnlyList<OutlineSegment> segments = RectilinearOutline.Trace(rects);
            var edges = new FrameEdge[segments.Count];
            for (int k = 0; k < segments.Count; k++)
            {
                edges[k] = CreateEdge(edgeId++, segments[k], segments[(k + 1) % segments.Count].StartsConvex, group, targets);
            }

            outlines.Add(new FrameOutline(edges));
        }

        return outlines;
    }

    /// <summary>True when two areas touch or overlap: they share a stretch of edge, closing gaps up to the tolerance.</summary>
    /// <param name="a">One area.</param>
    /// <param name="b">The other area.</param>
    /// <returns>True when they belong to one wreath.</returns>
    public static bool Touches(RectI a, RectI b)
    {
        int overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        int overlapY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return (overlapX > 0 && overlapY >= -TouchTolerance) || (overlapY > 0 && overlapX >= -TouchTolerance);
    }

    private static List<int[]> Groups(IReadOnlyList<LayoutTarget> targets, FrameMode mode)
    {
        int[] framed = [.. Enumerable.Range(0, targets.Count).Where(t => !targets[t].Area.IsEmpty)];
        if (mode == FrameMode.EachDisplay)
        {
            return [.. framed.Select(t => new[] { t })];
        }

        var parent = Enumerable.Range(0, targets.Count).ToArray();
        int Find(int t) => parent[t] == t ? t : parent[t] = Find(parent[t]);
        for (int i = 0; i < framed.Length; i++)
        {
            for (int j = i + 1; j < framed.Length; j++)
            {
                if (Touches(targets[framed[i]].Area, targets[framed[j]].Area))
                {
                    parent[Find(framed[j])] = Find(framed[i]);
                }
            }
        }

        return [.. framed.GroupBy(Find).Select(g => g.Order().ToArray()).OrderBy(g => g[0])];
    }

    // Narrow gaps between touching areas are filled so the union is one region; fillers belong to no display.
    private static IEnumerable<RectI> GapFillers(int[] group, IReadOnlyList<LayoutTarget> targets)
    {
        for (int i = 0; i < group.Length; i++)
        {
            for (int j = i + 1; j < group.Length; j++)
            {
                RectI a = targets[group[i]].Area;
                RectI b = targets[group[j]].Area;
                int overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                int overlapY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
                if (overlapY > 0 && overlapX < 0 && overlapX >= -TouchTolerance)
                {
                    yield return new RectI(Math.Min(a.Right, b.Right), Math.Max(a.Top, b.Top), Math.Max(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom));
                }
                else if (overlapX > 0 && overlapY < 0 && overlapY >= -TouchTolerance)
                {
                    yield return new RectI(Math.Max(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom), Math.Min(a.Right, b.Right), Math.Max(a.Top, b.Top));
                }
            }
        }
    }

    private static FrameEdge CreateEdge(int id, OutlineSegment segment, bool endConvex, int[] group, IReadOnlyList<LayoutTarget> targets)
    {
        Side side = segment.Facing;
        bool horizontal = side.IsHorizontal();
        int line = horizontal ? segment.From.Y : segment.From.X;
        int a = horizontal ? segment.From.X : segment.From.Y;
        int b = horizontal ? segment.To.X : segment.To.Y;
        int low = Math.Min(a, b);
        int high = Math.Max(a, b);
        bool forward = side is Side.Top or Side.Right;
        bool lowConvex = forward ? segment.StartsConvex : endConvex;
        bool highConvex = forward ? endConvex : segment.StartsConvex;
        IReadOnlyList<FramePiece> pieces = CutIntoPieces(side, line, low, high, lowConvex, highConvex, group, targets);
        return new FrameEdge(id, side, segment.StartsConvex, pieces);
    }

    // Each stretch of the edge belongs to the display whose side lies on it (the first one in target order where
    // displays overlap); stretches over a gap filler belong to none.
    private static FramePiece[] CutIntoPieces(Side side, int line, int low, int high, bool lowConvex, bool highConvex, int[] group, IReadOnlyList<LayoutTarget> targets)
    {
        var candidates = new List<(int Target, int Low, int High)>();
        foreach (int t in group)
        {
            RectI area = targets[t].Area;
            int sideLine = side switch
            {
                Side.Top => area.Top,
                Side.Bottom => area.Bottom,
                Side.Left => area.Left,
                _ => area.Right,
            };
            (int from, int to) = side.IsHorizontal() ? (area.Left, area.Right) : (area.Top, area.Bottom);
            from = Math.Max(from, low);
            to = Math.Min(to, high);
            if (sideLine == line && from < to)
            {
                candidates.Add((t, from, to));
            }
        }

        int[] breaks = [.. candidates.SelectMany(c => new[] { c.Low, c.High }).Distinct().Order()];
        var pieces = new List<(int Target, int Low, int High)>();
        for (int k = 0; k + 1 < breaks.Length; k++)
        {
            int from = breaks[k];
            int to = breaks[k + 1];
            int owner = candidates.FindIndex(c => c.Low <= from && c.High >= to);
            if (owner < 0)
            {
                continue;
            }

            int target = candidates[owner].Target;
            if (pieces.Count > 0 && pieces[^1].Target == target && pieces[^1].High == from)
            {
                pieces[^1] = (target, pieces[^1].Low, to);
            }
            else
            {
                pieces.Add((target, from, to));
            }
        }

        return [.. pieces.Select(p => new FramePiece(p.Target, p.Low, p.High, p.Low == low && lowConvex, p.High == high && highConvex))];
    }
}
