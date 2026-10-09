using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace HolidayLights.Tests.Layout;

/// <summary>
/// Test stand-ins for the 49 built-in bulbs with the exact 5.4 geometry of <c>assets/builtin/table.json</c> (cell rectangles
/// from the sprite rows, <c>GetCellRect</c> modulo rules, spacings, phase and flavor counts), so layout and flash tests do
/// not depend on core-bulbs. Only geometry and animation facts are provided; pixels are not.
/// </summary>
internal static class TableBulbs
{
    // The 15 lit/unlit built-ins (PRODUCT-SPEC 5.4); frame 0 is lit.
    private static readonly HashSet<string> LightBulbSlugs = new(StringComparer.Ordinal)
    {
        "standard-bulbs", "indoor-bulbs", "mini-bulbs", "heavy-duty-bulbs", "gifts", "sweet-hearts", "old-glory-bulbs",
        "ghosts", "autumn-leaves", "cornucopias", "dead-turkeys", "chili-peppers", "paper-lanterns", "laundry",
        "religious-icons",
    };

    private static readonly Lazy<IReadOnlyList<TableBulb>> All = new(Load);

    /// <summary>A resolver over every built-in bulb.</summary>
    public static FakeBulbResolver Resolver => new(All.Value);

    /// <summary>The built-in bulb with a legacy (5.4 registry) id.</summary>
    /// <param name="legacyId">0-48.</param>
    /// <returns>The bulb.</returns>
    public static TableBulb ByLegacyId(int legacyId) => All.Value.Single(b => b.LegacyId == legacyId);

    private static IReadOnlyList<TableBulb> Load()
    {
        using Stream stream = EmbeddedAssets.Open(EmbeddedAssets.BuiltInTable);
        using JsonDocument document = JsonDocument.Parse(stream);
        return [.. document.RootElement.GetProperty("bulbs").EnumerateArray().Select(r => new TableBulb(r, LightBulbSlugs))];
    }
}

/// <summary>One built-in bulb's geometry from the table.</summary>
internal sealed class TableBulb : IBulb
{
    private readonly (int Count, int Width, int Height)[] rows;
    private readonly int[][] corners;
    private readonly int[][][] edges;
    private readonly int previewCell;
    private readonly bool isLightBulb;

    public TableBulb(JsonElement record, IReadOnlySet<string> lightBulbs)
    {
        string slug = record.GetProperty("slug").GetString()!;
        Id = BulbIds.BuiltIn(slug);
        LegacyId = record.GetProperty("id").GetInt32();
        Name = record.GetProperty("name").GetString()!;
        Flavors = record.GetProperty("flavorCount").GetInt32();
        Phases = record.GetProperty("phaseCount").GetInt32();
        HorizontalSpacing = record.GetProperty("hSpacing").GetInt32();
        VerticalSpacing = record.GetProperty("vSpacing").GetInt32();
        rows = [.. record.GetProperty("rows").EnumerateArray().Take(4).Select(r =>
            (r.GetProperty("count").GetInt32(), r.GetProperty("cellWidth").GetInt32(), r.GetProperty("rowHeight").GetInt32()))];
        corners = [.. record.GetProperty("cornerCells").EnumerateArray().Select(c => c.EnumerateArray().Select(v => v.GetInt32()).ToArray())];
        edges = [.. record.GetProperty("edgeCells").EnumerateArray().Select(side => side.EnumerateArray()
            .Select(flavor => flavor.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToArray())];
        previewCell = record.GetProperty("previewCell").GetInt32();
        isLightBulb = lightBulbs.Contains(slug);
    }

    public int Flavors { get; }

    public int Phases { get; }

    public string Id { get; }

    public int LegacyId { get; }

    public BulbOrigin Origin => BulbOrigin.BuiltIn;

    public string ContentKey => Id;

    public string Name { get; }

    public string Description => string.Empty;

    public string Author => string.Empty;

    public string Copyright => string.Empty;

    public IReadOnlyList<string> Categories => [];

    public string? FilePath => null;

    public bool IsLocked => true;

    public bool IsEditable => false;

    public bool HasDamagedArt => false;

    public int HorizontalSpacing { get; }

    public int VerticalSpacing { get; }

    public RectI PreviewWindow => new(0, 0, 32, 32);

    public int GetFlavorCount(Side side) => Flavors;

    public int GetPhaseCount(Side side) => Phases;

    public BulbAnimationInfo GetAnimation(CellSlot slot, int flavor)
    {
        BulbAnimationKind kind = isLightBulb ? BulbAnimationKind.LightBulb : Phases == 1 ? BulbAnimationKind.Static : BulbAnimationKind.Animation;
        return new BulbAnimationInfo(kind, Phases, 0, GetCellSize(slot, flavor, 0));
    }

    public SizeI GetCellSize(CellSlot slot, int flavor, int phase) => CellRect(slot, flavor, phase).Size;

    public BulbCell GetCell(CellSlot slot, int flavor, int phase) =>
        throw new NotSupportedException("Table bulbs carry geometry only.");

    /// <summary>The 5.4 <c>GetCellRect</c>: the cell of a slot, flavor and phase in the sprite sheet.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">Raw flavor.</param>
    /// <param name="phase">Raw phase.</param>
    /// <returns>The cell rectangle.</returns>
    public RectI CellRect(CellSlot slot, int flavor, int phase)
    {
        int cell = slot switch
        {
            CellSlot.Preview => previewCell,
            _ when slot.IsSide() => edges[(int)slot][flavor % Flavors][phase % Phases],
            _ => corners[(int)slot - 4][phase % Phases],
        };
        int index = cell - 1;
        int y = 0;
        foreach ((int count, int width, int height) in rows)
        {
            if (index < count)
            {
                return new RectI(index * width, y, (index + 1) * width, y + height);
            }

            y += height;
            index -= count;
        }

        throw new InvalidOperationException($"Cell {cell} of {Id} is out of range.");
    }
}

/// <summary>A synthetic bulb for layout and flash tests: one cell size per side and corner, configurable counts and kind.</summary>
internal sealed class FakeBulb : IBulb
{
    public FakeBulb(string id, int width, int height)
    {
        Id = id;
        SideCell = new SizeI(width, height);
        CornerCell = new SizeI(width, height);
    }

    public SizeI SideCell { get; init; }

    public SizeI CornerCell { get; init; }

    public int Phases { get; init; } = 2;

    public int Flavors { get; init; } = 1;

    public BulbAnimationKind? Kind { get; init; }

    public int LitFrame { get; init; }

    public string Id { get; }

    public int LegacyId => -1;

    public BulbOrigin Origin => BulbOrigin.BundledAddOn;

    public string ContentKey => Id;

    public string Name => Id;

    public string Description => string.Empty;

    public string Author => string.Empty;

    public string Copyright => string.Empty;

    public IReadOnlyList<string> Categories => [];

    public string? FilePath => null;

    public bool IsLocked => true;

    public bool IsEditable => false;

    public bool HasDamagedArt => false;

    public int HorizontalSpacing { get; init; }

    public int VerticalSpacing { get; init; }

    public RectI PreviewWindow => new(0, 0, 32, 32);

    public int GetFlavorCount(Side side) => Flavors;

    public int GetPhaseCount(Side side) => Phases;

    public BulbAnimationInfo GetAnimation(CellSlot slot, int flavor)
    {
        BulbAnimationKind kind = Kind ?? (Phases == 1 ? BulbAnimationKind.Static : BulbAnimationKind.Animation);
        return new BulbAnimationInfo(kind, Phases, kind == BulbAnimationKind.LightBulb ? LitFrame : 0, GetCellSize(slot, flavor, 0));
    }

    public SizeI GetCellSize(CellSlot slot, int flavor, int phase) => slot.IsCorner() ? CornerCell : SideCell;

    public BulbCell GetCell(CellSlot slot, int flavor, int phase) =>
        throw new NotSupportedException("Fake bulbs carry geometry only.");
}

/// <summary>An <see cref="IBulbResolver"/> over a fixed set of bulbs.</summary>
internal sealed class FakeBulbResolver : IBulbResolver
{
    private readonly Dictionary<string, IBulb> bulbs = new(BulbIds.Comparer);

    public FakeBulbResolver(IEnumerable<IBulb> bulbs)
    {
        foreach (IBulb bulb in bulbs)
        {
            this.bulbs[bulb.Id] = bulb;
        }
    }

    public bool TryGetBulb(string id, [NotNullWhen(true)] out IBulb? bulb) => bulbs.TryGetValue(id, out bulb);
}
