namespace HolidayLights.Core.Layout;

/// <summary>A strip (or outline piece) after layout, before ordinals and rings are assigned.</summary>
internal sealed class PieceLayout
{
    /// <summary>The display (index into the layout targets).</summary>
    public required int TargetIndex { get; init; }

    /// <summary>The strip side.</summary>
    public required Side Side { get; init; }

    /// <summary>The outline edge id in All Displays Together, else null.</summary>
    public required int? OutlineEdge { get; init; }

    /// <summary>Where the piece starts along its edge (x for horizontal strips, y for vertical strips).</summary>
    public required int Low { get; init; }

    /// <summary>The strip rectangle.</summary>
    public required RectI Rect { get; init; }

    /// <summary>The strip thickness.</summary>
    public required int Thickness { get; init; }

    /// <summary>The fractional gap.</summary>
    public required double Gap { get; init; }

    /// <summary>The corner bulb at the low (left) end, if any.</summary>
    public required PlacedBulb? LowCorner { get; init; }

    /// <summary>The corner bulb at the high (right) end, if any.</summary>
    public required PlacedBulb? HighCorner { get; init; }

    /// <summary>The side bulbs in index order.</summary>
    public required IReadOnlyList<PlacedBulb> SideBulbs { get; init; }

    /// <summary>The chase counter after the last side bulb: where the next piece of the same outline edge continues.</summary>
    public required int NextChase { get; init; }

    /// <summary>The 5.4 build rank of the side: Top, Bottom, Right, Left.</summary>
    public int BuildRank => Side switch
    {
        Side.Top => 0,
        Side.Bottom => 1,
        Side.Right => 2,
        _ => 3,
    };

    /// <summary>Every placement in the 5.4 order of the golden layouts: corners (low end, high end), then side bulbs.</summary>
    /// <returns>The placements.</returns>
    public IEnumerable<PlacedBulb> InStripOrder()
    {
        if (LowCorner is { } low)
        {
            yield return low;
        }

        if (HighCorner is { } high)
        {
            yield return high;
        }

        foreach (PlacedBulb bulb in SideBulbs)
        {
            yield return bulb;
        }
    }
}

/// <summary>One placed bulb before it gets its ordinal and ring position.</summary>
/// <param name="BulbId">The arrangement id.</param>
/// <param name="Slot">The art slot.</param>
/// <param name="Index">Side bulbs: <c>i</c>; corners: 0 (low end) or 1 (high end).</param>
/// <param name="Flavor">Raw flavor (<c>i / n</c>; 0 for corners).</param>
/// <param name="Bounds">The cell rectangle in physical pixels.</param>
/// <param name="ChaseIndex">The chase counter <c>k</c>; null for corners.</param>
internal readonly record struct PlacedBulb(string BulbId, CellSlot Slot, int Index, int Flavor, RectI Bounds, int? ChaseIndex);
