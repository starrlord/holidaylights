namespace HolidayLights.Core.Layout;

/// <summary>One closed outline to frame: a display's work area, or a group of touching displays (PRODUCT-SPEC 5.2.4).</summary>
/// <param name="Edges">The outline edges in clockwise order, starting with the top edge at the top-left corner.</param>
internal sealed record FrameOutline(IReadOnlyList<FrameEdge> Edges);

/// <summary>One straight outline edge and the per-display pieces it is cut into.</summary>
/// <param name="Id">Unique id of the edge within the plan.</param>
/// <param name="Side">The strip side: the edge's outward direction.</param>
/// <param name="StartConvex">True when the clockwise walk turns right (a convex corner) where the edge begins.</param>
/// <param name="Pieces">The pieces along the edge, left to right or top to bottom.</param>
internal sealed record FrameEdge(int Id, Side Side, bool StartConvex, IReadOnlyList<FramePiece> Pieces)
{
    /// <summary>True when the clockwise walk runs left to right or top to bottom (top and right edges).</summary>
    public bool RunsForward => Side is Side.Top or Side.Right;
}

/// <summary>The part of an outline edge that lies on one display.</summary>
/// <param name="TargetIndex">The display (index into the layout targets).</param>
/// <param name="Low">Start along the edge (x for horizontal edges, y for vertical edges).</param>
/// <param name="High">End along the edge.</param>
/// <param name="LowIsCorner">The low end is a convex outline corner (horizontal pieces draw the corner bulb there).</param>
/// <param name="HighIsCorner">The high end is a convex outline corner.</param>
internal sealed record FramePiece(int TargetIndex, int Low, int High, bool LowIsCorner, bool HighIsCorner);
