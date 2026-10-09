using HolidayLights.Core.Sprites;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Sprites;

/// <summary>
/// <see cref="CpuCompositor.DrawLights"/>: the 5.4 picture of a layout golden, bit for bit against
/// reference renders of the original art (<c>Sprites/Golden/layoutsim-*.png</c>), and the brightness, glow, zoom and filter rules.
/// </summary>
public sealed class DrawLightsTests : IDisposable
{
    private readonly TempDataRoot dataRoot = new();
    private readonly SpriteProvider sprites;
    private readonly CpuCompositor compositor = new();

    public DrawLightsTests() => sprites = new SpriteProvider(dataRoot.Paths, NullAppLog.Instance);

    public void Dispose() => dataRoot.Dispose();

    [Theory]
    [InlineData("default", 1920, 1080, 48, 1, 0, "layoutsim-default-1920x1080-p1-f0.png")]
    [InlineData("mixed-example", 1280, 1024, 40, 3, 1, "layoutsim-mixed-1280x1024-p3-f1.png")]
    [InlineData("installer-halloween", 1280, 1024, 40, 4, 5, "layoutsim-halloween-1280x1024-p4-f5.png")]
    public void DrawLights_AtScaleOneMatchesTheLayoutSimRenderBitExactly(
        string theme, int width, int height, int taskbar, int pattern, int frame, string reference)
    {
        GoldenLayout golden = GoldenLayout.Load(theme, new RectI(0, 0, width, height - taskbar), pattern);
        PremultipliedImage target = LayoutSimBackground(width, height, taskbar);

        compositor.DrawLights(target, new LightsRenderRequest
        {
            Layout = golden.Layout,
            States = golden.StatesAt(frame, BuiltInTestBulbs.Instance),
            Bulbs = BuiltInTestBulbs.Instance,
            Sprites = sprites,
        });

        TestImages.AssertIdentical(TestImages.LoadPremultiplied(TestImages.SpritesGolden(reference)), target, reference);
    }

    [Fact]
    public void DrawLights_PlacesSpritesAtRoundedZoomedPositionsAndSizes()
    {
        var bulb = new FakeBulb("user:dot", BulbAnimationKind.Static, Art.Solid(16, 16, Art.Green));
        LightsLayout layout = LayoutOf("d1", new RectI(0, 0, 100, 100), 1.0, (bulb.Id, RectI.FromXYWH(10, 20, 16, 16)));
        var target = new PremultipliedImage(40, 40);

        Draw(target, layout, [new BulbVisualState(0, 1, 0)], new FakeResolver(bulb), zoom: 0.5, offsetX: 3, offsetY: 4);

        // x = floor(10 x 0.5 + 3 + 0.5) = 8, y = floor(20 x 0.5 + 4 + 0.5) = 14; 16 x 0.5 = 8 pixels.
        AssertRegion(target, new RectI(8, 14, 16, 22), Art.Green);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(0.5f)]
    public void DrawLights_LightBulbsShowTheLitFrameOverTheUnlitAtTheirBrightness(float brightness)
    {
        var bulb = new FakeBulb("user:light", BulbAnimationKind.LightBulb, Art.Solid(4, 4, Art.Red), Art.Solid(4, 4, Art.DarkRed));
        LightsLayout layout = LayoutOf("d1", new RectI(0, 0, 4, 4), 1.0, (bulb.Id, RectI.FromXYWH(0, 0, 4, 4)));
        var target = new PremultipliedImage(4, 4);

        Draw(target, layout, [new BulbVisualState(0, brightness, 0)], new FakeResolver(bulb));

        uint o = PixelBlender.OpacityToByte(brightness);
        uint expected = Blend(Art.Red, Art.DarkRed, o);
        Assert.All(target.Pixels, p => Assert.Equal(expected, p));
    }

    [Fact]
    public void DrawLights_AnimationsShowTheirStateFrame()
    {
        var bulb = new FakeBulb("user:anim", BulbAnimationKind.Animation,
            Art.Solid(2, 2, Art.Red), Art.Solid(2, 2, Art.Green), Art.Solid(2, 2, Art.Black));
        LightsLayout layout = LayoutOf("d1", new RectI(0, 0, 4, 2), 1.0,
            (bulb.Id, RectI.FromXYWH(0, 0, 2, 2)), (bulb.Id, RectI.FromXYWH(2, 0, 2, 2)));
        var target = new PremultipliedImage(4, 2);

        Draw(target, layout, [new BulbVisualState(1, 1, 0), new BulbVisualState(5, 1, 0)], new FakeResolver(bulb));

        AssertRegion(target, new RectI(0, 0, 2, 2), Art.Green);
        AssertRegion(target, new RectI(2, 0, 4, 2), Art.Black);
    }

    [Fact]
    public void DrawLights_GlowLiesBeneathEveryBulbAndAddsLightAroundThem()
    {
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(12);
        var bulb = new FakeBulb("user:light", BulbAnimationKind.LightBulb, lit, unlit);
        LightsLayout layout = LayoutOf("d1", new RectI(0, 0, 60, 30), 1.0,
            (bulb.Id, RectI.FromXYWH(18, 9, 12, 12)), (bulb.Id, RectI.FromXYWH(30, 9, 12, 12)));
        PremultipliedImage target = Opaque(60, 30, Bgra32.Pack(10, 10, 30, 255));
        BulbVisualState[] states = [new(0, 1, 1), new(0, 1, 1)];

        Draw(target, layout, states, new FakeResolver(bulb), glowIntensity: 1f);

        PremultipliedImage sprite = sprites.GetSprite(bulb, CellSlot.Top, 0, 0, 1.0, SpriteStyle.Smooth);
        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 12; x++)
            {
                if (sprite[x, y] >> 24 == 255)
                {
                    Assert.Equal(sprite[x, y], target[18 + x, 9 + y]);
                    Assert.Equal(sprite[x, y], target[30 + x, 9 + y]);
                }
            }
        }

        Assert.True(Bgra32.R(target[17, 15]) > 10, "The glow lights up the backdrop next to the bulbs.");
        Assert.Equal(Bgra32.Pack(10, 10, 30, 255), target[0, 0]);
    }

    [Fact]
    public void DrawLights_ClipsGlowAtTheDisplayArea()
    {
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(12);
        var bulb = new FakeBulb("user:light", BulbAnimationKind.LightBulb, lit, unlit);
        LightsLayout layout = LayoutOf("d1", new RectI(0, 0, 40, 30), 1.0, (bulb.Id, RectI.FromXYWH(28, 9, 12, 12)));
        PremultipliedImage target = Opaque(80, 30, Bgra32.Pack(10, 10, 30, 255));

        Draw(target, layout, [new BulbVisualState(0, 1, 1)], new FakeResolver(bulb), glowIntensity: 1f);

        AssertRegion(target, new RectI(40, 0, 80, 30), Bgra32.Pack(10, 10, 30, 255));
        Assert.True(Bgra32.R(target[27, 15]) > 10);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    public void DrawLights_DrawsNoGlowWithoutIntensityOrGlowState(float intensity, float glow)
    {
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(12);
        var bulb = new FakeBulb("user:light", BulbAnimationKind.LightBulb, lit, unlit);
        LightsLayout layout = LayoutOf("d1", new RectI(0, 0, 40, 30), 1.0, (bulb.Id, RectI.FromXYWH(14, 9, 12, 12)));
        PremultipliedImage withGlow = Opaque(40, 30, 0xFF000000);
        PremultipliedImage without = Opaque(40, 30, 0xFF000000);

        Draw(withGlow, layout, [new BulbVisualState(0, 1, glow)], new FakeResolver(bulb), glowIntensity: intensity);
        Draw(without, layout, [new BulbVisualState(0, 1, 0)], new FakeResolver(bulb));

        Assert.Equal(without.Pixels, withGlow.Pixels);
    }

    [Fact]
    public void DrawLights_DrawsOnlyTheRequestedDisplay()
    {
        var bulb = new FakeBulb("user:dot", BulbAnimationKind.Static, Art.Solid(4, 4, Art.Green));
        LightsLayout layout = GoldenLayout.Combine(
            LayoutOf("left", new RectI(-10, 0, 0, 10), 1.0, (bulb.Id, RectI.FromXYWH(-8, 2, 4, 4))),
            LayoutOf("right", new RectI(0, 0, 10, 10), 1.0, (bulb.Id, RectI.FromXYWH(2, 2, 4, 4))));
        var target = new PremultipliedImage(20, 10);

        compositor.DrawLights(target, Request(layout, [new(0, 1, 0), new(0, 1, 0)], new FakeResolver(bulb)) with
        {
            DisplayId = "right",
            OffsetX = 10,
        });

        AssertRegion(target, new RectI(12, 2, 16, 6), Art.Green);
        Assert.Equal(16, target.Pixels.Count(p => p != 0));
    }

    [Fact]
    public void DrawLights_SkipsBulbsThatDoNotResolve()
    {
        var bulb = new FakeBulb("user:dot", BulbAnimationKind.Static, Art.Solid(4, 4, Art.Green));
        LightsLayout layout = LayoutOf("d1", new RectI(0, 0, 10, 4), 1.0,
            ("user:missing", RectI.FromXYWH(0, 0, 4, 4)), (bulb.Id, RectI.FromXYWH(4, 0, 4, 4)));
        var target = new PremultipliedImage(10, 4);

        Draw(target, layout, [new(0, 1, 0), new(0, 1, 0)], new FakeResolver(bulb));

        Assert.Equal(16, target.Pixels.Count(p => p == Art.Green));
    }

    [Fact]
    public void DrawLights_RejectsTooFewStatesAndUnusableZooms()
    {
        var bulb = new FakeBulb("user:dot", BulbAnimationKind.Static, Art.Solid(4, 4, Art.Green));
        LightsLayout layout = LayoutOf("d1", new RectI(0, 0, 10, 4), 1.0, (bulb.Id, RectI.FromXYWH(0, 0, 4, 4)));
        var target = new PremultipliedImage(10, 4);
        LightsRenderRequest request = Request(layout, [new(0, 1, 0)], new FakeResolver(bulb));

        Assert.Throws<ArgumentException>(() => compositor.DrawLights(target, request with { States = [] }));
        Assert.Throws<ArgumentOutOfRangeException>(() => compositor.DrawLights(target, request with { Zoom = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => compositor.DrawLights(target, request with { OffsetX = double.NaN }));
    }

    /// <summary>The "wallpaper" of the reference renders (<c>make_background</c>): a night-sky gradient and a taskbar.</summary>
    internal static PremultipliedImage LayoutSimBackground(int width, int height, int taskbar)
    {
        var image = new PremultipliedImage(width, height);
        for (int y = 0; y < height; y++)
        {
            double t = y / (double)Math.Max(1, height - 1);
            image.GetRow(y).Fill(Bgra32.Pack((byte)(int)(18 + 30 * t), (byte)(int)(38 + 40 * t), (byte)(int)(84 + 50 * t), 255));
        }

        for (int y = height - taskbar; y < height; y++)
        {
            image.GetRow(y).Fill(Bgra32.Pack(32, 32, 36, 255));
            if (y >= height - taskbar + 8 && y <= height - 8)
            {
                image.GetRow(y).Slice(width / 2 - 120, 241).Fill(Bgra32.Pack(60, 60, 66, 255));
            }
        }

        return image;
    }

    /// <summary>A layout of one display from explicit placements (flavor 0, top slot).</summary>
    internal static LightsLayout LayoutOf(string displayId, RectI area, double scale, params (string BulbId, RectI Bounds)[] bulbs)
    {
        BulbPlacement[] placements = [.. bulbs.Select((b, i) => new BulbPlacement
        {
            Ordinal = i,
            DisplayId = displayId,
            StripIndex = 0,
            Slot = CellSlot.Top,
            Index = i,
            BulbId = b.BulbId,
            Flavor = 0,
            Bounds = b.Bounds,
            RingId = 0,
            RingIndex = i,
        })];
        return new LightsLayout
        {
            Mode = FrameMode.EachDisplay,
            Arrangement = SlotAssignment.Empty,
            Displays = [new DisplayLayout(new LayoutTarget(displayId, area, scale), [])],
            Placements = placements,
            Rings = [new LightsRing(0, [.. placements.Select(p => p.Ordinal)])],
        };
    }

    private static uint Blend(uint top, uint bottom, uint opacity)
    {
        var target = new uint[] { bottom };
        PixelBlender.SourceOverScalar([top], target, opacity);
        return opacity == 0 ? bottom : target[0];
    }

    private static PremultipliedImage Opaque(int width, int height, uint pixel)
    {
        var image = new PremultipliedImage(width, height);
        image.Pixels.AsSpan().Fill(pixel);
        return image;
    }

    private static void AssertRegion(PremultipliedImage image, RectI region, uint expected)
    {
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (region.Contains(new PointI(x, y)))
                {
                    Assert.Equal(expected, image[x, y]);
                }
            }
        }
    }

    private LightsRenderRequest Request(LightsLayout layout, BulbVisualState[] states, IBulbResolver bulbs) => new()
    {
        Layout = layout,
        States = states,
        Bulbs = bulbs,
        Sprites = sprites,
    };

    private void Draw(
        PremultipliedImage target, LightsLayout layout, BulbVisualState[] states, IBulbResolver bulbs,
        double zoom = 1, double offsetX = 0, double offsetY = 0, float glowIntensity = 0) =>
        compositor.DrawLights(target, Request(layout, states, bulbs) with
        {
            Zoom = zoom,
            OffsetX = offsetX,
            OffsetY = offsetY,
            GlowIntensity = glowIntensity,
        });
}
