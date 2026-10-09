namespace HolidayLights.Core.Layout;

/// <summary>
/// The usable bulb types of one edge measured at one scale: the 5.4 compacted id list (5.4 drops ids that
/// cannot be used and keeps the order), with every cell size and spacing scaled by <see cref="ArtScale.Scale(int, double)"/>.
/// </summary>
internal sealed class EdgeBulbs
{
    private readonly EdgeBulbType[] types;

    private EdgeBulbs(Side side, EdgeBulbType[] types)
    {
        Side = side;
        this.types = types;
        MaxThickness = types.Length == 0 ? 0 : types.Max(t => t.MaxThickness);
    }

    /// <summary>The edge.</summary>
    public Side Side { get; }

    /// <summary>n: the number of usable types (0-6).</summary>
    public int TypeCount => types.Length;

    /// <summary>The largest thickness any cell of this edge can give its strip (height on top/bottom, width on left/right).</summary>
    public int MaxThickness { get; }

    /// <summary>Resolves an edge and measures its bulbs.</summary>
    /// <param name="side">The edge.</param>
    /// <param name="ids">The arrangement's ids for the edge.</param>
    /// <param name="bulbs">Resolves ids; unknown ids are skipped (5.4 compacting).</param>
    /// <param name="scale">S.</param>
    /// <returns>The measured edge.</returns>
    public static EdgeBulbs Create(Side side, IReadOnlyList<string> ids, IBulbResolver bulbs, double scale)
    {
        var usable = new List<EdgeBulbType>(ids.Count);
        foreach (string id in ids)
        {
            if (!string.IsNullOrEmpty(id) && bulbs.TryGetBulb(id, out IBulb? bulb))
            {
                usable.Add(new EdgeBulbType(id, bulb, side, scale));
            }
        }

        return new EdgeBulbs(side, [.. usable]);
    }

    /// <summary>The type of bulb <c>i</c>: <c>ids[i % n]</c>.</summary>
    /// <param name="index">The position <c>i</c> along the edge.</param>
    /// <returns>The type.</returns>
    public EdgeBulbType TypeAt(int index) => types[index % types.Length];

    /// <summary>The raw flavor of bulb <c>i</c>: <c>i / n</c>.</summary>
    /// <param name="index">The position <c>i</c> along the edge.</param>
    /// <returns>The flavor passed to <see cref="IBulb.GetCellSize"/>.</returns>
    public int FlavorAt(int index) => index / types.Length;

    /// <summary>The length of a cell along the edge (width on top/bottom, height on left/right).</summary>
    /// <param name="cell">A scaled cell size.</param>
    /// <returns>The length.</returns>
    public int LengthOf(SizeI cell) => Side.IsHorizontal() ? cell.Width : cell.Height;

    /// <summary>The extent of a cell across the edge (height on top/bottom, width on left/right).</summary>
    /// <param name="cell">A scaled cell size.</param>
    /// <returns>The extent.</returns>
    public int ThicknessOf(SizeI cell) => Side.IsHorizontal() ? cell.Height : cell.Width;
}

/// <summary>One usable bulb type of an edge, measured at one scale.</summary>
internal sealed class EdgeBulbType
{
    private readonly SizeI[] cells;

    /// <summary>Measures a bulb for an edge.</summary>
    /// <param name="id">The arrangement id.</param>
    /// <param name="bulb">The bulb.</param>
    /// <param name="side">The edge.</param>
    /// <param name="scale">S.</param>
    public EdgeBulbType(string id, IBulb bulb, Side side, double scale)
    {
        Id = id;
        PhaseCount = bulb.GetPhaseCount(side);
        int spacing = side.IsHorizontal() ? bulb.HorizontalSpacing : bulb.VerticalSpacing;
        Spacing = ArtScale.Scale(Math.Max(0, spacing), scale);

        // 5.4 lays out with phase 0; every frame of an animation has the size of frame 0 (built-in cells and GIF frames).
        cells = new SizeI[Math.Max(1, bulb.GetFlavorCount(side))];
        for (int flavor = 0; flavor < cells.Length; flavor++)
        {
            SizeI art = bulb.GetCellSize(side.ToSlot(), flavor, 0);
            cells[flavor] = ArtScale.Scale(new SizeI(Math.Max(0, art.Width), Math.Max(0, art.Height)), scale);
        }

        MaxThickness = cells.Max(c => side.IsHorizontal() ? c.Height : c.Width);
    }

    /// <summary>The arrangement id (the placement's <see cref="BulbPlacement.BulbId"/>).</summary>
    public string Id { get; }

    /// <summary>The 5.4 phase count of the bulb on this edge (drives the chase counter).</summary>
    public int PhaseCount { get; }

    /// <summary>The scaled spacing before and after every cell.</summary>
    public int Spacing { get; }

    /// <summary>The largest cross-edge extent over the flavors.</summary>
    public int MaxThickness { get; }

    /// <summary>The scaled cell size of a raw flavor (taken modulo the flavor count, 5.4 <c>GetCellRect</c>).</summary>
    /// <param name="flavor">Raw flavor.</param>
    /// <returns>The size in physical pixels.</returns>
    public SizeI CellSize(int flavor) => cells[flavor % cells.Length];
}

/// <summary>The bulb of one corner, measured at one scale.</summary>
/// <param name="Id">The arrangement id.</param>
/// <param name="Slot">The corner slot.</param>
/// <param name="Size">The scaled cell size (phase 0).</param>
/// <param name="Spacing">The scaled horizontal spacing (5.4 corners use <c>HSpacing</c>).</param>
internal sealed record CornerBulb(string Id, CellSlot Slot, SizeI Size, int Spacing)
{
    /// <summary>The length a corner reserves on its strip: <c>cellWidth + hSpacing</c>.</summary>
    public int Reserved => Size.Width + Spacing;

    /// <summary>Resolves and measures the bulb of a corner.</summary>
    /// <param name="id">The arrangement id, or null for an empty corner.</param>
    /// <param name="corner">The corner.</param>
    /// <param name="bulbs">Resolves ids.</param>
    /// <param name="scale">S.</param>
    /// <returns>The measured corner, or null when the corner is empty or its id does not resolve.</returns>
    public static CornerBulb? Create(string? id, Corner corner, IBulbResolver bulbs, double scale)
    {
        if (string.IsNullOrEmpty(id) || !bulbs.TryGetBulb(id, out IBulb? bulb))
        {
            return null;
        }

        CellSlot slot = corner.ToSlot();
        SizeI art = bulb.GetCellSize(slot, 0, 0);
        return new CornerBulb(
            id,
            slot,
            ArtScale.Scale(new SizeI(Math.Max(0, art.Width), Math.Max(0, art.Height)), scale),
            ArtScale.Scale(Math.Max(0, bulb.HorizontalSpacing), scale));
    }
}
