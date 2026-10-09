namespace HolidayLights.Core.Abstractions;

/// <summary>An edge of the screen (a side strip). Values are the 5.4 side slots 0-3, clockwise from the top.</summary>
public enum Side
{
    /// <summary>The top edge (5.4 slot 0).</summary>
    Top = 0,

    /// <summary>The right edge (5.4 slot 1).</summary>
    Right = 1,

    /// <summary>The bottom edge (5.4 slot 2).</summary>
    Bottom = 2,

    /// <summary>The left edge (5.4 slot 3).</summary>
    Left = 3,
}

/// <summary>A corner of the screen. Values are the 5.4 corner slots 4-7 minus 4, clockwise from the top-left.</summary>
public enum Corner
{
    /// <summary>Top-left corner (5.4 slot 4).</summary>
    TopLeft = 0,

    /// <summary>Top-right corner (5.4 slot 5).</summary>
    TopRight = 1,

    /// <summary>Bottom-right corner (5.4 slot 6).</summary>
    BottomRight = 2,

    /// <summary>Bottom-left corner (5.4 slot 7).</summary>
    BottomLeft = 3,
}

/// <summary>
/// Which picture of a bulb is meant: one of the 4 sides, one of the 4 corners, or the bulb-list preview.
/// Values are the 5.4 slot numbers 0-8.
/// </summary>
public enum CellSlot
{
    /// <summary>Top side (slot 0).</summary>
    Top = 0,

    /// <summary>Right side (slot 1).</summary>
    Right = 1,

    /// <summary>Bottom side (slot 2).</summary>
    Bottom = 2,

    /// <summary>Left side (slot 3).</summary>
    Left = 3,

    /// <summary>Top-left corner (slot 4).</summary>
    TopLeft = 4,

    /// <summary>Top-right corner (slot 5).</summary>
    TopRight = 5,

    /// <summary>Bottom-right corner (slot 6).</summary>
    BottomRight = 6,

    /// <summary>Bottom-left corner (slot 7).</summary>
    BottomLeft = 7,

    /// <summary>The bulb-list preview (slot 8; "Bulb List Preview" in Bulb Editing).</summary>
    Preview = 8,
}

/// <summary>Conversions and orders for <see cref="Side"/>, <see cref="Corner"/> and <see cref="CellSlot"/>.</summary>
public static class CellSlots
{
    /// <summary>The four sides, clockwise from the top (Top, Right, Bottom, Left).</summary>
    public static IReadOnlyList<Side> Sides { get; } = [Side.Top, Side.Right, Side.Bottom, Side.Left];

    /// <summary>The 5.4 strip build order: Top, Bottom, Right, Left.</summary>
    public static IReadOnlyList<Side> BuildOrder { get; } = [Side.Top, Side.Bottom, Side.Right, Side.Left];

    /// <summary>The four corners, clockwise from the top-left (TopLeft, TopRight, BottomRight, BottomLeft).</summary>
    public static IReadOnlyList<Corner> Corners { get; } = [Corner.TopLeft, Corner.TopRight, Corner.BottomRight, Corner.BottomLeft];

    /// <summary>Returns the slot of a side.</summary>
    /// <param name="side">The side.</param>
    /// <returns>Slot 0-3.</returns>
    public static CellSlot ToSlot(this Side side) => (CellSlot)(int)side;

    /// <summary>Returns the slot of a corner.</summary>
    /// <param name="corner">The corner.</param>
    /// <returns>Slot 4-7.</returns>
    public static CellSlot ToSlot(this Corner corner) => (CellSlot)(4 + (int)corner);

    /// <summary>True for the four side slots.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>True when <paramref name="slot"/> is Top, Right, Bottom or Left.</returns>
    public static bool IsSide(this CellSlot slot) => slot is >= CellSlot.Top and <= CellSlot.Left;

    /// <summary>True for the four corner slots.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>True when <paramref name="slot"/> is a corner.</returns>
    public static bool IsCorner(this CellSlot slot) => slot is >= CellSlot.TopLeft and <= CellSlot.BottomLeft;

    /// <summary>Returns the side of a side slot.</summary>
    /// <param name="slot">A side slot.</param>
    /// <returns>The side.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is not a side.</exception>
    public static Side ToSide(this CellSlot slot) =>
        slot.IsSide() ? (Side)(int)slot : throw new ArgumentOutOfRangeException(nameof(slot), slot, "Not a side slot.");

    /// <summary>Returns the corner of a corner slot.</summary>
    /// <param name="slot">A corner slot.</param>
    /// <returns>The corner.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is not a corner.</exception>
    public static Corner ToCorner(this CellSlot slot) =>
        slot.IsCorner() ? (Corner)((int)slot - 4) : throw new ArgumentOutOfRangeException(nameof(slot), slot, "Not a corner slot.");

    /// <summary>True for the top and bottom sides (strips that run horizontally and own the corners).</summary>
    /// <param name="side">The side.</param>
    /// <returns>True for <see cref="Side.Top"/> and <see cref="Side.Bottom"/>.</returns>
    public static bool IsHorizontal(this Side side) => side is Side.Top or Side.Bottom;

    /// <summary>The two corners drawn by a horizontal strip, in strip order (left end first).</summary>
    /// <param name="side"><see cref="Side.Top"/> or <see cref="Side.Bottom"/>.</param>
    /// <returns>(TopLeft, TopRight) for the top strip, (BottomLeft, BottomRight) for the bottom strip.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="side"/> is vertical.</exception>
    public static (Corner Start, Corner End) CornersOf(Side side) => side switch
    {
        Side.Top => (Corner.TopLeft, Corner.TopRight),
        Side.Bottom => (Corner.BottomLeft, Corner.BottomRight),
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Only horizontal strips draw corners."),
    };
}
