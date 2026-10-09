using HolidayLights.Core.Sprites;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Sprites;

/// <summary>Scene previews: one display or the whole desktop, backdrop, taskbar band, lights with Smooth art and glow.</summary>
public sealed class ScenePreviewRendererTests : IDisposable
{
    private static readonly RectI DefaultWorkArea = new(0, 0, 1024, 738);
    private readonly TempDataRoot dataRoot = new();
    private readonly ScenePreviewRenderer renderer;
    private readonly CpuCompositor compositor = new();
    private readonly SpriteProvider sprites;

    public ScenePreviewRendererTests()
    {
        sprites = new SpriteProvider(dataRoot.Paths, NullAppLog.Instance);
        renderer = new ScenePreviewRenderer(sprites, compositor);
    }

    public void Dispose() => dataRoot.Dispose();

    [Fact]
    public void RenderDisplay_MatchesTheGoldenSceneWithSmoothArtAndSoftGlow()
    {
        // The 5.4 default theme laid out by the 5.4 rules on a 1024 x 768 display (30 px taskbar), all lights lit,
        // previewed at 150 %: MMPX + area averaging, Soft glow, night gradient and taskbar band.
        GoldenLayout golden = GoldenLayout.Load("default", DefaultWorkArea, 1);
        ScenePreviewRequest request = Request(SceneOf(golden.Layout, Display("display-1", new RectI(0, 0, 1024, 768), DefaultWorkArea))) with
        {
            States = golden.StatesAt(0, BuiltInTestBulbs.Instance),
            Zoom = 1.5,
        };

        PremultipliedImage image = renderer.RenderDisplay(request, "display-1");

        Assert.Equal(new SizeI(1536, 1152), image.Size);
        TestImages.AssertMatchesGolden("scene-default-1024x768-zoom150.png", image);
    }

    [Fact]
    public void RenderDisplay_IsTheBackdropTheBandAndDrawLights()
    {
        GoldenLayout golden = GoldenLayout.Load("installer-halloween", DefaultWorkArea, 2);
        DisplayInfo display = Display("display-1", new RectI(0, 0, 1024, 768), DefaultWorkArea);
        BulbVisualState[] states = golden.StatesAt(3, BuiltInTestBulbs.Instance);
        ScenePreviewRequest request = Request(SceneOf(golden.Layout, display)) with { States = states, Zoom = 0.5 };

        PremultipliedImage image = renderer.RenderDisplay(request, "display-1");

        var expected = new PremultipliedImage(512, 384);
        for (int y = 0; y < 384; y++)
        {
            expected.GetRow(y).Fill(PreviewPalette.NightGradientAt(y, 384));
        }

        compositor.Fill(expected, new RectI(0, 369, 512, 384), PreviewPalette.TaskbarBand);
        compositor.DrawLights(expected, new LightsRenderRequest
        {
            Layout = golden.Layout,
            DisplayId = "display-1",
            States = states,
            Bulbs = BuiltInTestBulbs.Instance,
            Sprites = sprites,
            Zoom = 0.5,
            GlowIntensity = GlowLevels.Intensity(GlowLevel.Soft),
        });
        Assert.Equal(expected.Pixels, image.Pixels);
    }

    [Fact]
    public void RenderDesktop_PlacesEveryDisplayAtItsVirtualScreenPosition()
    {
        // The reference PC's arrangement, reduced: a second display to the left at negative x.
        LightsLayout layout = GoldenLayout.Combine(
            GoldenLayout.Load("standard", DefaultWorkArea, 1, "left", offsetX: -1024).Layout,
            GoldenLayout.Load("default", DefaultWorkArea, 1, "right").Layout);
        LightsScene scene = SceneOf(
            layout,
            Display("left", new RectI(-1024, 0, 0, 768), new RectI(-1024, 0, 0, 738)),
            Display("right", new RectI(0, 0, 1024, 768), DefaultWorkArea));
        ScenePreviewRequest request = Request(scene) with { Zoom = 0.75 };

        PremultipliedImage desktop = renderer.RenderDesktop(request);
        PremultipliedImage left = renderer.RenderDisplay(request, "left");
        PremultipliedImage right = renderer.RenderDisplay(request, "right");

        Assert.Equal(new SizeI(1536, 576), desktop.Size);
        Assert.Equal(left.Pixels, desktop.Crop(new RectI(0, 0, 768, 576)).Pixels);
        Assert.Equal(right.Pixels, desktop.Crop(new RectI(768, 0, 1536, 576)).Pixels);
        Assert.NotEqual(left.Pixels, right.Pixels);
    }

    [Fact]
    public void RenderDisplay_DrawsNoBulbsWhileTheLightsAreOff()
    {
        GoldenLayout golden = GoldenLayout.Load("default", DefaultWorkArea, 1);
        DisplayInfo display = Display("display-1", new RectI(0, 0, 1024, 768), DefaultWorkArea);

        PremultipliedImage off = renderer.RenderDisplay(Request(SceneOf(golden.Layout, display) with { LightsOn = false }) with { Zoom = 0.25 }, "display-1");
        PremultipliedImage empty = renderer.RenderDisplay(Request(SceneOf(LightsLayout.Empty, display)) with { Zoom = 0.25 }, "display-1");

        Assert.Equal(empty.Pixels, off.Pixels);
    }

    [Fact]
    public void RenderDisplay_PaintsTheNightGradientAndTheTaskbarBand()
    {
        DisplayInfo display = Display("d", new RectI(0, 0, 100, 80), new RectI(0, 0, 100, 70));

        PremultipliedImage image = renderer.RenderDisplay(Request(SceneOf(LightsLayout.Empty, display)), "d");

        Assert.Equal(PreviewPalette.NightGradientTop.ToBgra32(), image[50, 0]);
        Assert.Equal(PreviewPalette.NightGradientAt(40, 80), image[3, 40]);
        var band = new uint[] { PreviewPalette.NightGradientAt(75, 80) };
        PixelBlender.SourceOver([PreviewPalette.TaskbarBand], band, 255);
        Assert.Equal(band[0], image[10, 75]);
        Assert.Equal(PreviewPalette.NightGradientBottom.ToBgra32(), PreviewPalette.NightGradientAt(79, 80));
    }

    [Fact]
    public void RenderDisplay_HonoursTheBackdropChoice()
    {
        DisplayInfo display = Display("d", new RectI(0, 0, 10, 10), new RectI(0, 0, 10, 10));
        LightsScene scene = SceneOf(LightsLayout.Empty, display);

        PremultipliedImage none = renderer.RenderDisplay(Request(scene) with { Backdrop = PreviewBackdrop.None }, "d");
        PremultipliedImage solid = renderer.RenderDisplay(
            Request(scene) with { Backdrop = PreviewBackdrop.SolidColor, BackdropColor = new RgbColor(1, 2, 3) }, "d");

        Assert.All(none.Pixels, p => Assert.Equal(0u, p));
        Assert.All(solid.Pixels, p => Assert.Equal(new RgbColor(1, 2, 3).ToBgra32(), p));
    }

    [Fact]
    public void RenderDisplay_OverridesForStyleAndGlowTakePrecedenceOverTheScene()
    {
        GoldenLayout golden = GoldenLayout.Load("default", DefaultWorkArea, 1);
        DisplayInfo display = Display("display-1", new RectI(0, 0, 1024, 768), DefaultWorkArea);
        LightsScene scene = SceneOf(golden.Layout, display);
        LightsScene classic = scene with { Effects = new SceneEffects { Pixels = SpriteStyle.Crisp, Glow = GlowLevel.Off } };

        PremultipliedImage overridden = renderer.RenderDisplay(Request(scene) with { Zoom = 1.25, Style = SpriteStyle.Crisp, GlowIntensity = 0 }, "display-1");
        PremultipliedImage fromScene = renderer.RenderDisplay(Request(classic) with { Zoom = 1.25 }, "display-1");
        PremultipliedImage modern = renderer.RenderDisplay(Request(scene) with { Zoom = 1.25 }, "display-1");

        Assert.Equal(fromScene.Pixels, overridden.Pixels);
        Assert.NotEqual(modern.Pixels, overridden.Pixels);
    }

    [Fact]
    public void CreateStaticStates_LightsEveryLightBulbAndShowsFrameZeroOfTheRest()
    {
        var resolver = new FakeResolver(
            new FakeBulb("user:light", BulbAnimationKind.LightBulb, litFrame: 1, flavorCount: 1, Art.Solid(2, 2, Art.DarkRed), Art.Solid(2, 2, Art.Red)),
            new FakeBulb("user:anim", BulbAnimationKind.Animation, Art.Solid(2, 2, Art.Red), Art.Solid(2, 2, Art.Green)));
        LightsLayout layout = DrawLightsTests.LayoutOf("d", new RectI(0, 0, 10, 2), 1.0,
            ("user:light", RectI.FromXYWH(0, 0, 2, 2)), ("user:anim", RectI.FromXYWH(2, 0, 2, 2)), ("user:gone", RectI.FromXYWH(4, 0, 2, 2)));

        BulbVisualState[] states = ScenePreviewRenderer.CreateStaticStates(layout, resolver);

        Assert.Equal(new BulbVisualState[] { new(1, 1, 1), new(0, 1, 0), new(0, 1, 0) }, states);
    }

    [Theory]
    [InlineData(3840, 2160, 0.25, 960, 540)]
    [InlineData(1024, 768, 1.5, 1536, 1152)]
    [InlineData(3, 3, 0.5, 2, 2)]
    [InlineData(1, 1, 0.01, 1, 1)]
    public void GetOutputSize_RoundsAndKeepsAtLeastOnePixel(int width, int height, double zoom, int expectedWidth, int expectedHeight) =>
        Assert.Equal(new SizeI(expectedWidth, expectedHeight), ScenePreviewRenderer.GetOutputSize(new RectI(0, 0, width, height), zoom));

    [Fact]
    public void RenderDisplay_RejectsUnknownDisplaysAndUnusableZooms()
    {
        LightsScene scene = SceneOf(LightsLayout.Empty, Display("d", new RectI(0, 0, 10, 10), new RectI(0, 0, 10, 10)));

        Assert.Throws<ArgumentException>(() => renderer.RenderDisplay(Request(scene), "elsewhere"));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RenderDisplay(Request(scene) with { Zoom = 0 }, "d"));
        Assert.Equal(new SizeI(0, 0), renderer.RenderDesktop(Request(LightsScene.Empty)).Size);
    }

    private static DisplayInfo Display(string id, RectI bounds, RectI workArea) => new()
    {
        DeviceId = id,
        DeviceName = @"\\.\DISPLAY1",
        Number = 1,
        Bounds = bounds,
        WorkArea = workArea,
        Dpi = 96,
        IsPrimary = bounds.Left == 0,
    };

    private static LightsScene SceneOf(LightsLayout layout, params DisplayInfo[] displays) => new()
    {
        LightsOn = true,
        Displays = [.. displays.Select(d => new DisplayScene(d, true))],
        Layout = layout,
    };

    private static ScenePreviewRequest Request(LightsScene scene) => new() { Scene = scene, Bulbs = BuiltInTestBulbs.Instance };
}
