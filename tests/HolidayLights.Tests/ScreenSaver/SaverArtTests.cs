using System.Text.Json;
using HolidayLights.App.ScreenSaver.Art;
using HolidayLights.App.ScreenSaver.Simulation;
using HolidayLights.Core.Bulbs;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.ScreenSaver;

public sealed class SaverArtTests
{
    [Fact]
    public void Table_HasThe21PictureAnimationsOfTheList()
    {
        string[] own = [SaverAnimations.None, SaverAnimations.Snow, SaverAnimations.SnowFlakes, SaverAnimations.Balloons];
        Assert.Equal(21, SaverAnimationTable.All.Count);
        Assert.Equal(SaverAnimations.All.Except(own).Order(StringComparer.Ordinal), SaverAnimationTable.All.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BuiltInCells_MatchTheRectanglesVerifiedByRenderingEveryRecord()
    {
        using JsonDocument reference = JsonDocument.Parse(File.ReadAllBytes(TestPaths.Golden("screensaver-animations.json")));
        int checkedCells = 0;
        foreach (JsonElement animation in reference.RootElement.EnumerateArray())
        {
            if (animation.GetProperty("kind").GetString() != "includedBulbs")
            {
                continue;
            }

            SaverAnimationEntry entry = SaverAnimationTable.All[animation.GetProperty("name").GetString()!];
            JsonElement[] records = [.. animation.GetProperty("records").EnumerateArray()];
            Assert.Equal(records.Length, entry.Records.Count);
            for (int r = 0; r < records.Length; r++)
            {
                int[] cells = [.. records[r].GetProperty("images").EnumerateArray().Select(i => i.GetInt32())];
                int[][] rects = [.. records[r].GetProperty("rects").EnumerateArray().Select(a => a.EnumerateArray().Select(v => v.GetInt32()).ToArray())];
                Assert.Equal(records[r].GetProperty("bulbId").GetInt32(), entry.Records[r].BulbId);
                Assert.Equal(cells, entry.Records[r].Cells);
                for (int c = 0; c < cells.Length; c++)
                {
                    Assert.Equal(new RectI(rects[c][0], rects[c][1], rects[c][2], rects[c][3]), BuiltInBulbs.GetSheetCell(entry.Records[r].BulbId, cells[c])?.SourceRect);
                    checkedCells++;
                }
            }
        }

        Assert.True(checkedCells > 300);
    }

    [Theory]
    [InlineData("Singing Tree", 3100, 121, 132, 10)]
    [InlineData("Dancing Demon", 3101, 130, 114, 9)]
    [InlineData("Skeleton", 3102, 29, 77, 9)]
    [InlineData("Gingerbread Man", 3103, 100, 119, 6)]
    [InlineData("Angel", 3104, 112, 170, 2)]
    [InlineData("Santa", 3105, 62, 103, 20)]
    public void GifAnimations_DecodeEveryFrame(string name, int resource, int width, int height, int frames)
    {
        SaverAnimationEntry entry = SaverAnimationTable.All[name];
        Assert.Equal(resource, entry.GifResource);
        SpriteArt art = Assert.Single(SaverArtLibrary.ForTableAnimation(entry));
        Assert.Equal(new SizeI(width, height), art.Size);
        Assert.Equal(frames, art.FrameCount);
        Assert.All(art.Frames, f => Assert.Contains(f.Pixels, p => Bgra32.A(p) == 255));
    }

    [Fact]
    public void Balloons_AreSixColoursOf21By50()
    {
        SpriteArt balloons = SaverArtLibrary.Balloons;
        Assert.Equal(SaverArtLibrary.BalloonColors, balloons.FrameCount);
        Assert.All(balloons.Frames, f => Assert.Equal(new SizeI(21, 50), f.Size));
        Assert.Equal(6, balloons.Frames.Select(f => f.Pixels.Where(p => Bgra32.A(p) == 255).GroupBy(p => p).OrderByDescending(g => g.Count()).First().Key).Distinct().Count());
    }

    [Theory]
    [InlineData(2, 2, 2, 4)]
    [InlineData(3, 3, 3, 5)]
    [InlineData(4, 4, 4, 12)]
    [InlineData(5, 5, 5, 21)]
    public void SnowShapes_AreGdiSquaresAndEllipses(int size, int width, int height, int whitePixels)
    {
        Rgba32Image flake = SnowArt.Shapes.ForSize(size).Frames[0];
        Assert.Equal(new SizeI(width, height), flake.Size);
        Assert.Equal(whitePixels, flake.Pixels.Count(p => p == 0xFFFFFFFF));
        Assert.Equal(width * height - whitePixels, flake.Pixels.Count(p => p == 0));
    }

    [Fact]
    public void SnowFlakes_UseTheFirstThreeFlakeBitmapsForSizesThreeToFive()
    {
        Assert.Equal(new SizeI(2, 2), SnowArt.Flakes.ForSize(2).Size);
        for (int size = 3; size <= 5; size++)
        {
            Rgba32Image flake = SnowArt.Flakes.ForSize(size).Frames[0];
            Assert.Equal(new SizeI(9, 9), flake.Size);
            Assert.Contains(flake.Pixels, p => Bgra32.A(p) == 255);
        }

        Assert.NotEqual(SnowArt.Flakes.ForSize(3).Frames[0].Pixels, SnowArt.Flakes.ForSize(5).Frames[0].Pixels);
        Assert.Equal(9, SnowArt.Flakes.MaxHeight);
    }

    [Fact]
    public void EveryAnimationOfTheList_HasItsModule()
    {
        using var kit = new SaverTestKit();
        var field = new SaverField(2560, 1440);
        foreach (string animation in SaverAnimations.All)
        {
            SaverModule? module = SaverModules.Create(animation, SaverAnimations.DefaultStyleFor(animation), field, new CrtSaverRandom(1), kit.Catalog);
            if (animation == SaverAnimations.None)
            {
                Assert.Null(module);
                continue;
            }

            Assert.NotNull(module);
            if (module is FloaterModule floaters)
            {
                Assert.Equal(FloaterModule.MaxFloaters, floaters.Floaters.Count);
                Assert.All(floaters.Floaters, f => Assert.False(f.Art.Size.IsEmpty));
            }

            var context = new SaverStepContext(null);
            module.Step(context);
            Assert.NotEmpty(context.Sprites);
        }
    }

    [Fact]
    public void AnAddOnBulb_FloatsItsAnimations()
    {
        using var kit = new SaverTestKit();
        string bulbId = Directory.EnumerateFiles(TestPaths.ContentFolder + "/Bulbs", "*.bul").Select(p => BulbIds.AddOn(Path.GetFileNameWithoutExtension(p))).First();

        SaverModule? module = SaverModules.Create(SaverAnimations.ForBulb(bulbId), SaverMovementStyle.Attraction, new SaverField(2560, 1440), new CrtSaverRandom(3), kit.Catalog);

        FloaterModule floaters = Assert.IsType<FloaterModule>(module);
        Assert.NotEmpty(floaters.Floaters);
        Assert.All(floaters.Floaters, f => Assert.StartsWith(bulbId + "#", f.Art.Key));
    }

    [Fact]
    public void AMissingBulbOrUnknownName_ShowsNoAnimation()
    {
        using var kit = new SaverTestKit();
        var field = new SaverField(800, 600);
        Assert.Null(SaverModules.Create(SaverAnimations.ForBulb("addon:NoSuchBulbAnywhere"), SaverMovementStyle.BounceOffSides, field, new CrtSaverRandom(1), kit.Catalog));
        Assert.Null(SaverModules.Create("Fireworks", SaverMovementStyle.BounceOffSides, field, new CrtSaverRandom(1), kit.Catalog));
    }
}
