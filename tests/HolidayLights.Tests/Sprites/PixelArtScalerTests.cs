using HolidayLights.Core.Sprites;

namespace HolidayLights.Tests.Sprites;

/// <summary>Smooth and Crisp scaling (PRODUCT-SPEC 5.3.3): sizes, identities, alpha properties and the upscaling prototype's references (Golden/upscale).</summary>
public sealed class PixelArtScalerTests
{
    private static readonly Lazy<Rgba32Image> Sheet = new(() => TestImages.Load(TestImages.UpscaleResult("1x.png")));
    private static readonly PixelArtScaleOptions TransparentEdge = new() { Edge = MmpxEdge.Transparent };

    public static TheoryData<int, int, double> Sizes()
    {
        var data = new TheoryData<int, int, double>();
        foreach ((int w, int h) in new[] { (1, 1), (3, 7), (13, 5), (32, 32), (50, 24) })
        {
            foreach (double scale in new[] { 0.25, 0.375, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 3.0, 3.5, 4.0, 5.0, 6.25, 8.0 })
            {
                data.Add(w, h, scale);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Scale_SizeIsArtScaleScaleForBothStyles(int width, int height, double scale)
    {
        Rgba32Image source = Art.Solid(width, height, Art.Red);
        SizeI expected = ArtScale.Scale(new SizeI(width, height), scale);

        Assert.Equal(expected, PixelArtScaler.Scale(source, scale, SpriteStyle.Smooth).Size);
        Assert.Equal(expected, PixelArtScaler.Scale(source, scale, SpriteStyle.Crisp).Size);
    }

    [Theory]
    [InlineData(SpriteStyle.Smooth)]
    [InlineData(SpriteStyle.Crisp)]
    public void Scale_AtOneReturnsTheOriginalPixels(SpriteStyle style)
    {
        foreach (Rgba32Image cell in BuiltInTestBulbs.Instance["standard-bulbs"].Cells)
        {
            PremultipliedImage actual = PixelArtScaler.Scale(cell, 1.0, style);

            Assert.Equal(cell.Pixels, actual.Pixels);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Crisp_AtIntegerScalesIsNearestNeighbour(int factor)
    {
        Rgba32Image source = Sheet.Value;

        PremultipliedImage actual = PixelArtScaler.Scale(source, factor, SpriteStyle.Crisp);

        for (int y = 0; y < actual.Height; y += 7)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                Assert.Equal(source[x / factor, y / factor], actual[x, y]);
            }
        }
    }

    [Fact]
    public void Smooth_AtTwoIsMmpxClampedAtTheFrameEdge()
    {
        Rgba32Image expected = TestImages.Load(TestImages.SpritesGolden("mmpx-2x-clamp.png"));

        PremultipliedImage actual = PixelArtScaler.Scale(Sheet.Value, 2.0, SpriteStyle.Smooth);

        Assert.Equal(expected.Pixels, actual.Pixels);
    }

    [Fact]
    public void Smooth_AtFourIsMmpxTwice()
    {
        Rgba32Image cell = BuiltInTestBulbs.Instance["candy-canes"].Cells.First();

        PremultipliedImage actual = PixelArtScaler.Scale(cell, 4.0, SpriteStyle.Smooth);

        Assert.Equal(Mmpx.Scale2x(Mmpx.Scale2x(cell)).Pixels, actual.Pixels);
    }

    [Fact]
    public void Smooth_AtEightIsMmpxFourTimesThenNearestDoubling()
    {
        Rgba32Image cell = BuiltInTestBulbs.Instance["jolly-holly"].Cells.First();
        Rgba32Image fourTimes = Mmpx.Scale2x(Mmpx.Scale2x(cell));

        PremultipliedImage actual = PixelArtScaler.Scale(cell, 8.0, SpriteStyle.Smooth);

        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                Assert.Equal(fourTimes[x / 2, y / 2], actual[x, y]);
            }
        }
    }

    [Fact]
    public void Smooth_BeyondFourIsMmpxFourTimesThenNearestDoublingsThenAreaAveraging()
    {
        // The spec's literal recipe for S = 6, with the 8x picture built in memory: MMPX 4x, a nearest doubling, averaging.
        Rgba32Image cell = BuiltInTestBulbs.Instance["standard-bulbs"].Cells.First();
        Rgba32Image fourTimes = Mmpx.Scale2x(Mmpx.Scale2x(cell));
        var eightTimes = new Rgba32Image(fourTimes.Width * 2, fourTimes.Height * 2);
        for (int y = 0; y < eightTimes.Height; y++)
        {
            for (int x = 0; x < eightTimes.Width; x++)
            {
                eightTimes[x, y] = fourTimes[x / 2, y / 2];
            }
        }

        SizeI size = ArtScale.Scale(cell.Size, 6.0);
        uint[] expected = AreaResampler.Resample(eightTimes.Pixels, eightTimes.Width, eightTimes.Height, 1, size.Width, size.Height);

        PremultipliedImage actual = PixelArtScaler.Scale(cell, 6.0, SpriteStyle.Smooth);

        Assert.Equal(expected.Select(Bgra32.Premultiply), actual.Pixels);
    }

    [Theory]
    [InlineData("1.25x-mmpx-box.png", 1.25)]
    [InlineData("1.50x-mmpx-box.png", 1.5)]
    [InlineData("1.75x-mmpx-box.png", 1.75)]
    [InlineData("2.50x-mmpx-box.png", 2.5)]
    public void Smooth_MatchesThePocAreaAveraging(string reference, double scale)
    {
        Rgba32Image expected = TestImages.Load(TestImages.UpscaleResult(reference));

        PremultipliedImage actual = PixelArtScaler.Scale(Sheet.Value, scale, SpriteStyle.Smooth, TransparentEdge);

        AssertMatchesStraightReference(expected, actual);
    }

    [Fact]
    public void Crisp_MatchesThePocNearestThenAreaAveraging()
    {
        Rgba32Image expected = TestImages.Load(TestImages.UpscaleResult("1.50x-nearest-box.png"));

        PremultipliedImage actual = PixelArtScaler.Scale(Sheet.Value, 1.5, SpriteStyle.Crisp);

        AssertMatchesStraightReference(expected, actual);
    }

    [Fact]
    public void AlphaThreshold_MatchesThePocOneBitVariant()
    {
        Rgba32Image expected = TestImages.Load(TestImages.UpscaleResult("1.50x-mmpx-box-1bit.png"));

        PremultipliedImage actual = PixelArtScaler.Scale(
            Sheet.Value, 1.5, SpriteStyle.Smooth, TransparentEdge with { AlphaThreshold = 128 });

        Assert.Equal(expected.Pixels, actual.Pixels);
        Assert.All(actual.Pixels, p => Assert.True(p >> 24 is 0 or 255));
    }

    [Theory]
    [InlineData(SpriteStyle.Smooth, 1.5)]
    [InlineData(SpriteStyle.Crisp, 1.5)]
    [InlineData(SpriteStyle.Smooth, 0.375)]
    [InlineData(SpriteStyle.Smooth, 2.25)]
    public void Scale_OutputIsPremultiplied(SpriteStyle style, double scale)
    {
        foreach (TestBuiltInBulb bulb in BuiltInTestBulbs.Instance.All.Take(12))
        {
            foreach (Rgba32Image cell in bulb.Cells)
            {
                PremultipliedImage actual = PixelArtScaler.Scale(cell, scale, style);

                Assert.All(actual.Pixels, p => Assert.True(
                    Bgra32.R(p) <= Bgra32.A(p) && Bgra32.G(p) <= Bgra32.A(p) && Bgra32.B(p) <= Bgra32.A(p), $"{p:X8} in {bulb.Name}"));
            }
        }
    }

    [Theory]
    [InlineData(SpriteStyle.Smooth)]
    [InlineData(SpriteStyle.Crisp)]
    public void Scale_KeepsColourWithoutDarkFringes(SpriteStyle style)
    {
        // Every pixel of a one-colour disc is that colour times its coverage: averaging never pulls in black.
        Rgba32Image disc = Art.Disc(21, Art.Red, 8.5);

        foreach (double scale in new[] { 0.6, 1.25, 1.5, 1.75, 2.5, 3.3 })
        {
            PremultipliedImage actual = PixelArtScaler.Scale(disc, scale, style);

            Assert.Contains(actual.Pixels, p => p >> 24 is > 0 and < 255);
            foreach (uint p in actual.Pixels)
            {
                double coverage = Bgra32.A(p) / 255.0;
                Assert.InRange(Bgra32.R(p), Bgra32.R(Art.Red) * coverage - 1, Bgra32.R(Art.Red) * coverage + 1);
                Assert.InRange(Bgra32.G(p), Bgra32.G(Art.Red) * coverage - 1, Bgra32.G(Art.Red) * coverage + 1);
                Assert.InRange(Bgra32.B(p), Bgra32.B(Art.Red) * coverage - 1, Bgra32.B(Art.Red) * coverage + 1);
            }
        }
    }

    [Theory]
    [InlineData(SpriteStyle.Smooth)]
    [InlineData(SpriteStyle.Crisp)]
    public void Scale_OpaqueStaysOpaqueAndTransparentStaysTransparent(SpriteStyle style)
    {
        foreach (double scale in new[] { 0.5, 1.5, 2.5 })
        {
            Assert.All(PixelArtScaler.Scale(Art.Solid(7, 9, Art.Green), scale, style).Pixels, p => Assert.Equal(Art.Green, p));
            Assert.All(PixelArtScaler.Scale(new Rgba32Image(7, 9), scale, style).Pixels, p => Assert.Equal(0u, p));
        }
    }

    [Fact]
    public void Smooth_CanonicalizesTransparentPixelsFirst()
    {
        Rgba32Image clean = BuiltInTestBulbs.Instance["standard-bulbs"].Cells.First();
        Rgba32Image dirty = clean.Clone();
        for (int i = 0; i < dirty.Pixels.Length; i++)
        {
            if (dirty.Pixels[i] >> 24 == 0)
            {
                dirty.Pixels[i] = 0x00FF00FF + (uint)(i % 7);
            }
        }

        Assert.Equal(PixelArtScaler.Scale(clean, 1.5, SpriteStyle.Smooth).Pixels, PixelArtScaler.Scale(dirty, 1.5, SpriteStyle.Smooth).Pixels);
        Assert.Equal(PixelArtScaler.Scale(clean, 1.5, SpriteStyle.Crisp).Pixels, PixelArtScaler.Scale(dirty, 1.5, SpriteStyle.Crisp).Pixels);
    }

    [Fact]
    public void Scale_HandlesOpaquePicturesSuchAsTheAboutBanner()
    {
        Rgba32Image banner;
        using (Stream stream = EmbeddedAssets.Open(HeritageAssets.About))
        {
            banner = TestImages.Decode(stream);
        }

        PremultipliedImage actual = PixelArtScaler.Scale(banner, 1.5, SpriteStyle.Smooth);

        Assert.Equal(new SizeI(720, 141), actual.Size);
        Assert.All(actual.Pixels, p => Assert.Equal(255, Bgra32.A(p)));
    }

    [Theory]
    [InlineData(0.25, 1)]
    [InlineData(1.0, 1)]
    [InlineData(1.01, 2)]
    [InlineData(1.5, 2)]
    [InlineData(2.0, 2)]
    [InlineData(2.25, 4)]
    [InlineData(4.0, 4)]
    [InlineData(5.0, 8)]
    [InlineData(10.0, 16)]
    public void MagnificationFor_IsTheSmallestPowerOfTwoAtOrAboveTheScale(double scale, int expected) =>
        Assert.Equal(expected, PixelArtScaler.MagnificationFor(scale));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e6)]
    public void Scale_RejectsUnusableScales(double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelArtScaler.Scale(Art.Solid(2, 2, Art.Red), scale, SpriteStyle.Smooth));

    [Fact]
    public void Scale_RejectsUnknownStyles() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelArtScaler.Scale(Art.Solid(2, 2, Art.Red), 1.5, (SpriteStyle)7));

    [Fact]
    public void Scale_OfAnEmptyImageIsEmpty() =>
        Assert.Equal(new SizeI(0, 0), PixelArtScaler.Scale(new Rgba32Image(0, 0), 1.5, SpriteStyle.Smooth).Size);

    /// <summary>The prototype's references are straight alpha; the scaler returns exactly their premultiplied form.</summary>
    private static void AssertMatchesStraightReference(Rgba32Image expected, PremultipliedImage actual)
    {
        Assert.Equal(expected.Size, actual.Size);
        (int pixels, int max) = TestImages.Compare(expected.ToPremultiplied().Pixels, actual.Pixels);
        Assert.True(pixels == 0, $"{pixels} pixels differ from the prototype's reference (largest channel difference {max}).");
    }
}
