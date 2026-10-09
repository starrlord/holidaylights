namespace HolidayLights.Core.Abstractions;

/// <summary>How the frames of one bulb animation behave (PRODUCT-SPEC 5.4).</summary>
public enum BulbAnimationKind
{
    /// <summary>One frame; never changes (Jolly Holly, Snow Family, Spring Flowers, spacers).</summary>
    Static,

    /// <summary>
    /// A lit/unlit pair: it glows, fades and takes brightness in the new patterns. Built-in: the 15 lit/unlit bulbs
    /// (frame 0 = lit). Add-on: exactly 2 frames with identical masks where the brighter frame's mean luma exceeds the
    /// other's by at least 12 % and at least 5 % of opaque pixels differ in luma by 32 or more (the brighter is lit).
    /// </summary>
    LightBulb,

    /// <summary>Two or more frames that are not a light bulb; frames switch instantly, as in 2003.</summary>
    Animation,
}

/// <summary>Facts about one animation of a bulb (one side and flavor, one corner, or the preview).</summary>
/// <param name="Kind">Static, light bulb or animation.</param>
/// <param name="FrameCount">Number of frames (1 or more). The frame shown for phase <c>p</c> is <c>p % FrameCount</c>.</param>
/// <param name="LitFrame">For <see cref="BulbAnimationKind.LightBulb"/>: the lit frame (0 or 1; the other one is unlit). 0 otherwise.</param>
/// <param name="CellSize">Size of frame 0 in art pixels (unscaled). Layout uses this size (5.4 lays out with phase 0).</param>
public sealed record BulbAnimationInfo(BulbAnimationKind Kind, int FrameCount, int LitFrame, SizeI CellSize)
{
    /// <summary>The unlit frame of a light bulb (<c>1 - LitFrame</c>); equals <see cref="LitFrame"/> for other kinds.</summary>
    public int UnlitFrame => Kind == BulbAnimationKind.LightBulb ? 1 - LitFrame : LitFrame;
}

/// <summary>One decoded cell (frame) of a bulb.</summary>
/// <param name="Image">
/// The picture in art pixels with straight alpha; alpha is 0 or 255 (5.4 masks, black = opaque) and transparent pixels
/// have colour 0. Callers must not modify it (it may be cached and shared).
/// </param>
/// <param name="SourceRect">Built-in bulbs: the cell rectangle in the RT_BITMAP sheet (golden <c>builtin-cells.json</c>). Null for add-ons.</param>
/// <param name="IsPlaceholder">True when the art could not be decoded and the 5.4 <c>WARNING</c> picture (32 x 32) stands in.</param>
public sealed record BulbCell(Rgba32Image Image, RectI? SourceRect, bool IsPlaceholder);

/// <summary>
/// One kind of decoration ("bulb"): a built-in bulb or an add-on <c>.bul</c> file, with metadata and lazy cell access.
/// </summary>
/// <remarks>
/// <para>Implemented by core-bulbs (<c>src/HolidayLights.Core/Bulbs/</c>). Instances are immutable snapshots: when a file
/// changes (Bulb Editing saved it, or it was replaced on disk) the catalog creates a new instance with a new
/// <see cref="ContentKey"/>.</para>
/// <para>Thread safety: every member may be called from any thread (UI, Lights, thread pool). Cell decoding is lazy and
/// cached.</para>
/// <para>Indexing rules are the 5.4 <c>GetCellRect</c> rules: for sides, flavor is taken modulo
/// <see cref="GetFlavorCount"/>; for corners and the preview, flavor is ignored; the frame shown for a phase is
/// <c>phase % FrameCount</c> of that animation.</para>
/// </remarks>
public interface IBulb
{
    /// <summary>Stable id (<see cref="BulbIds"/>).</summary>
    string Id { get; }

    /// <summary>
    /// The 5.4 numeric id: built-in record id 0-48 (not the table index), or the <c>.bul</c> header id at offset 0x0C as
    /// stored in the file (before any collision bump). Used only by the 5.4 importer and golden tests.
    /// </summary>
    int LegacyId { get; }

    /// <summary>Where the bulb comes from.</summary>
    BulbOrigin Origin { get; }

    /// <summary>
    /// Identifies the art content for caches: equal keys mean identical pixels. Built-ins: the id; files: the id plus the
    /// file size and last-write time (or a content hash). Transient bulbs (Bulb Editing) use a unique key per revision.
    /// </summary>
    string ContentKey { get; }

    /// <summary>Bulb name (Windows-1252 decoded), shown in the Bulb List.</summary>
    string Name { get; }

    /// <summary>Description, verbatim.</summary>
    string Description { get; }

    /// <summary>Author field, verbatim (may contain a line break: "Name\r\nE-mail").</summary>
    string Author { get; }

    /// <summary>Copyright field, verbatim.</summary>
    string Copyright { get; }

    /// <summary>
    /// Categories stored with the bulb (built-in table or the <c>categ:</c> record), split on '|', trimmed, empties and the
    /// internal <c>_0</c>/<c>_1</c> removed. User overrides are applied by the catalog (<see cref="BulbInfo.Categories"/>).
    /// </summary>
    IReadOnlyList<string> Categories { get; }

    /// <summary>Full path of the <c>.bul</c> file; null for built-in bulbs.</summary>
    string? FilePath { get; }

    /// <summary>True for built-ins and for add-ons whose header <c>locked</c> flag is non-zero (all 1,501 bundled bulbs).</summary>
    bool IsLocked { get; }

    /// <summary>True when "Edit Bulb..." opens Bulb Editing: a My Bulbs file with <c>locked</c> = 0 (5.4 rule). Otherwise "Edit Categories...".</summary>
    bool IsEditable { get; }

    /// <summary>True when at least one animation could not be decoded (it is drawn as the 5.4 WARNING picture).</summary>
    bool HasDamagedArt { get; }

    /// <summary>Spacing in art pixels added before and after every cell on the top and bottom strips and for corners (built-in table <c>hSpacing</c>; add-ons 0).</summary>
    int HorizontalSpacing { get; }

    /// <summary>Spacing in art pixels added before and after every cell on the left and right strips (built-in table <c>vSpacing</c>; add-ons 0).</summary>
    int VerticalSpacing { get; }

    /// <summary>
    /// The 32 x 32 window of the preview cell that the Details view and the 5.4 list show, in preview-cell coordinates
    /// (add-ons: header <c>previewX</c>/<c>previewY</c>; built-ins: the preview cell, centred and clipped to 32 x 32).
    /// </summary>
    RectI PreviewWindow { get; }

    /// <summary>Number of flavors on a side (built-in <c>F</c>; add-ons: entries before the first -1 of that side).</summary>
    /// <param name="side">The side.</param>
    /// <returns>1 or more.</returns>
    int GetFlavorCount(Side side);

    /// <summary>
    /// The 5.4 <c>GetPhaseCount(orientation)</c>: built-ins <c>N</c>; add-ons the largest frame count over that side's
    /// flavors. It sets a strip's frame count and the chase counter.
    /// </summary>
    /// <param name="side">The side.</param>
    /// <returns>1 or more.</returns>
    int GetPhaseCount(Side side);

    /// <summary>Returns the facts of one animation. May decode the first two frames to classify an add-on light bulb (cached).</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">Raw flavor index (reduced modulo the flavor count for sides; ignored otherwise).</param>
    /// <returns>Kind, frame count, lit frame and the size of frame 0.</returns>
    BulbAnimationInfo GetAnimation(CellSlot slot, int flavor);

    /// <summary>Returns the size of one cell in art pixels without decoding pixels.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">Raw flavor index.</param>
    /// <param name="phase">Raw phase; the frame is <c>phase % FrameCount</c>.</param>
    /// <returns>Width and height in art pixels.</returns>
    SizeI GetCellSize(CellSlot slot, int flavor, int phase);

    /// <summary>Returns one decoded cell (lazy, cached, thread-safe). Damaged art yields the WARNING placeholder.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">Raw flavor index.</param>
    /// <param name="phase">Raw phase; the frame is <c>phase % FrameCount</c>.</param>
    /// <returns>The cell.</returns>
    BulbCell GetCell(CellSlot slot, int flavor, int phase);
}

/// <summary>Looks up bulbs by id. Implemented by the catalog; layout, flash and sprite code depend only on this.</summary>
public interface IBulbResolver
{
    /// <summary>Finds a bulb, including hidden (removed bundled) bulbs that arrangements and themes still reference.</summary>
    /// <param name="id">A bulb id (compared with <see cref="BulbIds.Comparer"/>).</param>
    /// <param name="bulb">The bulb when found.</param>
    /// <returns>False when the id is unknown or its file is missing or damaged (such bulbs are skipped on screen).</returns>
    bool TryGetBulb(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBulb? bulb);
}
