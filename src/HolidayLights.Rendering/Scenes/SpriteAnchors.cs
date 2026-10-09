namespace HolidayLights.Rendering.Scenes;

/// <summary>
/// Where a frame is drawn inside its placement: top cells are top-aligned, bottom cells
/// bottom-aligned, right cells right-aligned, left cells left-aligned; corners are flush with their corner. Frame 0 fills the
/// placement exactly; frames of another size (some add-on GIFs) keep the same alignment.
/// </summary>
internal static class SpriteAnchors
{
    /// <summary>Returns the top-left of a sprite of <paramref name="size"/> drawn for a placement.</summary>
    /// <param name="slot">The placement's art slot.</param>
    /// <param name="bounds">The placement bounds (frame 0 size).</param>
    /// <param name="size">The sprite size.</param>
    public static PointI Position(CellSlot slot, RectI bounds, SizeI size)
    {
        bool alignRight = slot is CellSlot.Right or CellSlot.TopRight or CellSlot.BottomRight;
        bool alignBottom = slot is CellSlot.Bottom or CellSlot.BottomLeft or CellSlot.BottomRight;
        int x = alignRight ? bounds.Right - size.Width : bounds.Left;
        int y = alignBottom ? bounds.Bottom - size.Height : bounds.Top;
        return new PointI(x, y);
    }
}
