using System.Text.Json;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

public sealed class AddOnBulbTests
{
    private static readonly byte[] Gif = TestGif.Encode(4, 3, [(0, 0, 0), (255, 0, 0)], [new TestGifFrame { Width = 4, Height = 3, Pixels = new byte[12] }]);

    [Fact]
    public void Cells_AreTheFaithfulFramesOfTheSlotTable()
    {
        JsonElement golden = BulbGoldens.BulFiles.First(f => f.GetProperty("file").GetString() == "10thBirthday.bul");
        IBulb bulb = Bundled("10thBirthday.bul");

        // Top flavors use entries 8, 1, 2, ... 6; entry 8 has 4 frames, the others 2.
        Assert.Equal(7, bulb.GetFlavorCount(Side.Top));
        Assert.Equal(4, bulb.GetPhaseCount(Side.Top));
        Assert.Equal(5, bulb.GetFlavorCount(Side.Right));
        for (int phase = 0; phase < 9; phase++)
        {
            AssertFrame(golden, entry: 8, frame: phase % 4, bulb.GetCell(CellSlot.Top, 0, phase));
            AssertFrame(golden, entry: 1, frame: phase % 2, bulb.GetCell(CellSlot.Top, 8, phase));
            AssertFrame(golden, entry: 0, frame: phase % 4, bulb.GetCell(CellSlot.BottomLeft, 3, phase));
        }

        Assert.Equal(new SizeI(32, 32), bulb.GetCellSize(CellSlot.Top, 0, 0));
        Assert.Equal(new BulbAnimationInfo(BulbAnimationKind.Animation, 4, 0, new SizeI(32, 32)), bulb.GetAnimation(CellSlot.Preview, 0));
        Assert.Null(bulb.GetCell(CellSlot.Top, 0, 0).SourceRect);
    }

    [Theory]
    [InlineData("BeadBulbs.bul", BulbAnimationKind.LightBulb, 0)]
    [InlineData("ChristmasBells.bul", BulbAnimationKind.LightBulb, 1)]
    [InlineData("12DaysofChristmasPart1.bul", BulbAnimationKind.Animation, 0)]
    [InlineData("Arrow.bul", BulbAnimationKind.Static, 0)]
    public void Kinds_FollowTheLightBulbRule(string file, BulbAnimationKind kind, int litFrame)
    {
        BulbAnimationInfo info = Bundled(file).GetAnimation(CellSlot.Top, 0);

        Assert.Equal(kind, info.Kind);
        Assert.Equal(litFrame, info.LitFrame);
        Assert.Equal(kind == BulbAnimationKind.LightBulb ? 1 - litFrame : litFrame, info.UnlitFrame);
    }

    [Theory]
    [InlineData("Snowman.bul", 39, 34, 71, 66)]
    [InlineData("Arrow.bul", 0, 0, 13, 13)]
    [InlineData("BunnyWithFlower.bul", 17, 32, 49, 64)]
    public void PreviewWindow_IsThe32By32CropOfFrameZero(string file, int left, int top, int right, int bottom)
    {
        Assert.Equal(new RectI(left, top, right, bottom), Bundled(file).PreviewWindow);
    }

    [Fact]
    public void Metadata_ComesFromTheHeader()
    {
        string path = Path.Combine(BulbGoldens.BundledBulbsFolder, "Snowman3.bul");
        IBulb bulb = AddOnBulbs.FromFile(BulFile.Read(path), "addon:Snowman3", BulbOrigin.BundledAddOn, "key-1");

        Assert.Equal("addon:Snowman3", bulb.Id);
        Assert.Equal(9771345, bulb.LegacyId);
        Assert.Equal(BulbOrigin.BundledAddOn, bulb.Origin);
        Assert.Equal("key-1", bulb.ContentKey);
        Assert.Equal("Snowman", bulb.Name);
        Assert.StartsWith("For a nice effect use on bottom", bulb.Description);
        Assert.StartsWith("J Perkins\r\n", bulb.Author);
        Assert.Equal(new[] { "Characters", "Christmas" }, bulb.Categories);
        Assert.Equal(path, bulb.FilePath);
        Assert.True(bulb.IsLocked);
        Assert.False(bulb.IsEditable);
        Assert.False(bulb.HasDamagedArt);
        Assert.Equal((0, 0), (bulb.HorizontalSpacing, bulb.VerticalSpacing));
    }

    [Fact]
    public void Editable_OnlyForUnlockedMyBulbs()
    {
        BulFile unlocked = BulFile.Parse(new TestBul { Gifs = [Gif], Locked = false, Categories = "_1|Mine" }.Build());

        IBulb mine = AddOnBulbs.FromFile(unlocked, "user:Mine", BulbOrigin.UserAddOn, "k");

        Assert.True(mine.IsEditable);
        Assert.False(mine.IsLocked);
        Assert.False(AddOnBulbs.FromFile(unlocked, "addon:Mine", BulbOrigin.BundledAddOn, "k").IsEditable);
        Assert.Equal(new[] { "Mine" }, mine.Categories);
    }

    [Fact]
    public void DamagedAnimations_AreDrawnAsTheWarningPicture()
    {
        byte[] broken = [.. Gif.AsSpan(0, Gif.Length - 6), 0x3B];
        BulFile file = BulFile.Parse(new TestBul { Gifs = [Gif, broken], Sides = [[0, 1, 5], [0], [0], [0]] }.Build());

        IBulb bulb = AddOnBulbs.FromFile(file, "user:Broken", BulbOrigin.UserAddOn, "broken-1");

        Assert.False(bulb.GetCell(CellSlot.Top, 0, 0).IsPlaceholder);
        BulbCell bad = bulb.GetCell(CellSlot.Top, 1, 0);
        Assert.True(bad.IsPlaceholder);
        Assert.Same(HeritageArt.Warning, bad.Image);
        Assert.True(bulb.GetCell(CellSlot.Top, 2, 0).IsPlaceholder);
        Assert.Equal(new SizeI(32, 32), bulb.GetCellSize(CellSlot.Top, 1, 0));
        Assert.Equal(new BulbAnimationInfo(BulbAnimationKind.Static, 1, 0, new SizeI(32, 32)), bulb.GetAnimation(CellSlot.Top, 2));
        Assert.Equal(new SizeI(4, 3), bulb.GetCellSize(CellSlot.Top, 3, 0));
        Assert.True(bulb.HasDamagedArt);
        Assert.Equal("This bulb is damaged and may cause problems.", bulb.Description);
    }

    [Fact]
    public void FromFile_RejectsADamagedFile()
    {
        BulFile damaged = BulFile.Parse(new TestBul { Gifs = [Gif], PreviewEntry = -1 }.Build());

        Assert.Throws<ArgumentException>(() => AddOnBulbs.FromFile(damaged, "user:X", BulbOrigin.UserAddOn, "x"));
    }

    [Fact]
    public void Cells_AreThreadSafeAndShared()
    {
        IBulb bulb = Bundled("HalloweenBulbs.bul");
        BulbCell[] cells = new BulbCell[64];

        Parallel.For(0, cells.Length, i => cells[i] = bulb.GetCell(CellSlot.Top, i % 8, i / 8));

        for (int i = 0; i < cells.Length; i++)
        {
            Assert.Equal(GoldenData.RgbaSha256(bulb.GetCell(CellSlot.Top, i % 8, i / 8).Image), GoldenData.RgbaSha256(cells[i].Image));
        }
    }

    private static IBulb Bundled(string file)
    {
        string path = Path.Combine(BulbGoldens.BundledBulbsFolder, file);
        return AddOnBulbs.FromFile(BulFile.Read(path), BulbIds.AddOn(Path.GetFileNameWithoutExtension(file)), BulbOrigin.BundledAddOn, "test:" + file);
    }

    private static void AssertFrame(JsonElement golden, int entry, int frame, BulbCell cell)
    {
        JsonElement expected = golden.GetProperty("entries").EnumerateArray().First(e => e.GetProperty("index").GetInt32() == entry);
        Assert.False(cell.IsPlaceholder);
        Assert.Equal(BulbGoldens.Strings(expected, "sha256")[frame], GoldenData.RgbaSha256(cell.Image));
    }
}
