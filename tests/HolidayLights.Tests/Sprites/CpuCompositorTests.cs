using HolidayLights.Core.Sprites;

namespace HolidayLights.Tests.Sprites;

/// <summary>Compositor math: premultiplied source-over (including colour above alpha), additive light, clipping.</summary>
public sealed class CpuCompositorTests
{
    private readonly CpuCompositor compositor = new();

    [Fact]
    public void Div255_IsExactlyRoundedForEveryProductOfTwoBytes()
    {
        for (uint x = 0; x <= 255 * 255; x++)
        {
            Assert.Equal((uint)Math.Round(x / 255.0, MidpointRounding.AwayFromZero), PixelBlender.Div255(x));
        }
    }

    [Theory]
    [InlineData(0f, 0u)]
    [InlineData(1f, 255u)]
    [InlineData(0.55f, 140u)]
    [InlineData(-0.5f, 0u)]
    [InlineData(3f, 255u)]
    public void OpacityToByte_RoundsAndClamps(float opacity, uint expected) =>
        Assert.Equal(expected, PixelBlender.OpacityToByte(opacity));

    [Theory]
    // What DWM composes, measured on the reference PC.
    [InlineData(255, 128, 0, 0, 60, 60, 60, 255, 188, 60)]
    [InlineData(255, 128, 0, 64, 60, 60, 60, 255, 173, 45)]
    [InlineData(255, 128, 0, 0, 5, 4, 36, 255, 132, 36)]
    [InlineData(255, 128, 0, 64, 5, 4, 36, 255, 131, 27)]
    public void SourceOver_MatchesTheMeasuredDesktopComposition(
        byte r, byte g, byte b, byte a, byte dr, byte dg, byte db, byte er, byte eg, byte eb)
    {
        PremultipliedImage target = Filled(1, 1, Bgra32.Pack(dr, dg, db, 255));

        compositor.Draw(target, Filled(1, 1, Bgra32.Pack(r, g, b, a)), 0, 0, 1f, CompositeMode.SourceOver);

        Assert.Equal(Bgra32.Pack(er, eg, eb, 255), target[0, 0]);
    }

    [Fact]
    public void SourceOver_BlendsPremultipliedPixelsExactly()
    {
        PremultipliedImage target = Filled(3, 1, Bgra32.Pack(0, 200, 100, 255));
        var source = new PremultipliedImage(3, 1, [0xFF112233, 0x80400000, 0x00000000]);

        compositor.Draw(target, source, 0, 0, 1f, CompositeMode.SourceOver);

        Assert.Equal(0xFF112233u, target[0, 0]);                       // opaque replaces
        Assert.Equal(Bgra32.Pack(64, 100, 50, 255), target[1, 0]);       // 64 + 0; 0 + 200 x 127 / 255; 0 + 100 x 127 / 255
        Assert.Equal(Bgra32.Pack(0, 200, 100, 255), target[2, 0]);       // transparent changes nothing
    }

    [Fact]
    public void SourceOver_AppliesOpacityToEveryChannel()
    {
        PremultipliedImage target = Filled(1, 1, Bgra32.Pack(0, 0, 200, 255));

        compositor.Draw(target, Filled(1, 1, Bgra32.Pack(255, 0, 0, 255)), 0, 0, 0.5f, CompositeMode.SourceOver);

        // o = 128: source (128, 0, 0, a 128); destination x (255 - 128) / 255.
        Assert.Equal(Bgra32.Pack(128, 0, 100, 255), target[0, 0]);
    }

    [Fact]
    public void SourceOver_OntoTransparentKeepsPremultipliedValues()
    {
        var target = new PremultipliedImage(1, 1);

        compositor.Draw(target, Filled(1, 1, 0x80402010), 0, 0, 1f, CompositeMode.SourceOver);

        Assert.Equal(0x80402010u, target[0, 0]);
    }

    [Fact]
    public void Additive_AddsColourClampsAndKeepsAlpha()
    {
        var target = new PremultipliedImage(3, 1, [Bgra32.Pack(60, 60, 60, 255), Bgra32.Pack(250, 10, 0, 0x80), 0]);
        var source = new PremultipliedImage(3, 1, [Bgra32.Pack(255, 128, 0, 0), Bgra32.Pack(20, 20, 20, 0xFF), Bgra32.Pack(30, 40, 50, 0)]);

        compositor.Draw(target, source, 0, 0, 1f, CompositeMode.Additive);

        Assert.Equal(Bgra32.Pack(255, 188, 60, 255), target[0, 0]);
        Assert.Equal(Bgra32.Pack(255, 30, 20, 0x80), target[1, 0]);     // the source alpha is ignored
        Assert.Equal(Bgra32.Pack(30, 40, 50, 0), target[2, 0]);         // light on transparent stays additive light
    }

    [Fact]
    public void Additive_ScalesTheLightByTheOpacity()
    {
        PremultipliedImage target = Filled(1, 1, Bgra32.Pack(10, 10, 10, 255));

        compositor.Draw(target, Filled(1, 1, Bgra32.Pack(200, 100, 0, 0)), 0, 0, 0.55f, CompositeMode.Additive);

        // o = 140: 200 x 140 / 255 = 110, 100 x 140 / 255 = 55.
        Assert.Equal(Bgra32.Pack(120, 65, 10, 255), target[0, 0]);
    }

    [Theory]
    [InlineData(-2, -2)]
    [InlineData(5, 6)]
    [InlineData(-3, 4)]
    [InlineData(6, -1)]
    [InlineData(20, 20)]
    [InlineData(-9, 0)]
    public void Draw_ClipsAtEveryEdge(int x, int y)
    {
        PremultipliedImage target = Filled(8, 8, 0xFF000000);
        var source = new PremultipliedImage(4, 4, [.. Enumerable.Range(0, 16).Select(i => 0xFF000000 | (uint)(i + 1))]);

        compositor.Draw(target, source, x, y, 1f, CompositeMode.SourceOver);

        for (int ty = 0; ty < 8; ty++)
        {
            for (int tx = 0; tx < 8; tx++)
            {
                bool inside = tx - x is >= 0 and < 4 && ty - y is >= 0 and < 4;
                Assert.Equal(inside ? source[tx - x, ty - y] : 0xFF000000, target[tx, ty]);
            }
        }
    }

    [Fact]
    public void Draw_WithZeroOpacityChangesNothing()
    {
        PremultipliedImage target = Filled(2, 2, 0xFF102030);

        compositor.Draw(target, Filled(2, 2, 0xFFFFFFFF), 0, 0, 0f, CompositeMode.SourceOver);
        compositor.Draw(target, Filled(2, 2, 0x00FFFFFF), 0, 0, 0f, CompositeMode.Additive);

        Assert.All(target.Pixels, p => Assert.Equal(0xFF102030u, p));
    }

    [Fact]
    public void Draw_RejectsNanOpacityAndUnknownModes()
    {
        var target = new PremultipliedImage(1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => compositor.Draw(target, target.Clone(), 0, 0, float.NaN, CompositeMode.SourceOver));
        Assert.Throws<ArgumentOutOfRangeException>(() => compositor.Draw(target, target.Clone(), 0, 0, 1f, (CompositeMode)5));
    }

    [Fact]
    public void Clear_FillsEveryPixel()
    {
        var target = new PremultipliedImage(5, 3);

        compositor.Clear(target, 0x80102030);

        Assert.All(target.Pixels, p => Assert.Equal(0x80102030u, p));
    }

    [Fact]
    public void Fill_BlendsTheTaskbarBandOverTheBackdropInsideTheClippedArea()
    {
        PremultipliedImage target = Filled(4, 4, Bgra32.Pack(11, 21, 48, 255));

        compositor.Fill(target, new RectI(-5, 2, 3, 10), PreviewPalette.TaskbarBand);

        // 27 + c x 38 / 255 per channel (band alpha 217).
        uint expected = Bgra32.Pack(29, 30, 34, 255);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(x < 3 && y >= 2 ? expected : Bgra32.Pack(11, 21, 48, 255), target[x, y]);
            }
        }
    }

    [Fact]
    public void Fill_OpaqueReplacesAndTransparentDoesNothing()
    {
        PremultipliedImage target = Filled(3, 3, 0xFF000000);

        compositor.Fill(target, new RectI(1, 1, 3, 3), 0xFFABCDEF);
        compositor.Fill(target, new RectI(0, 0, 3, 3), 0);

        Assert.Equal(0xFF000000u, target[0, 0]);
        Assert.Equal(0xFFABCDEFu, target[2, 2]);
    }

    [Theory]
    [InlineData(255u)]
    [InlineData(140u)]
    [InlineData(1u)]
    [InlineData(128u)]
    public void VectorAndScalarPathsAgree(uint opacity)
    {
        for (int length = 0; length <= 37; length++)
        {
            uint[] source = Art.Noise(length, seed: length, premultiplied: true);
            uint[] destination = Art.Noise(length, seed: 1000 + length, premultiplied: true);

            uint[] vector = (uint[])destination.Clone();
            uint[] scalar = (uint[])destination.Clone();
            PixelBlender.SourceOver(source, vector, opacity);
            PixelBlender.SourceOverScalar(source, scalar, opacity);
            Assert.Equal(scalar, vector);

            vector = (uint[])destination.Clone();
            scalar = (uint[])destination.Clone();
            PixelBlender.Additive(source, vector, opacity);
            PixelBlender.AdditiveScalar(source, scalar, opacity);
            Assert.Equal(scalar, vector);
        }
    }

    [Fact]
    public void SourceOver_MatchesTheFormulaForRandomPixels()
    {
        uint[] source = Art.Noise(4000, seed: 7, premultiplied: true);
        uint[] destination = Art.Noise(4000, seed: 8, premultiplied: true);
        uint[] actual = (uint[])destination.Clone();

        PixelBlender.SourceOver(source, actual, 200);

        for (int i = 0; i < source.Length; i++)
        {
            double o = 200 / 255.0;
            double sa = (source[i] >> 24) * o;
            for (int shift = 0; shift < 32; shift += 8)
            {
                double s = ((source[i] >> shift) & 0xFF) * o;
                double d = (destination[i] >> shift) & 0xFF;
                double expected = Math.Min(255, s + d * (1 - sa / 255));

                // Three roundings to whole levels (scaled source, scaled alpha, blended destination), half a level each.
                Assert.InRange((double)((actual[i] >> shift) & 0xFF), expected - 1.5, expected + 1.5);
            }
        }
    }

    private static PremultipliedImage Filled(int width, int height, uint pixel)
    {
        var pixels = new uint[width * height];
        Array.Fill(pixels, pixel);
        return new PremultipliedImage(width, height, pixels);
    }
}
