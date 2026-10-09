using System.Text.Json;
using HolidayLights.Core.Bulbs;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

public sealed class BuiltInBulbsTests
{
    [Fact]
    public void Load_Returns49BulbsInTableOrderWithRecordIds()
    {
        IReadOnlyList<IBulb> bulbs = BuiltInBulbs.Load();

        Assert.Equal(49, bulbs.Count);
        Assert.Same(bulbs, BuiltInBulbs.Load());
        Assert.Equal("builtin:standard-bulbs", bulbs[0].Id);
        Assert.Equal("builtin:50-pixel-spacer", bulbs[48].Id);
        // Table index 13 is North Pole Express with record id 47.
        Assert.Equal("builtin:north-pole-express", bulbs[13].Id);
        Assert.Equal(47, bulbs[13].LegacyId);
        Assert.Equal(Enumerable.Range(0, 49).ToHashSet(), bulbs.Select(b => b.LegacyId).ToHashSet());
        foreach ((IBulb bulb, int index) in bulbs.Select((b, i) => (b, i)))
        {
            JsonElement golden = BulbGoldens.BuiltInCells.GetProperty(BulbIds.GetKey(bulb.Id));
            Assert.Equal(index, golden.GetProperty("tableIndex").GetInt32());
            Assert.Equal(golden.GetProperty("id").GetInt32(), bulb.LegacyId);
        }
    }

    [Fact]
    public void EveryReferencedCell_MatchesTheGoldenRectAndPixels()
    {
        int checkedSlots = 0;
        foreach (JsonProperty property in BulbGoldens.BuiltInCells.EnumerateObject())
        {
            Assert.True(BuiltInBulbs.TryGet(BulbIds.BuiltIn(property.Name), out BuiltInBulb? bulb), property.Name);
            JsonElement golden = property.Value;
            JsonElement cells = golden.GetProperty("cells");
            int flavors = golden.GetProperty("flavorCount").GetInt32();
            int phases = golden.GetProperty("phaseCount").GetInt32();
            foreach (Side side in CellSlots.Sides)
            {
                Assert.Equal(flavors, bulb.GetFlavorCount(side));
                Assert.Equal(phases, bulb.GetPhaseCount(side));
                JsonElement sideCells = golden.GetProperty("sides")[(int)side];
                for (int flavor = 0; flavor < flavors; flavor++)
                {
                    int[] frames = BulbGoldens.Ints(sideCells[flavor]);
                    for (int phase = 0; phase < phases; phase++)
                    {
                        AssertCell(bulb, side.ToSlot(), flavor, phase, cells.GetProperty(frames[phase].ToString()));
                        checkedSlots++;
                    }
                }
            }

            foreach (Corner corner in CellSlots.Corners)
            {
                int[] frames = BulbGoldens.Ints(golden.GetProperty("corners")[(int)corner]);
                for (int phase = 0; phase < phases; phase++)
                {
                    AssertCell(bulb, corner.ToSlot(), 0, phase, cells.GetProperty(frames[phase].ToString()));
                    checkedSlots++;
                }
            }

            AssertCell(bulb, CellSlot.Preview, 0, 0, cells.GetProperty(golden.GetProperty("preview").GetInt32().ToString()));
            checkedSlots++;
        }

        Assert.Equal(3133, checkedSlots);
    }

    [Fact]
    public void Indexing_WrapsFlavorsAndPhasesLikeGetCellRect()
    {
        Assert.True(BuiltInBulbs.TryGet("builtin:standard-bulbs", out BuiltInBulb? bulb));

        // 5 flavors x 2 frames: flavor 7 is flavor 2, phase 5 is frame 1, negative values wrap too.
        Assert.Equal(bulb.GetCell(CellSlot.Top, 2, 1).SourceRect, bulb.GetCell(CellSlot.Top, 7, 5).SourceRect);
        Assert.Equal(bulb.GetCell(CellSlot.Top, 4, 1).SourceRect, bulb.GetCell(CellSlot.Top, -1, -1).SourceRect);
        // Corners ignore the flavor.
        Assert.Equal(bulb.GetCell(CellSlot.TopLeft, 0, 1).SourceRect, bulb.GetCell(CellSlot.TopLeft, 3, 1).SourceRect);
        Assert.Same(bulb.GetCell(CellSlot.Bottom, 1, 0).Image, bulb.GetCell(CellSlot.Bottom, 6, 2).Image);
    }

    [Fact]
    public void Animations_FollowTheBulbKindsOfTheSpec()
    {
        IBulb standard = Bulb("standard-bulbs");
        Assert.Equal(new BulbAnimationInfo(BulbAnimationKind.LightBulb, 2, 0, new SizeI(32, 32)), standard.GetAnimation(CellSlot.Top, 0));
        Assert.Equal(BulbAnimationKind.LightBulb, standard.GetAnimation(CellSlot.BottomRight, 0).Kind);
        Assert.Equal(BulbAnimationKind.Static, Bulb("jolly-holly").GetAnimation(CellSlot.Left, 1).Kind);
        Assert.Equal(BulbAnimationKind.Animation, Bulb("north-pole-express").GetAnimation(CellSlot.Top, 0).Kind);
        Assert.Equal(BulbAnimationKind.LightBulb, Bulb("religious-icons").GetAnimation(CellSlot.Right, 3).Kind);
        Assert.Equal(BulbAnimationKind.Static, standard.GetAnimation(CellSlot.Preview, 0).Kind);

        int lightBulbs = BuiltInBulbs.Load().Count(b => b.GetAnimation(CellSlot.Top, 0).Kind == BulbAnimationKind.LightBulb);
        Assert.Equal(15, lightBulbs);
    }

    [Fact]
    public void Metadata_ComesFromTheTable()
    {
        IBulb standard = Bulb("standard-bulbs");

        Assert.Equal("Standard Bulbs", standard.Name);
        Assert.Equal("Standard lights, without the tangled cord or burned-out bulbs!", standard.Description);
        Assert.Equal("Joe Lachoff", standard.Author);
        Assert.Equal("Copyright " + (char)0xA9 + " 1994-2003 Tiger Technologies.", standard.Copyright);
        Assert.Equal(new[] { "Christmas", "Light Bulbs" }, standard.Categories);
        Assert.Equal(standard.Id, standard.ContentKey);
        Assert.Equal(BulbOrigin.BuiltIn, standard.Origin);
        Assert.True(standard.IsLocked);
        Assert.False(standard.IsEditable);
        Assert.False(standard.HasDamagedArt);
        Assert.Null(standard.FilePath);
        Assert.Equal(new RectI(0, 0, 32, 32), standard.PreviewWindow);
        Assert.Empty(Bulb("candy-canes").Categories);
        Assert.Equal((8, 8), (Bulb("chili-peppers").HorizontalSpacing, Bulb("chili-peppers").VerticalSpacing));
        Assert.Equal((2, 2), (Bulb("snow-family").HorizontalSpacing, Bulb("snow-family").VerticalSpacing));
    }

    private static void AssertCell(BuiltInBulb bulb, CellSlot slot, int flavor, int phase, JsonElement golden)
    {
        int[] rect = BulbGoldens.Ints(golden.GetProperty("rect"));
        var expected = new RectI(rect[0], rect[1], rect[2], rect[3]);
        BulbCell cell = bulb.GetCell(slot, flavor, phase);

        Assert.False(cell.IsPlaceholder);
        Assert.Equal(expected, cell.SourceRect);
        Assert.Equal(expected.Size, bulb.GetCellSize(slot, flavor, phase));
        Assert.Equal(golden.GetProperty("opaque").GetInt32(), cell.Image.Pixels.Count(p => Bgra32.A(p) == 255));
        Assert.Equal(golden.GetProperty("sha256").GetString(), GoldenData.RgbaSha256(cell.Image));
    }

    private static IBulb Bulb(string slug) =>
        BuiltInBulbs.TryGet(BulbIds.BuiltIn(slug), out BuiltInBulb? bulb) ? bulb : throw new KeyNotFoundException(slug);
}
