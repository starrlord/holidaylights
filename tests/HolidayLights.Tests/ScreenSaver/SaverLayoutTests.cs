using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.App.ScreenSaver.Simulation;

namespace HolidayLights.Tests.ScreenSaver;

public sealed class SaverLayoutTests
{
    private static readonly SaverField Reference = new(2560, 1440);

    [Fact]
    public void Center_PutsAShortPictureAtAThirdOfTheFreeHeight()
    {
        // Santa Candle (282 x 297) on the reference display: x = (2560 - 282) / 2, y = (1440 - 297) / 3.
        PictureLayout layout = SaverLayoutRules.PlacePicture(Reference, new SizeI(282, 297), PicturePlacement.Center);
        Assert.Equal([RectI.FromXYWH(1139, 381, 282, 297)], layout.Rectangles);
        Assert.Equal(RectI.FromXYWH(1139, 381, 282, 297), layout.Centered);
    }

    [Fact]
    public void Center_CentresAPictureTallerOrWiderThanTheScreen()
    {
        PictureLayout layout = SaverLayoutRules.PlacePicture(new SaverField(100, 100), new SizeI(151, 150), PicturePlacement.Center);
        Assert.Equal([RectI.FromXYWH(-25, -25, 151, 150)], layout.Rectangles);
    }

    [Fact]
    public void Tile_CoversTheScreenFromTheTopLeft()
    {
        PictureLayout layout = SaverLayoutRules.PlacePicture(new SaverField(100, 100), new SizeI(30, 40), PicturePlacement.Tile);
        Assert.Equal(12, layout.Rectangles.Count);
        Assert.Equal(RectI.FromXYWH(0, 0, 30, 40), layout.Rectangles[0]);
        Assert.Equal(RectI.FromXYWH(90, 80, 30, 40), layout.Rectangles[^1]);
        Assert.Null(layout.Centered);
    }

    [Fact]
    public void Stretch_FillsTheScreen()
    {
        PictureLayout layout = SaverLayoutRules.PlacePicture(Reference, new SizeI(282, 297), PicturePlacement.Stretch);
        Assert.Equal([new RectI(0, 0, 2560, 1440)], layout.Rectangles);
    }

    [Fact]
    public void Fit_KeepsTheShapeAndCentres()
    {
        PictureLayout layout = SaverLayoutRules.PlacePicture(new SaverField(200, 100), new SizeI(50, 50), PicturePlacement.Fit);
        Assert.Equal([RectI.FromXYWH(50, 0, 100, 100)], layout.Rectangles);
    }

    [Fact]
    public void Message_SitsBelowACentredPicture_WhenItFitsInSevenTenthsOfTheSpace()
    {
        // Below the picture: 1440 - 678 = 762; a 55-DIP message fits; gaps of 76.
        TextRange range = SaverLayoutRules.MessageRange(Reference, 55, RectI.FromXYWH(1139, 381, 282, 297));
        Assert.Equal(new TextRange(320, 2240, 754, 1288), range);
    }

    [Fact]
    public void Message_UsesEighthMarginsOtherwise()
    {
        TextRange expected = new(320, 2240, 180, 1260);
        Assert.Equal(expected, SaverLayoutRules.MessageRange(Reference, 600, RectI.FromXYWH(1139, 381, 282, 297)));
        Assert.Equal(expected, SaverLayoutRules.MessageRange(Reference, 55, null));
    }

    [Fact]
    public void Message_BouncesOneDipPerStep_TurningOneStepPastEachEnd()
    {
        var bounce = new TextBounce(new TextRange(0, 100, 10, 20), textHeight: 5);
        var tops = new List<int> { bounce.Top };
        for (int i = 0; i < 16; i++)
        {
            bounce.Step();
            tops.Add(bounce.Top);
        }

        Assert.Equal([10, 11, 12, 13, 14, 15, 16, 15, 14, 13, 12, 11, 10, 9, 10, 11, 12], tops);
        Assert.Equal(11, bounce.PreviousTop);
    }

    [Fact]
    public void Message_TallerThanItsRange_StaysStill()
    {
        var bounce = new TextBounce(new TextRange(0, 100, 10, 20), textHeight: 11);
        bounce.Step();
        bounce.Step();
        Assert.Equal((10, 0), (bounce.Top, bounce.Velocity));
    }

    [Fact]
    public void Timeline_RunsDueStepsAndCatchesUpOnlyAFew()
    {
        var timeline = new SaverTimeline(1000, stepTicks: 100);
        Assert.Equal(0, timeline.Advance(1000));
        Assert.Equal(0f, timeline.Progress(1000));
        Assert.Equal(2, timeline.Advance(1250));
        Assert.Equal(0.5f, timeline.Progress(1250));
        Assert.Equal(0, timeline.Advance(1290));

        Assert.Equal(SaverTimeline.MaxCatchUpSteps, timeline.Advance(11_050));
        Assert.Equal(100, timeline.StepsDone);
        Assert.Equal(0.5f, timeline.Progress(11_050));
    }

    [Fact]
    public void Pixels_InterpolateWithSmoothMotion_AndTruncateWholeDipsLike2003()
    {
        var smooth = new SaverPixels(1.5, Smooth: true);
        Assert.Equal(23, smooth.Position(10f, 20f, 0.5f));
        Assert.Equal(30, smooth.Position(10f, 20f, 1f));

        var classic = new SaverPixels(1.5, Smooth: false);
        Assert.Equal(30, classic.Position(10f, 20.9f, 0.1f));
        Assert.Equal(0, classic.Position(0f, -0.9f, 0.5f));
        Assert.Equal(-1, classic.Position(0f, -1.2f, 0.5f));
    }

    [Fact]
    public void Pixels_SnapRectanglesSoNeighboursTile()
    {
        var pixels = new SaverPixels(1.5, true);
        RectI first = pixels.Snap(RectI.FromXYWH(0, 0, 1, 1));
        RectI second = pixels.Snap(RectI.FromXYWH(1, 0, 1, 1));
        Assert.Equal(first.Right, second.Left);
        Assert.Equal(3, first.Width + second.Width);
    }

    [Fact]
    public void Options_LimitFlashingSlowsTheStepsAndKeepsFading()
    {
        var settings = new AppSettings
        {
            Current = new ThemeableSettings { Flash = new FlashSettings { Pattern = FlashPatternId.BulbChase, Interval = 1 } },
            Look = LookPresets.ValuesOf(LookPreset.Classic2003)!,
            Accessibility = new AccessibilitySettings { LimitFlashing = true },
        };

        SaverOptions options = SaverOptions.FromSettings(settings, 42);

        Assert.Equal(3, options.Interval);
        Assert.True(options.Flash.SmoothFading);
        Assert.True(options.Flash.LimitFlashing);
        Assert.Equal(FlashPatternId.BulbChase, options.Flash.Pattern);
        Assert.Equal(SpriteStyle.Crisp, options.Pixels);
        Assert.Equal(0f, options.GlowIntensity);
        Assert.False(options.SmoothMotion);
    }

    [Fact]
    public void Options_ModernGlowIsTheDefault()
    {
        SaverOptions options = SaverOptions.FromSettings(new AppSettings(), 7);
        Assert.Equal(FlashSettings.DefaultInterval, options.Interval);
        Assert.Equal(GlowLevels.Intensity(GlowLevel.Soft), options.GlowIntensity);
        Assert.True(options.SmoothMotion);
        Assert.Equal(SpriteStyle.Smooth, options.Pixels);
        Assert.Equal(SaverAnimations.Snow, options.Look.Animation);
    }

    [Theory]
    [InlineData(SaverDisplays.All, true)]
    [InlineData(SaverDisplays.MainOnly, false)]
    public void Plans_ShowContentOnEveryDisplayOrOnlyTheMainOne(SaverDisplays showOn, bool secondShows)
    {
        DisplayInfo main = SaverTestKit.Display(3840, 2160, 144);
        DisplayInfo second = SaverTestKit.Display(3840, 2160, 144, left: -3840, primary: false, number: 2);

        IReadOnlyList<SaverDisplayPlan> plans = SaverDisplayPlan.For([main, second], showOn);

        Assert.Equal(new SaverDisplayPlan(main, true, true), plans[0]);
        Assert.Equal(new SaverDisplayPlan(second, secondShows, false), plans[1]);
        Assert.Equal(new SaverField(2560, 1440), plans[1].Field);
    }
}
