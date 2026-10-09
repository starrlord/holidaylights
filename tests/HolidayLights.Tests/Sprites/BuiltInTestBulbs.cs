using System.Text.Json;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Sprites;

/// <summary>
/// The 49 built-in bulbs for sprite tests, independent of core-bulbs: the sheets are the embedded RT_BITMAP files
/// (decoded with WPF), the cells and slot tables come from <c>Golden/builtin-cells.json</c>, and every cell is checked
/// against its golden RGBA hash when loaded.
/// </summary>
internal sealed class BuiltInTestBulbs : IBulbResolver
{
    /// <summary>The 15 built-in lit/unlit bulbs (PRODUCT-SPEC 5.4); frame 0 is lit.</summary>
    private static readonly HashSet<string> LightBulbSlugs =
    [
        "standard-bulbs", "indoor-bulbs", "mini-bulbs", "heavy-duty-bulbs", "gifts", "sweet-hearts", "old-glory-bulbs",
        "ghosts", "autumn-leaves", "cornucopias", "dead-turkeys", "chili-peppers", "paper-lanterns", "laundry", "religious-icons",
    ];

    private static readonly Lazy<BuiltInTestBulbs> Shared = new(Load);
    private readonly Dictionary<string, TestBuiltInBulb> bulbs;

    private BuiltInTestBulbs(Dictionary<string, TestBuiltInBulb> bulbs) => this.bulbs = bulbs;

    /// <summary>The bulbs, loaded once per test run.</summary>
    public static BuiltInTestBulbs Instance => Shared.Value;

    /// <summary>The bulbs by slug.</summary>
    public IEnumerable<TestBuiltInBulb> All => bulbs.Values;

    /// <summary>Returns a bulb by slug.</summary>
    public TestBuiltInBulb this[string slug] => bulbs[BulbIds.BuiltIn(slug)];

    /// <inheritdoc />
    public bool TryGetBulb(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IBulb? bulb)
    {
        bulb = bulbs.TryGetValue(id, out TestBuiltInBulb? found) ? found : null;
        return bulb is not null;
    }

    private static BuiltInTestBulbs Load()
    {
        using JsonDocument table = JsonDocument.Parse(EmbeddedAssets.ReadAllBytes(EmbeddedAssets.BuiltInTable));
        var records = table.RootElement.GetProperty("bulbs").EnumerateArray().ToDictionary(r => r.GetProperty("slug").GetString()!);
        using JsonDocument golden = GoldenData.ReadJson("builtin-cells.json");
        var result = new Dictionary<string, TestBuiltInBulb>(BulbIds.Comparer);
        foreach (JsonProperty entry in golden.RootElement.GetProperty("bulbs").EnumerateObject())
        {
            var bulb = new TestBuiltInBulb(entry.Name, entry.Value, records[entry.Name], LightBulbSlugs.Contains(entry.Name));
            result.Add(bulb.Id, bulb);
        }

        return new BuiltInTestBulbs(result);
    }
}

/// <summary>One built-in bulb (see <see cref="BuiltInTestBulbs"/>).</summary>
internal sealed class TestBuiltInBulb : IBulb
{
    private readonly int[][][] sides;
    private readonly int[][] corners;
    private readonly int previewCell;
    private readonly Dictionary<int, Rgba32Image> cells = [];
    private readonly bool isLightBulb;

    public TestBuiltInBulb(string slug, JsonElement golden, JsonElement record, bool isLightBulb)
    {
        Id = BulbIds.BuiltIn(slug);
        LegacyId = golden.GetProperty("id").GetInt32();
        Name = record.GetProperty("name").GetString()!;
        Description = record.GetProperty("description").GetString()!;
        Author = record.GetProperty("author").GetString()!;
        Copyright = record.GetProperty("copyright").GetString()!;
        Categories = [.. record.GetProperty("categories").EnumerateArray().Select(c => c.GetString()!)];
        HorizontalSpacing = record.GetProperty("hSpacing").GetInt32();
        VerticalSpacing = record.GetProperty("vSpacing").GetInt32();
        FlavorCount = golden.GetProperty("flavorCount").GetInt32();
        PhaseCount = golden.GetProperty("phaseCount").GetInt32();
        this.isLightBulb = isLightBulb;
        sides = [.. golden.GetProperty("sides").EnumerateArray().Select(side =>
            side.EnumerateArray().Select(flavor => flavor.EnumerateArray().Select(c => c.GetInt32()).ToArray()).ToArray())];
        corners = [.. golden.GetProperty("corners").EnumerateArray().Select(corner => corner.EnumerateArray().Select(c => c.GetInt32()).ToArray())];
        previewCell = golden.GetProperty("preview").GetInt32();

        Rgba32Image sheet = LoadSheet(golden.GetProperty("art").GetInt32(), golden.GetProperty("mask").GetInt32());
        foreach (JsonProperty cell in golden.GetProperty("cells").EnumerateObject())
        {
            int[] rect = [.. cell.Value.GetProperty("rect").EnumerateArray().Select(v => v.GetInt32())];
            Rgba32Image image = sheet.Crop(new RectI(rect[0], rect[1], rect[2], rect[3]));
            Assert.Equal(cell.Value.GetProperty("sha256").GetString(), GoldenData.RgbaSha256(image));
            cells.Add(int.Parse(cell.Name, System.Globalization.CultureInfo.InvariantCulture), image);
        }
    }

    public int FlavorCount { get; }

    public int PhaseCount { get; }

    /// <summary>Every distinct cell of the bulb.</summary>
    public IEnumerable<Rgba32Image> Cells => cells.Values;

    public string Id { get; }

    public int LegacyId { get; }

    public BulbOrigin Origin => BulbOrigin.BuiltIn;

    public string ContentKey => Id;

    public string Name { get; }

    public string Description { get; }

    public string Author { get; }

    public string Copyright { get; }

    public IReadOnlyList<string> Categories { get; }

    public string? FilePath => null;

    public bool IsLocked => true;

    public bool IsEditable => false;

    public bool HasDamagedArt => false;

    public int HorizontalSpacing { get; }

    public int VerticalSpacing { get; }

    public RectI PreviewWindow => new(0, 0, 32, 32);

    public int GetFlavorCount(Side side) => FlavorCount;

    public int GetPhaseCount(Side side) => PhaseCount;

    public BulbAnimationInfo GetAnimation(CellSlot slot, int flavor)
    {
        SizeI size = GetCellSize(slot, flavor, 0);
        return PhaseCount == 1
            ? new BulbAnimationInfo(BulbAnimationKind.Static, 1, 0, size)
            : new BulbAnimationInfo(isLightBulb ? BulbAnimationKind.LightBulb : BulbAnimationKind.Animation, PhaseCount, 0, size);
    }

    public SizeI GetCellSize(CellSlot slot, int flavor, int phase) => GetCell(slot, flavor, phase).Image.Size;

    public BulbCell GetCell(CellSlot slot, int flavor, int phase)
    {
        int frame = phase % PhaseCount;
        int cell = slot switch
        {
            CellSlot.Preview => previewCell,
            _ when slot.IsSide() => sides[(int)slot][flavor % FlavorCount][frame],
            _ => corners[(int)slot.ToCorner()][frame],
        };
        return new BulbCell(cells[cell], null, false);
    }

    private static Rgba32Image LoadSheet(int artId, int maskId)
    {
        Rgba32Image art;
        Rgba32Image mask;
        using (Stream stream = EmbeddedAssets.Open(EmbeddedAssets.BuiltInBitmap(artId)))
        {
            art = TestImages.Decode(stream);
        }

        using (Stream stream = EmbeddedAssets.Open(EmbeddedAssets.BuiltInBitmap(maskId)))
        {
            mask = TestImages.Decode(stream);
        }

        // Black mask pixels are opaque; a mask shorter than its sheet (Sun) leaves the rest transparent.
        var pixels = new uint[art.Width * art.Height];
        for (int y = 0; y < Math.Min(art.Height, mask.Height); y++)
        {
            for (int x = 0; x < Math.Min(art.Width, mask.Width); x++)
            {
                if ((mask[x, y] & 0x00FFFFFF) == 0)
                {
                    pixels[y * art.Width + x] = art[x, y] | 0xFF000000;
                }
            }
        }

        return new Rgba32Image(art.Width, art.Height, pixels);
    }
}
