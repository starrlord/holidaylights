using System.Globalization;

namespace HolidayLights.App.Controls;

/// <summary>The kinds of place a bulb can be used for (PRODUCT-SPEC 3.2.4).</summary>
public enum ArrangementTargetKind
{
    /// <summary>All four edges become this bulb only and all four corners become it (the default target).</summary>
    WholeFrame,

    /// <summary>All four edges become this bulb only; the corners stay.</summary>
    AllEdges,

    /// <summary>All four corners become this bulb; the edges stay.</summary>
    AllCorners,

    /// <summary>A chip of an edge: that bulb is replaced, its position kept.</summary>
    Chip,

    /// <summary>An edge's "+": the bulb is added at the end of the edge.</summary>
    Plus,

    /// <summary>A corner box.</summary>
    Corner,
}

/// <summary>
/// The target of the Bulb Factory (PRODUCT-SPEC 3.2.4): the place a bulb goes when it is double-clicked, when Enter is
/// pressed on it, or when the "Use for ..." button is pressed. There is always exactly one.
/// </summary>
/// <param name="Kind">The kind.</param>
/// <param name="Side">The edge of a chip or "+".</param>
/// <param name="Index">The position of a chip in its edge (0-based).</param>
/// <param name="Corner">The corner.</param>
public sealed record ArrangementTarget(ArrangementTargetKind Kind, Side Side = Side.Top, int Index = 0, Corner Corner = Corner.TopLeft)
{
    /// <summary>"the whole frame" (the default each time the page opens).</summary>
    public static ArrangementTarget WholeFrame { get; } = new(ArrangementTargetKind.WholeFrame);

    /// <summary>"all four edges (corners stay)".</summary>
    public static ArrangementTarget AllEdges { get; } = new(ArrangementTargetKind.AllEdges);

    /// <summary>"all four corners".</summary>
    public static ArrangementTarget AllCorners { get; } = new(ArrangementTargetKind.AllCorners);

    /// <summary>True for the three quick targets (Whole Frame, All Edges, All Corners).</summary>
    public bool IsQuick => Kind is ArrangementTargetKind.WholeFrame or ArrangementTargetKind.AllEdges or ArrangementTargetKind.AllCorners;

    /// <summary>The slot whose strip or corner the stage highlights, or null for the quick targets.</summary>
    public CellSlot? Slot => Kind switch
    {
        ArrangementTargetKind.Chip or ArrangementTargetKind.Plus => Side.ToSlot(),
        ArrangementTargetKind.Corner => Corner.ToSlot(),
        _ => null,
    };

    /// <summary>A chip.</summary>
    /// <param name="side">The edge.</param>
    /// <param name="index">The position (0-based).</param>
    /// <returns>The target.</returns>
    public static ArrangementTarget ForChip(Side side, int index) => new(ArrangementTargetKind.Chip, side, index);

    /// <summary>An edge's "+".</summary>
    /// <param name="side">The edge.</param>
    /// <returns>The target.</returns>
    public static ArrangementTarget ForPlus(Side side) => new(ArrangementTargetKind.Plus, side);

    /// <summary>A corner.</summary>
    /// <param name="corner">The corner.</param>
    /// <returns>The target.</returns>
    public static ArrangementTarget ForCorner(Corner corner) => new(ArrangementTargetKind.Corner, Corner: corner);

    /// <summary>
    /// The target after the arrangement changed: a chip beyond the end of its edge becomes the edge's "+"; a "+" of a full
    /// edge becomes its last chip.
    /// </summary>
    /// <param name="arrangement">The arrangement now.</param>
    /// <returns>A valid target.</returns>
    public ArrangementTarget Normalize(SlotAssignment arrangement)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        int count = arrangement.GetEdge(Side).Count;
        return Kind switch
        {
            ArrangementTargetKind.Chip when Index >= count => count < SlotAssignment.MaxTypesPerEdge ? ForPlus(Side) : ForChip(Side, count - 1),
            ArrangementTargetKind.Plus when count >= SlotAssignment.MaxTypesPerEdge => ForChip(Side, count - 1),
            _ => this,
        };
    }

    /// <summary>The phrase after "Double-click a bulb to use it for:" ("the 2nd bulb on the top edge").</summary>
    /// <param name="arrangement">The arrangement now.</param>
    /// <returns>The phrase.</returns>
    public string Sentence(SlotAssignment arrangement)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        return Kind switch
        {
            ArrangementTargetKind.WholeFrame => "the whole frame",
            ArrangementTargetKind.AllEdges => "all four edges (corners stay)",
            ArrangementTargetKind.AllCorners => "all four corners",
            ArrangementTargetKind.Chip => $"the {ArrangementTexts.Ordinal(Index + 1)} bulb on the {ArrangementTexts.EdgeName(Side)}",
            ArrangementTargetKind.Plus => string.Create(CultureInfo.CurrentCulture,
                $"a new bulb at the end of the {ArrangementTexts.EdgeName(Side)} ({arrangement.GetEdge(Side).Count + 1} of {SlotAssignment.MaxTypesPerEdge})"),
            _ => $"the {ArrangementTexts.CornerName(Corner)}",
        };
    }

    /// <summary>The primary button of the selected-bulb bar ("Use for the Whole Frame", "Add to the Top Edge").</summary>
    /// <returns>The Title Case label (without access key).</returns>
    public string ButtonText() => Kind switch
    {
        ArrangementTargetKind.WholeFrame => "Use for the Whole Frame",
        ArrangementTargetKind.AllEdges => "Use for All Edges",
        ArrangementTargetKind.AllCorners => "Use in All Corners",
        ArrangementTargetKind.Chip => $"Use for the {ArrangementTexts.Ordinal(Index + 1)} Bulb on the {ArrangementTexts.EdgeTitle(Side)}",
        ArrangementTargetKind.Plus => $"Add to the {ArrangementTexts.EdgeTitle(Side)}",
        _ => $"Use for the {ArrangementTexts.CornerTitle(Corner)}",
    };
}
