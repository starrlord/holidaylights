namespace HolidayLights.Core.Layout;

/// <summary>
/// The 5.4 layout, exact (golden <c>Golden/layout/*.json.gz</c>), on scaled
/// integer inputs, plus the "All Displays Together" outline (PRODUCT-SPEC 5.2.4). Owner: core-layout.
/// </summary>
/// <remarks>
/// <para>Per strip (PRODUCT-SPEC 5.3.1): strips are built Top, Bottom, Right, Left; top
/// and bottom strips span the full width and own the corners (<c>cellWidth + hSpacing</c> each); right and left strips fit
/// between them; bulb <c>i</c> is type <c>ids[i % n]</c> with flavor <c>i / n</c> and takes
/// <c>spacing + cellLength + spacing</c>; the leftover is spread as a double gap after every bulb with truncated
/// positions; top cells are top-aligned, bottom cells bottom-aligned, right cells right-aligned; each strip is as thick as
/// its largest cell; the chase counter skips single-phase bulbs. Cell sizes (phase 0) and spacings are scaled with
/// <see cref="ArtScale.Scale(int, double)"/> first (5.3.2); the algorithm then runs on integers in physical pixels.</para>
/// <para>Each Display frames every target separately. All Displays Together frames each group of touching targets as one
/// outline, cut into per-display pieces that each behave as a 5.4 strip (own fit, gap and frame count), except that the
/// type and flavor index and the chase counter continue across the pieces of one outline edge, so neither the colors
/// nor Alternating and Bulb Chase restart at a seam. Both modes give one clockwise <see cref="LightsRing"/> per
/// outline.</para>
/// <para>One deliberate difference from 5.4 and the golden layouts, without any visible effect: a right strip without usable bulbs
/// reports a zero-width rectangle at the right edge between the horizontal strips (5.4 left that empty strip's rectangle at
/// the whole free area). <see cref="StripLayout.Gap"/> of an empty strip is <see cref="double.PositiveInfinity"/>.</para>
/// <para>Pure and thread-safe: every call works on its own data.</para>
/// </remarks>
public sealed class ClassicLayoutEngine : ILayoutEngine
{
    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">A target's scale is not a positive finite number.</exception>
    public LightsLayout Layout(IReadOnlyList<LayoutTarget> targets, SlotAssignment arrangement, IBulbResolver bulbs, FrameMode mode = FrameMode.EachDisplay)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(bulbs);
        foreach (LayoutTarget target in targets)
        {
            if (!double.IsFinite(target.Scale) || target.Scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targets), target.Scale, $"The scale of display '{target.DisplayId}' must be a positive number.");
            }
        }

        IReadOnlyList<FrameOutline> outlines = DesktopFrames.Plan(targets, mode);
        var layouter = new PieceLayouter(arrangement, bulbs);
        PieceLayout[][][] pieces = [.. outlines.Select(o => o.Edges.Select(e => new PieceLayout[e.Pieces.Count]).ToArray())];

        // Horizontal pieces first: they own the corners and decide where the vertical pieces of their display may run.
        // Pieces run left to right or top to bottom; the index and the chase counter continue from piece to piece.
        foreach ((FrameEdge edge, PieceLayout[] laidOut) in Edges(outlines, pieces).Where(e => e.Edge.Side.IsHorizontal()))
        {
            int index = 0;
            int chase = 1;
            for (int p = 0; p < edge.Pieces.Count; p++)
            {
                FramePiece piece = edge.Pieces[p];
                laidOut[p] = layouter.LayoutHorizontal(edge, piece, targets[piece.TargetIndex], index, chase, OutlineEdgeId(edge, mode));
                index += laidOut[p].SideBulbs.Count;
                chase = laidOut[p].NextChase;
            }
        }

        ILookup<int, PieceLayout> horizontals = pieces.SelectMany(o => o).SelectMany(e => e)
            .Where(p => p is not null && p.Side.IsHorizontal())
            .ToLookup(p => p.TargetIndex);
        foreach ((FrameEdge edge, PieceLayout[] laidOut) in Edges(outlines, pieces).Where(e => !e.Edge.Side.IsHorizontal()))
        {
            int index = 0;
            int chase = 1;
            for (int p = 0; p < edge.Pieces.Count; p++)
            {
                FramePiece piece = edge.Pieces[p];
                laidOut[p] = layouter.LayoutVertical(edge, piece, targets[piece.TargetIndex], index, chase, OutlineEdgeId(edge, mode), horizontals[piece.TargetIndex]);
                index += laidOut[p].SideBulbs.Count;
                chase = laidOut[p].NextChase;
            }
        }

        return LayoutAssembler.Assemble(targets, arrangement, mode, outlines, pieces);
    }

    private static int? OutlineEdgeId(FrameEdge edge, FrameMode mode) => mode == FrameMode.AllDisplaysTogether ? edge.Id : null;

    private static IEnumerable<(FrameEdge Edge, PieceLayout[] LaidOut)> Edges(IReadOnlyList<FrameOutline> outlines, PieceLayout[][][] pieces)
    {
        for (int o = 0; o < outlines.Count; o++)
        {
            for (int e = 0; e < outlines[o].Edges.Count; e++)
            {
                yield return (outlines[o].Edges[e], pieces[o][e]);
            }
        }
    }
}
