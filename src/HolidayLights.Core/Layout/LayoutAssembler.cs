namespace HolidayLights.Core.Layout;

/// <summary>
/// Turns laid-out pieces into the contract model: strips per display in build order, dense ordinals, and one clockwise
/// ring per outline (PRODUCT-SPEC 5.7 "ring order").
/// </summary>
internal static class LayoutAssembler
{
    /// <summary>Builds the layout.</summary>
    /// <param name="targets">The targets, in input order.</param>
    /// <param name="arrangement">The arrangement that was laid out.</param>
    /// <param name="mode">The frame mode.</param>
    /// <param name="outlines">The plan.</param>
    /// <param name="pieces">The laid-out pieces, indexed like the plan: <c>[outline][edge][piece]</c>.</param>
    /// <returns>The layout.</returns>
    public static LightsLayout Assemble(
        IReadOnlyList<LayoutTarget> targets,
        SlotAssignment arrangement,
        FrameMode mode,
        IReadOnlyList<FrameOutline> outlines,
        IReadOnlyList<PieceLayout[][]> pieces)
    {
        // Strips per display in the 5.4 build order (Top, Bottom, Right, Left), pieces of one side along the side.
        List<PieceLayout>[] strips = [.. targets.Select(_ => new List<PieceLayout>())];
        foreach (PieceLayout piece in pieces.SelectMany(o => o).SelectMany(e => e))
        {
            strips[piece.TargetIndex].Add(piece);
        }

        var firstOrdinal = new Dictionary<PieceLayout, int>(ReferenceEqualityComparer.Instance);
        var placed = new List<(PlacedBulb Bulb, string DisplayId, int Strip)>();
        for (int t = 0; t < targets.Count; t++)
        {
            strips[t] = [.. strips[t].OrderBy(p => p.BuildRank).ThenBy(p => p.Low)];
            for (int s = 0; s < strips[t].Count; s++)
            {
                firstOrdinal.Add(strips[t][s], placed.Count);
                placed.AddRange(strips[t][s].InStripOrder().Select(b => (b, targets[t].DisplayId, s)));
            }
        }

        var ringId = new int[placed.Count];
        var ringIndex = new int[placed.Count];
        var rings = new LightsRing[outlines.Count];
        for (int r = 0; r < outlines.Count; r++)
        {
            List<int> order = RingOrder(outlines[r], pieces[r], firstOrdinal);
            for (int k = 0; k < order.Count; k++)
            {
                ringId[order[k]] = r;
                ringIndex[order[k]] = k;
            }

            rings[r] = new LightsRing(r, order);
        }

        var placements = new BulbPlacement[placed.Count];
        for (int ordinal = 0; ordinal < placed.Count; ordinal++)
        {
            (PlacedBulb bulb, string displayId, int strip) = placed[ordinal];
            placements[ordinal] = new BulbPlacement
            {
                Ordinal = ordinal,
                DisplayId = displayId,
                StripIndex = strip,
                Slot = bulb.Slot,
                Index = bulb.Index,
                BulbId = bulb.BulbId,
                Flavor = bulb.Flavor,
                Bounds = bulb.Bounds,
                ChaseIndex = bulb.ChaseIndex,
                RingId = ringId[ordinal],
                RingIndex = ringIndex[ordinal],
            };
        }

        var displays = new DisplayLayout[targets.Count];
        for (int t = 0; t < targets.Count; t++)
        {
            var stripLayouts = new StripLayout[strips[t].Count];
            for (int s = 0; s < stripLayouts.Length; s++)
            {
                PieceLayout piece = strips[t][s];
                int first = firstOrdinal[piece];
                int total = piece.InStripOrder().Count();
                stripLayouts[s] = new StripLayout
                {
                    Index = s,
                    DisplayId = targets[t].DisplayId,
                    Side = piece.Side,
                    Rect = piece.Rect,
                    Thickness = piece.Thickness,
                    Count = piece.SideBulbs.Count,
                    Gap = piece.Gap,
                    Placements = placements[first..(first + total)],
                    OutlineEdge = piece.OutlineEdge,
                };
            }

            displays[t] = new DisplayLayout(targets[t], stripLayouts);
        }

        return new LightsLayout
        {
            Mode = mode,
            Arrangement = arrangement,
            Displays = displays,
            Placements = placements,
            Rings = rings,
        };
    }

    // Clockwise from the top-left corner: at every convex vertex its corner bulb (drawn by the horizontal piece that
    // ends there), then each edge's side bulbs in walking direction (top left to right, right top to bottom, bottom right
    // to left, left bottom to top).
    private static List<int> RingOrder(FrameOutline outline, PieceLayout[][] pieces, Dictionary<PieceLayout, int> firstOrdinal)
    {
        var order = new List<int>();
        for (int e = 0; e < outline.Edges.Count; e++)
        {
            FrameEdge edge = outline.Edges[e];
            if (edge.StartConvex)
            {
                int previous = (e + outline.Edges.Count - 1) % outline.Edges.Count;
                int corner = CornerAtStart(edge, pieces[e], pieces[previous], firstOrdinal);
                if (corner >= 0)
                {
                    order.Add(corner);
                }
            }

            PieceLayout[] along = pieces[e];
            IEnumerable<PieceLayout> walk = edge.RunsForward ? along : along.Reverse();
            foreach (PieceLayout piece in walk)
            {
                int first = firstOrdinal[piece] + (piece.LowCorner is null ? 0 : 1) + (piece.HighCorner is null ? 0 : 1);
                IEnumerable<int> bulbs = Enumerable.Range(first, piece.SideBulbs.Count);
                order.AddRange(edge.RunsForward ? bulbs : bulbs.Reverse());
            }
        }

        return order;
    }

    // The corner at the start of a clockwise edge: a top edge starts at its own left end, a bottom edge at its own right
    // end; a right edge starts where the previous (top) edge ends on the right, a left edge where the previous (bottom)
    // edge ends on the left.
    private static int CornerAtStart(FrameEdge edge, PieceLayout[] own, PieceLayout[] previous, Dictionary<PieceLayout, int> firstOrdinal)
    {
        (PieceLayout? piece, bool lowEnd) = edge.Side switch
        {
            Side.Top => (own.FirstOrDefault(), true),
            Side.Bottom => (own.LastOrDefault(), false),
            Side.Right => (previous.LastOrDefault(), false),
            _ => (previous.FirstOrDefault(), true),
        };
        if (piece is null)
        {
            return -1;
        }

        int first = firstOrdinal[piece];
        if (lowEnd)
        {
            return piece.LowCorner is null ? -1 : first;
        }

        return piece.HighCorner is null ? -1 : first + (piece.LowCorner is null ? 0 : 1);
    }
}
