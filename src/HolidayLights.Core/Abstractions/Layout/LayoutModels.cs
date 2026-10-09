using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>"Bulb Size" (PRODUCT-SPEC 5.3.2): a factor on top of each display's scale.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BulbSize>))]
public enum BulbSize
{
    /// <summary>75 % of the display scale.</summary>
    [JsonStringEnumMemberName("small")]
    Small,

    /// <summary>100 % (default): the 2003 physical size next to everything else on screen.</summary>
    [JsonStringEnumMemberName("standard")]
    Standard,

    /// <summary>150 %.</summary>
    [JsonStringEnumMemberName("large")]
    Large,

    /// <summary>200 %.</summary>
    [JsonStringEnumMemberName("extraLarge")]
    ExtraLarge,
}

/// <summary>
/// The scaling rule shared by layout and sprites (PRODUCT-SPEC 5.3.2). Layout and the sprite pipeline must agree to the
/// pixel, so both use these functions and nothing else.
/// </summary>
public static class ArtScale
{
    /// <summary>The factor of a bulb size: Small 0.75, Standard 1.0, Large 1.5, Extra Large 2.0.</summary>
    /// <param name="size">The bulb size.</param>
    /// <returns>The factor.</returns>
    public static double Factor(BulbSize size) => size switch
    {
        BulbSize.Small => 0.75,
        BulbSize.Standard => 1.0,
        BulbSize.Large => 1.5,
        BulbSize.ExtraLarge => 2.0,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };

    /// <summary>The effective scale S of a display: display scale x bulb size factor (1.5 on the reference PC at Standard).</summary>
    /// <param name="displayScale">Effective DPI / 96.</param>
    /// <param name="size">The bulb size.</param>
    /// <returns>S.</returns>
    public static double Effective(double displayScale, BulbSize size) => displayScale * Factor(size);

    /// <summary>
    /// Scales an art-pixel length: <c>round(length x S)</c>, rounding halves away from zero, never below 1 for a positive
    /// length. Zero stays zero. Applies to cell widths, cell heights and spacings.
    /// </summary>
    /// <param name="length">Length in art pixels (zero or more).</param>
    /// <param name="scale">S.</param>
    /// <returns>Length in physical pixels.</returns>
    public static int Scale(int length, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return length == 0 ? 0 : Math.Max(1, (int)Math.Round(length * scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>Scales a cell size with <see cref="Scale(int, double)"/>.</summary>
    /// <param name="size">Size in art pixels.</param>
    /// <param name="scale">S.</param>
    /// <returns>Size in physical pixels.</returns>
    public static SizeI Scale(SizeI size, double scale) => new(Scale(size.Width, scale), Scale(size.Height, scale));
}

/// <summary>"Frame:" (PRODUCT-SPEC 3.7, 5.2): one frame per display, or one wreath around all displays.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FrameMode>))]
public enum FrameMode
{
    /// <summary>"Each Display" (default): every enabled display gets the complete arrangement around its work area.</summary>
    [JsonStringEnumMemberName("eachDisplay")]
    EachDisplay,

    /// <summary>"All Displays Together" (PO-1): the enabled displays are framed as one outline (algorithm 5.2.4).</summary>
    [JsonStringEnumMemberName("allDisplaysTogether")]
    AllDisplaysTogether,
}

/// <summary>One area to frame with bulbs.</summary>
/// <param name="DisplayId">The display (<see cref="DisplayInfo.DeviceId"/>).</param>
/// <param name="Area">The area in physical pixels: the work area for desktop lights, the whole display for the screen saver.</param>
/// <param name="Scale">S: the art-pixel to physical-pixel factor (<see cref="ArtScale.Effective"/>).</param>
public sealed record LayoutTarget(string DisplayId, RectI Area, double Scale);

/// <summary>One bulb placed on screen.</summary>
public sealed record BulbPlacement
{
    /// <summary>Index of this placement in <see cref="LightsLayout.Placements"/> (0-based, dense). Flash states are indexed by it.</summary>
    public required int Ordinal { get; init; }

    /// <summary>The display the bulb is drawn on.</summary>
    public required string DisplayId { get; init; }

    /// <summary>Index of the strip in <see cref="DisplayLayout.Strips"/>.</summary>
    public required int StripIndex { get; init; }

    /// <summary>
    /// The art slot: the strip's side slot for side bulbs, the corner slot for corners. Pass it to
    /// <see cref="IBulb.GetCell"/> and the sprite provider.
    /// </summary>
    public required CellSlot Slot { get; init; }

    /// <summary>
    /// Side bulbs: <c>i</c>, the position along the strip (0-based; in "All Displays Together" it continues across the
    /// pieces of one outline edge). Corners: 0 for the strip's start end (left), 1 for its far end (right).
    /// </summary>
    public required int Index { get; init; }

    /// <summary>The bulb id (<c>ids[i % n]</c> for side bulbs).</summary>
    public required string BulbId { get; init; }

    /// <summary>The raw flavor passed to <see cref="IBulb.GetCell"/>: <c>i / n</c> for side bulbs, 0 for corners (reduced modulo the flavor count by the bulb).</summary>
    public required int Flavor { get; init; }

    /// <summary>The cell rectangle in physical pixels, virtual-screen coordinates (the same space as <see cref="LayoutTarget.Area"/>).</summary>
    public required RectI Bounds { get; init; }

    /// <summary>
    /// The 5.4 chase counter <c>k</c>: 1 at the start of every strip, or in All Displays Together of every outline edge,
    /// continuing across that edge's pieces; +1 after each side bulb with a phase count above 1; null for corners.
    /// </summary>
    public int? ChaseIndex { get; init; }

    /// <summary>The ring this bulb belongs to (<see cref="LightsLayout.Rings"/>).</summary>
    public required int RingId { get; init; }

    /// <summary>Position in its ring's clockwise order (PRODUCT-SPEC 5.7 "ring order").</summary>
    public required int RingIndex { get; init; }

    /// <summary>True for corner bulbs.</summary>
    public bool IsCorner => Slot.IsCorner();
}

/// <summary>One strip (an edge, or in "All Displays Together" one piece of an outline edge on one display).</summary>
public sealed record StripLayout
{
    /// <summary>Index in <see cref="DisplayLayout.Strips"/> (strips are in build order: Top, Bottom, Right, Left pieces).</summary>
    public required int Index { get; init; }

    /// <summary>The display.</summary>
    public required string DisplayId { get; init; }

    /// <summary>The side the strip belongs to (by outward direction in "All Displays Together").</summary>
    public required Side Side { get; init; }

    /// <summary>The strip rectangle in physical pixels (virtual-screen coordinates).</summary>
    public required RectI Rect { get; init; }

    /// <summary>Strip thickness: the largest cell across the strip (height for horizontal strips, width for vertical ones).</summary>
    public required int Thickness { get; init; }

    /// <summary>Number of side bulbs (corners excluded).</summary>
    public required int Count { get; init; }

    /// <summary>The fractional gap after every side bulb (double precision, 5.4); <see cref="double.PositiveInfinity"/> when <see cref="Count"/> is 0.</summary>
    public required double Gap { get; init; }

    /// <summary>The placements in the 5.4 order of the golden layouts: corners first (start end, then far end), then side bulbs <c>i = 0..</c>.</summary>
    public required IReadOnlyList<BulbPlacement> Placements { get; init; }

    /// <summary>"All Displays Together": id of the outline edge this piece belongs to (pieces of one edge share the type/flavor index and the chase counter); null in "Each Display".</summary>
    public int? OutlineEdge { get; init; }
}

/// <summary>The layout of one display.</summary>
/// <param name="Target">The target that was laid out.</param>
/// <param name="Strips">The strips in build order.</param>
public sealed record DisplayLayout(LayoutTarget Target, IReadOnlyList<StripLayout> Strips);

/// <summary>A closed clockwise ring of bulbs (PRODUCT-SPEC 5.7): one per display in "Each Display", one per outline in "All Displays Together".</summary>
/// <param name="Id">Ring id (<see cref="BulbPlacement.RingId"/>).</param>
/// <param name="Ordinals">Placement ordinals in ring order: top-left corner, top strip left to right, top-right corner, right strip top to bottom, bottom-right corner, bottom strip right to left, bottom-left corner, left strip bottom to top (missing corners skipped).</param>
public sealed record LightsRing(int Id, IReadOnlyList<int> Ordinals);

/// <summary>The complete layout of every framed display: the input of flash sequencing, rendering and previews.</summary>
public sealed record LightsLayout
{
    /// <summary>An empty layout (no displays).</summary>
    public static LightsLayout Empty { get; } = new()
    {
        Mode = FrameMode.EachDisplay,
        Arrangement = SlotAssignment.Empty,
        Displays = [],
        Placements = [],
        Rings = [],
    };

    /// <summary>The frame mode that produced this layout.</summary>
    public required FrameMode Mode { get; init; }

    /// <summary>The arrangement that was laid out.</summary>
    public required SlotAssignment Arrangement { get; init; }

    /// <summary>One entry per laid-out target, in input order.</summary>
    public required IReadOnlyList<DisplayLayout> Displays { get; init; }

    /// <summary>Every placement of every display, indexed by <see cref="BulbPlacement.Ordinal"/>.</summary>
    public required IReadOnlyList<BulbPlacement> Placements { get; init; }

    /// <summary>The rings (clockwise orders) of the layout.</summary>
    public required IReadOnlyList<LightsRing> Rings { get; init; }
}

/// <summary>
/// The 5.4 layout engine, exact (golden <c>Golden/layout/*.json.gz</c>, PRODUCT-SPEC
/// 5.3), plus "All Displays Together" (5.2.4). Implemented by core-layout. Pure and thread-safe.
/// </summary>
/// <remarks>
/// Cell sizes come from <see cref="IBulb.GetCellSize"/> (phase 0) and spacings from <see cref="IBulb.HorizontalSpacing"/> /
/// <see cref="IBulb.VerticalSpacing"/>, all scaled with <see cref="ArtScale.Scale(int, double)"/> before the classic
/// integer algorithm runs in physical pixels. Ids that do not resolve are skipped (5.4 compacting). The chase index uses
/// <see cref="IBulb.GetPhaseCount"/>.
/// </remarks>
public interface ILayoutEngine
{
    /// <summary>Lays out an arrangement on one or more targets.</summary>
    /// <param name="targets">The areas to frame (enabled displays), in a stable order.</param>
    /// <param name="arrangement">The 8 boxes.</param>
    /// <param name="bulbs">Resolves bulb ids.</param>
    /// <param name="mode">Each Display, or All Displays Together.</param>
    /// <returns>The layout; placements have dense ordinals.</returns>
    LightsLayout Layout(IReadOnlyList<LayoutTarget> targets, SlotAssignment arrangement, IBulbResolver bulbs, FrameMode mode = FrameMode.EachDisplay);
}
