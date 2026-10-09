namespace HolidayLights.Core.Layout;

/// <summary>
/// Lays out one strip or outline piece with the 5.4 rules: horizontal strips own their
/// corners, vertical strips fit between the horizontal strips of their display.
/// </summary>
/// <remarks>Measured edges and corners are cached per scale, so pieces of one edge on displays with equal scales share them.</remarks>
internal sealed class PieceLayouter
{
    private readonly SlotAssignment arrangement;
    private readonly IBulbResolver bulbs;
    private readonly Dictionary<(Side Side, double Scale), EdgeBulbs> edges = [];
    private readonly Dictionary<(Corner Corner, double Scale), CornerBulb?> corners = [];

    /// <summary>Creates a layouter for one arrangement.</summary>
    /// <param name="arrangement">The 8 boxes.</param>
    /// <param name="bulbs">Resolves ids.</param>
    public PieceLayouter(SlotAssignment arrangement, IBulbResolver bulbs)
    {
        this.arrangement = arrangement;
        this.bulbs = bulbs;
    }

    /// <summary>Lays out a top or bottom piece (<c>TopBulbWorld::Layout</c> / <c>BottomBulbWorld::Layout</c> and Render).</summary>
    /// <param name="edge">The outline edge.</param>
    /// <param name="piece">The piece.</param>
    /// <param name="target">The piece's display.</param>
    /// <param name="firstIndex">The position <c>i</c> of its first side bulb.</param>
    /// <param name="firstChase">The chase counter <c>k</c> of its first side bulb.</param>
    /// <param name="outlineEdge">The outline edge id to report (All Displays Together), or null.</param>
    /// <returns>The laid-out piece.</returns>
    public PieceLayout LayoutHorizontal(FrameEdge edge, FramePiece piece, LayoutTarget target, int firstIndex, int firstChase, int? outlineEdge)
    {
        EdgeBulbs run = Edge(edge.Side, target.Scale);
        (Corner lowCorner, Corner highCorner) = CellSlots.CornersOf(edge.Side);
        int width = piece.High - piece.Low;
        CornerBulb? low = piece.LowIsCorner ? Corner(lowCorner, target.Scale) : null;
        CornerBulb? high = piece.HighIsCorner ? Corner(highCorner, target.Scale) : null;

        // A piece narrower than its corner bulbs (a few pixels of outline between displays) drops them.
        if (low is not null && low.Size.Width > width)
        {
            low = null;
        }

        if (high is not null && high.Size.Width + (low?.Size.Width ?? 0) > width)
        {
            high = null;
        }

        int start = low?.Reserved ?? 0;
        int limit = width - (high?.Reserved ?? 0);
        RunFit fit = ClassicRun.Fit(run, firstIndex, start, limit);
        int thickness = Math.Max(fit.Thickness, Math.Max(low?.Size.Height ?? 0, high?.Size.Height ?? 0));
        double gap = ClassicRun.Gap(fit, limit);

        bool top = edge.Side == Side.Top;
        int line = top ? target.Area.Top : target.Area.Bottom;
        int CellTop(SizeI cell) => top ? line : line - cell.Height;
        RunBulb[] placed = ClassicRun.Place(run, firstIndex, firstChase, fit.Count, start, gap, out int nextChase);

        return new PieceLayout
        {
            TargetIndex = piece.TargetIndex,
            Side = edge.Side,
            OutlineEdge = outlineEdge,
            Low = piece.Low,
            Rect = top ? new RectI(piece.Low, line, piece.High, line + thickness) : new RectI(piece.Low, line - thickness, piece.High, line),
            Thickness = thickness,
            Gap = gap,
            LowCorner = low is null ? null : new PlacedBulb(low.Id, low.Slot, 0, 0, RectI.FromXYWH(piece.Low, CellTop(low.Size), low.Size.Width, low.Size.Height), null),
            HighCorner = high is null ? null : new PlacedBulb(high.Id, high.Slot, 1, 0, RectI.FromXYWH(piece.High - high.Size.Width, CellTop(high.Size), high.Size.Width, high.Size.Height), null),
            SideBulbs = [.. placed.Select(b => new PlacedBulb(
                b.BulbId, edge.Side.ToSlot(), b.Index, b.Flavor, RectI.FromXYWH(piece.Low + b.Offset, CellTop(b.Cell), b.Cell.Width, b.Cell.Height), b.ChaseIndex))],
            NextChase = nextChase,
        };
    }

    /// <summary>
    /// Lays out a right or left piece (<c>RightBulbWorld::Layout</c> / <c>LeftBulbWorld::Layout</c> and Render) between the
    /// horizontal strips of its display: wherever a horizontal strip of the same display crosses the piece's column, the
    /// piece starts below it (top strips) or ends above it (bottom strips), so strips never overlap.
    /// </summary>
    /// <param name="edge">The outline edge.</param>
    /// <param name="piece">The piece.</param>
    /// <param name="target">The piece's display.</param>
    /// <param name="firstIndex">The position <c>i</c> of its first side bulb.</param>
    /// <param name="firstChase">The chase counter <c>k</c> of its first side bulb.</param>
    /// <param name="outlineEdge">The outline edge id to report (All Displays Together), or null.</param>
    /// <param name="horizontals">The laid-out horizontal pieces of the same display.</param>
    /// <returns>The laid-out piece.</returns>
    public PieceLayout LayoutVertical(FrameEdge edge, FramePiece piece, LayoutTarget target, int firstIndex, int firstChase, int? outlineEdge, IEnumerable<PieceLayout> horizontals)
    {
        EdgeBulbs run = Edge(edge.Side, target.Scale);
        RectI area = target.Area;
        bool right = edge.Side == Side.Right;
        int column = Math.Max(1, run.MaxThickness);
        int columnLeft = right ? area.Right - column : area.Left;
        int columnRight = right ? area.Right : area.Left + column;

        int low = piece.Low;
        int high = piece.High;
        foreach (PieceLayout strip in horizontals)
        {
            if (strip.Rect.Left < columnRight && strip.Rect.Right > columnLeft)
            {
                if (strip.Side == Side.Top)
                {
                    low = Math.Max(low, strip.Rect.Bottom);
                }
                else
                {
                    high = Math.Min(high, strip.Rect.Top);
                }
            }
        }

        high = Math.Max(high, low);
        int length = high - low;
        RunFit fit = ClassicRun.Fit(run, firstIndex, 0, length);
        double gap = ClassicRun.Gap(fit, length);
        int thickness = fit.Thickness;
        RunBulb[] placed = ClassicRun.Place(run, firstIndex, firstChase, fit.Count, 0, gap, out int nextChase);

        return new PieceLayout
        {
            TargetIndex = piece.TargetIndex,
            Side = edge.Side,
            OutlineEdge = outlineEdge,
            Low = piece.Low,
            Rect = right ? new RectI(area.Right - thickness, low, area.Right, high) : new RectI(area.Left, low, area.Left + thickness, high),
            Thickness = thickness,
            Gap = gap,
            LowCorner = null,
            HighCorner = null,
            SideBulbs = [.. placed.Select(b => new PlacedBulb(
                b.BulbId, edge.Side.ToSlot(), b.Index, b.Flavor,
                RectI.FromXYWH(right ? area.Right - b.Cell.Width : area.Left, low + b.Offset, b.Cell.Width, b.Cell.Height), b.ChaseIndex))],
            NextChase = nextChase,
        };
    }

    private EdgeBulbs Edge(Side side, double scale)
    {
        if (!edges.TryGetValue((side, scale), out EdgeBulbs? edge))
        {
            edge = EdgeBulbs.Create(side, arrangement.GetEdge(side), bulbs, scale);
            edges.Add((side, scale), edge);
        }

        return edge;
    }

    private CornerBulb? Corner(Corner corner, double scale)
    {
        if (!corners.TryGetValue((corner, scale), out CornerBulb? bulb))
        {
            bulb = CornerBulb.Create(arrangement.GetCorner(corner), corner, bulbs, scale);
            corners.Add((corner, scale), bulb);
        }

        return bulb;
    }
}
