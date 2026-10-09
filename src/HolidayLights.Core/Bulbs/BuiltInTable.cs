using System.Text.Json;
using System.Text.Json.Serialization;

namespace HolidayLights.Core.Bulbs;

/// <summary>The embedded built-in bulb table (<c>assets/builtin/table.json</c>, schema <c>holidaylights.builtin-table/1</c>).</summary>
internal sealed class BuiltInTable
{
    /// <summary>The 49 records in table order.</summary>
    public IReadOnlyList<BuiltInRecord> Bulbs { get; init; } = [];

    /// <summary>Reads the embedded table.</summary>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidDataException">The embedded table is unreadable.</exception>
    public static BuiltInTable Load()
    {
        byte[] json = EmbeddedAssets.ReadAllBytes(EmbeddedAssets.BuiltInTable);
        BuiltInTable? table;
        try
        {
            table = JsonSerializer.Deserialize(json, BuiltInTableJsonContext.Default.BuiltInTable);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("The built-in bulb table is unreadable.", e);
        }

        return table is { Bulbs.Count: > 0 } ? table : throw new InvalidDataException("The built-in bulb table is empty.");
    }
}

/// <summary>One record of the 0x3C8-byte 5.4 table.</summary>
internal sealed class BuiltInRecord
{
    /// <summary>Position in the table (0-48).</summary>
    public int TableIndex { get; init; }

    /// <summary>The 5.4 bulb id (record +0x00; not the table index).</summary>
    public int Id { get; init; }

    /// <summary>The stable slug (<c>builtin:&lt;slug&gt;</c>).</summary>
    public string Slug { get; init; } = "";

    /// <summary>RT_BITMAP id of the art.</summary>
    public int ArtBitmapId { get; init; }

    /// <summary>RT_BITMAP id of the 1 bpp mask.</summary>
    public int MaskBitmapId { get; init; }

    /// <summary>Cell of the bulb-list preview (slot 8).</summary>
    public int PreviewCell { get; init; }

    /// <summary>Flavors per side (F).</summary>
    public int FlavorCount { get; init; }

    /// <summary>Frames per animation (N).</summary>
    public int PhaseCount { get; init; }

    /// <summary>Spacing on the top and bottom strips and for corners.</summary>
    public int HSpacing { get; init; }

    /// <summary>Spacing on the left and right strips.</summary>
    public int VSpacing { get; init; }

    /// <summary>The 8 cell rows; rows 0-3 are the normal art.</summary>
    public IReadOnlyList<BuiltInRow> Rows { get; init; } = [];

    /// <summary>Corner cells [corner TL, TR, BR, BL][frame].</summary>
    public IReadOnlyList<IReadOnlyList<int>> CornerCells { get; init; } = [];

    /// <summary>Edge cells [side top, right, bottom, left][flavor][frame].</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> EdgeCells { get; init; } = [];

    /// <summary>Name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Description.</summary>
    public string Description { get; init; } = "";

    /// <summary>Copyright.</summary>
    public string Copyright { get; init; } = "";

    /// <summary>Author.</summary>
    public string Author { get; init; } = "";

    /// <summary>Categories (split on '|', trimmed, empties dropped).</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];
}

/// <summary>One row of cells in a built-in sheet.</summary>
internal sealed class BuiltInRow
{
    /// <summary>Cells in the row.</summary>
    public int Count { get; init; }

    /// <summary>Width of each cell.</summary>
    public int CellWidth { get; init; }

    /// <summary>Height of the row.</summary>
    public int RowHeight { get; init; }
}

/// <summary>Source-generated metadata for the built-in table.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BuiltInTable))]
internal sealed partial class BuiltInTableJsonContext : JsonSerializerContext;
