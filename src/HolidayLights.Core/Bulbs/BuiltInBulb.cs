using System.Collections.Concurrent;
using HolidayLights.Core.Imaging;

namespace HolidayLights.Core.Bulbs;

/// <summary>
/// One of the 49 built-in bulbs: a record of the 5.4 table and its RT_BITMAP sheet, sliced exactly like
/// <c>GetCellRect</c> / <c>CellIndexToRect</c>.
/// </summary>
/// <remarks>Immutable and thread-safe: the sheet decodes on first use, cells are cut once and kept.</remarks>
internal sealed class BuiltInBulb : IBulb
{
    /// <summary>The 15 lit/unlit bulbs of PRODUCT-SPEC 5.4: frame 0 is lit, frame 1 unlit.</summary>
    private static readonly HashSet<string> LightBulbSlugs = new(StringComparer.Ordinal)
    {
        "standard-bulbs", "indoor-bulbs", "mini-bulbs", "heavy-duty-bulbs", "gifts", "sweet-hearts", "old-glory-bulbs",
        "ghosts", "autumn-leaves", "cornucopias", "dead-turkeys", "chili-peppers", "paper-lanterns", "laundry",
        "religious-icons",
    };

    private const int NormalRows = 4;

    private readonly BuiltInRecord record;
    private readonly Lazy<Rgba32Image> sheet;
    private readonly ConcurrentDictionary<int, BulbCell> cells = new();
    private readonly BulbAnimationKind kind;

    /// <summary>Creates the bulb.</summary>
    /// <param name="record">Its table record.</param>
    public BuiltInBulb(BuiltInRecord record)
    {
        this.record = record;
        Id = BulbIds.BuiltIn(record.Slug);
        Categories = [.. record.Categories.Where(c => c is not ("_0" or "_1"))];
        sheet = new Lazy<Rgba32Image>(() => BmpDecoder.DecodeMasked(
            EmbeddedAssets.ReadAllBytes(EmbeddedAssets.BuiltInBitmap(record.ArtBitmapId)),
            EmbeddedAssets.ReadAllBytes(EmbeddedAssets.BuiltInBitmap(record.MaskBitmapId))));
        kind = record.PhaseCount <= 1 ? BulbAnimationKind.Static
            : LightBulbSlugs.Contains(record.Slug) ? BulbAnimationKind.LightBulb
            : BulbAnimationKind.Animation;
        SizeI preview = CellSize(record.PreviewCell);
        int x = preview.Width >= 32 ? preview.Width / 2 - 16 : 0;
        int y = preview.Height >= 32 ? preview.Height / 2 - 16 : 0;
        PreviewWindow = RectI.FromXYWH(x, y, Math.Min(preview.Width, 32), Math.Min(preview.Height, 32));
    }

    /// <summary>The position of the record in the 5.4 table (the "Original Order" of built-ins).</summary>
    public int TableIndex => record.TableIndex;

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public int LegacyId => record.Id;

    /// <inheritdoc />
    public BulbOrigin Origin => BulbOrigin.BuiltIn;

    /// <inheritdoc />
    public string ContentKey => Id;

    /// <inheritdoc />
    public string Name => record.Name;

    /// <inheritdoc />
    public string Description => record.Description;

    /// <inheritdoc />
    public string Author => record.Author;

    /// <inheritdoc />
    public string Copyright => record.Copyright;

    /// <inheritdoc />
    public IReadOnlyList<string> Categories { get; }

    /// <inheritdoc />
    public string? FilePath => null;

    /// <inheritdoc />
    public bool IsLocked => true;

    /// <inheritdoc />
    public bool IsEditable => false;

    /// <inheritdoc />
    public bool HasDamagedArt => false;

    /// <inheritdoc />
    public int HorizontalSpacing => record.HSpacing;

    /// <inheritdoc />
    public int VerticalSpacing => record.VSpacing;

    /// <inheritdoc />
    public RectI PreviewWindow { get; }

    /// <inheritdoc />
    public int GetFlavorCount(Side side) => record.FlavorCount;

    /// <inheritdoc />
    public int GetPhaseCount(Side side) => record.PhaseCount;

    /// <inheritdoc />
    public BulbAnimationInfo GetAnimation(CellSlot slot, int flavor) =>
        slot == CellSlot.Preview
            ? new BulbAnimationInfo(BulbAnimationKind.Static, 1, 0, CellSize(record.PreviewCell))
            : new BulbAnimationInfo(kind, record.PhaseCount, 0, CellSize(CellNumber(slot, flavor, 0)));

    /// <inheritdoc />
    public SizeI GetCellSize(CellSlot slot, int flavor, int phase) => CellSize(CellNumber(slot, flavor, phase));

    /// <inheritdoc />
    public BulbCell GetCell(CellSlot slot, int flavor, int phase) => cells.GetOrAdd(CellNumber(slot, flavor, phase), CutCell);

    /// <summary>A cell of the sheet by its 1-based number, independently of the edge tables (<see cref="BuiltInBulbs.GetSheetCell"/>).</summary>
    /// <param name="cell">The 1-based cell number.</param>
    /// <returns>The cell, or null where 5.4 throws (cell 0, or past the last normal row).</returns>
    public BulbCell? GetSheetCell(int cell) => CellRect(cell) is null ? null : cells.GetOrAdd(cell, CutCell);

    /// <summary>The 1-based cell a slot shows: flavor modulo F, frame modulo N.</summary>
    private int CellNumber(CellSlot slot, int flavor, int phase)
    {
        if (slot == CellSlot.Preview)
        {
            return record.PreviewCell;
        }

        int frame = Modulo(phase, record.PhaseCount);
        return slot.IsCorner()
            ? record.CornerCells[(int)slot.ToCorner()][frame]
            : record.EdgeCells[(int)slot.ToSide()][Modulo(flavor, record.FlavorCount)][frame];
    }

    /// <summary>
    /// <c>CellIndexToRect</c> for the normal rows: cell k (1-based) is found by walking the rows stacked from y = 0, x
    /// restarting at 0 in every row. Null where the original throws (cell 0, or past the last row).
    /// </summary>
    private RectI? CellRect(int cell)
    {
        if (cell <= 0)
        {
            return null;
        }

        int k = cell - 1;
        int y = 0;
        for (int row = 0; row < NormalRows && row < record.Rows.Count; row++)
        {
            BuiltInRow r = record.Rows[row];
            if (k < r.Count)
            {
                return RectI.FromXYWH(k * r.CellWidth, y, r.CellWidth, r.RowHeight);
            }

            y += r.RowHeight;
            k -= r.Count;
        }

        return null;
    }

    private SizeI CellSize(int cell) => CellRect(cell)?.Size ?? HeritageArt.Warning.Size;

    private BulbCell CutCell(int cell)
    {
        if (CellRect(cell) is not { } rect)
        {
            return new BulbCell(HeritageArt.Warning, null, true);
        }

        return new BulbCell(CropClipped(sheet.Value, rect), rect, false);
    }

    /// <summary>Copies a rectangle; pixels outside the sheet stay transparent (as in the reference exporter).</summary>
    private static Rgba32Image CropClipped(Rgba32Image source, RectI rect)
    {
        var image = new Rgba32Image(rect.Width, rect.Height);
        RectI inside = rect.Intersect(new RectI(0, 0, source.Width, source.Height));
        for (int y = inside.Top; y < inside.Bottom; y++)
        {
            source.Pixels.AsSpan(y * source.Width + inside.Left, inside.Width)
                .CopyTo(image.Pixels.AsSpan((y - rect.Top) * rect.Width + inside.Left - rect.Left));
        }

        return image;
    }

    private static int Modulo(int value, int count) => count <= 0 ? 0 : ((value % count) + count) % count;
}
