using HolidayLights.Core.Sprites;

namespace HolidayLights.Tests.Sprites;

/// <summary>Glow halos (PRODUCT-SPEC 5.9, product-owner decision 1): emissive pixels, blur, margin, additive alpha 0, energy.</summary>
public sealed class GlowBakerTests
{
    [Theory]
    [InlineData(48, 48, 1.5, 10.56)]
    [InlineData(32, 32, 1.0, 7.04)]
    [InlineData(600, 50, 1.0, 11.0)] // StringOfLights.bul: the shorter side, not 132
    [InlineData(50, 600, 1.0, 11.0)]
    [InlineData(383, 36, 1.5, 7.92)] // BirthdayBulb (255 x 24) at 150 %: not 84.26
    [InlineData(300, 300, 1.0, 24.0)] // capped at 24 art pixels
    [InlineData(450, 450, 1.5, 36.0)] // 36 pixels at 150 %
    [InlineData(100, 100, 1.0, 22.0)]
    public void SigmaFor_IsPoint22TimesTheShorterSideCappedAt24ArtPixels(int width, int height, double scale, double expected) =>
        Assert.Equal(expected, GlowBaker.SigmaFor(new SizeI(width, height), scale), 9);

    [Fact]
    public void SigmaFor_KeepsTheHaloOfALongStripCloseToItsBulbs()
    {
        // BirthdayBulb at 150 %: the halo used to be 889 x 542 (sigma 84); now it reaches 3 sigma beyond the shorter side.
        double sigma = GlowBaker.SigmaFor(new SizeI(383, 36), 1.5);
        int margin = GlowBaker.MarginFor(sigma);

        Assert.Equal(24, margin);
        Assert.Equal(new SizeI(431, 84), new SizeI(383 + 2 * margin, 36 + 2 * margin));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    public void SigmaFor_RejectsUnusableScales(double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GlowBaker.SigmaFor(new SizeI(32, 32), scale));

    [Theory]
    [InlineData(10.56, 32)]
    [InlineData(7.04, 22)]
    [InlineData(1.0, 3)]
    [InlineData(0.22, 1)]
    public void MarginFor_IsCeilingOfThreeSigma(double sigma, int expected) =>
        Assert.Equal(expected, GlowBaker.MarginFor(sigma));

    [Fact]
    public void MarginFor_IgnoresRepresentationErrorOfWholeNumbers() =>
        Assert.Equal(33, GlowBaker.MarginFor(GlowBaker.SigmaFor(new SizeI(50, 50), 1.0)));

    [Fact]
    public void Bake_ExtendsTheSpriteByTheMarginWithAlphaZeroEverywhere()
    {
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(16);
        double sigma = GlowBaker.SigmaFor(lit.Size, 1.0);
        int margin = GlowBaker.MarginFor(sigma);

        GlowSprite? glow = GlowBaker.Bake(lit.ToPremultiplied(), unlit.ToPremultiplied(), sigma);

        Assert.NotNull(glow);
        Assert.Equal(new SizeI(16 + 2 * margin, 16 + 2 * margin), glow.Image.Size);
        Assert.Equal((-margin, -margin), (glow.OffsetX, glow.OffsetY));
        Assert.All(glow.Image.Pixels, p => Assert.Equal(0, Bgra32.A(p)));
        Assert.Contains(glow.Image.Pixels, p => p != 0);
    }

    [Fact]
    public void Bake_ReturnsNullWithoutEmissivePixels()
    {
        Rgba32Image lit = Art.Disc(16, Art.Red, 6);

        Assert.Null(GlowBaker.Bake(lit.ToPremultiplied(), lit.ToPremultiplied(), 3.5));
    }

    [Theory]
    [InlineData(132, true)]
    [InlineData(131, false)]
    public void Bake_EmissiveMeansAtLeast32LumaBrighter(byte litGray, bool emissive)
    {
        // For grey pixels luma equals the grey level: 132 - 100 = 32 is emissive, 131 - 100 = 31 is not.
        Rgba32Image lit = Art.Solid(4, 4, Bgra32.Pack(litGray, litGray, litGray, 255));
        Rgba32Image unlit = Art.Solid(4, 4, Bgra32.Pack(100, 100, 100, 255));

        GlowSprite? glow = GlowBaker.Bake(lit.ToPremultiplied(), unlit.ToPremultiplied(), 1.0);

        Assert.Equal(emissive, glow is not null);
    }

    [Theory]
    [InlineData(0xFF993300u, 0xFFFF3300u, true)] // the red glass of Standard Bulbs: +30.5 luma, +102 red
    [InlineData(0xFF000099u, 0xFF0000FFu, true)] // saturated blue: +11.6 luma, +102 blue
    [InlineData(0xFF000064u, 0xFF000084u, true)] // +32 in the brightest channel
    [InlineData(0xFF000064u, 0xFF000083u, false)] // +31: neither rule
    [InlineData(0xFF0000C8u, 0xFF640096u, false)] // a hue change that is no brighter: +24.2 luma, brightest channel -50
    [InlineData(0xFFFF3300u, 0xFF993300u, false)] // darker when lit
    public void Bake_EmissiveAlsoMeansTheBrightestChannelRisesBy32(uint unlitPixel, uint litPixel, bool emissive)
    {
        Rgba32Image lit = Art.Solid(4, 4, litPixel);
        Rgba32Image unlit = Art.Solid(4, 4, unlitPixel);

        GlowSprite? glow = GlowBaker.Bake(lit.ToPremultiplied(), unlit.ToPremultiplied(), 1.0);

        Assert.Equal(emissive, glow is not null);
    }

    [Fact]
    public void Bake_EveryColourOfStandardBulbsGivesOffComparableLight()
    {
        // Regression: by luma alone the red glass (153,51,0) -> (255,51,0) was not emissive, so only 65 of the red bulb's
        // 322 opaque pixels glowed against 228 for every other colour, and red bulbs looked unlit beside them.
        TestBuiltInBulb bulb = BuiltInTestBulbs.Instance["standard-bulbs"];
        int flavors = bulb.GetFlavorCount(Side.Top);
        var emissive = new int[flavors];
        var light = new double[flavors];
        for (int flavor = 0; flavor < flavors; flavor++)
        {
            PremultipliedImage lit = bulb.GetCell(CellSlot.Top, flavor, 0).Image.ToPremultiplied();
            PremultipliedImage unlit = bulb.GetCell(CellSlot.Top, flavor, 1).Image.ToPremultiplied();
            emissive[flavor] = Enumerable.Range(0, lit.Pixels.Length).Count(i => GlowBaker.IsEmissive(lit.Pixels[i], unlit.Pixels[i]));
            GlowSprite glow = GlowBaker.Bake(lit, unlit, GlowBaker.SigmaFor(lit.Size, 1.0))!;
            light[flavor] = glow.Image.Pixels.Sum(p => (double)Bgra32.R(p) + Bgra32.G(p) + Bgra32.B(p));
        }

        Assert.Equal(5, flavors);
        Assert.All(emissive, count => Assert.Equal(emissive.Max(), count));
        Assert.True(light[0] >= 0.5 * light.Skip(1).Min(), $"red {light[0]:F0}, others {string.Join(", ", light.Skip(1).Select(l => l.ToString("F0")))}");
    }

    [Fact]
    public void Bake_UsesTheLitColoursOfEmissivePixelsOnly()
    {
        // Pure red light; the black cord row is equal in both frames and adds nothing.
        (Rgba32Image lit, Rgba32Image unlit) = Art.LightBulbPair(16);
        for (int i = 0; i < lit.Pixels.Length; i++)
        {
            if (lit.Pixels[i] == Art.Red)
            {
                lit.Pixels[i] = 0xFFFF0000;
            }
        }

        GlowSprite glow = GlowBaker.Bake(lit.ToPremultiplied(), unlit.ToPremultiplied(), 4.0)!;

        Assert.All(glow.Image.Pixels, p => Assert.Equal(0u, p & 0x0000FFFF));
        Assert.Contains(glow.Image.Pixels, p => Bgra32.R(p) > 0);
    }

    [Fact]
    public void Bake_ConservesTheEmittedLight()
    {
        var lit = new Rgba32Image(24, 24);
        var unlit = new Rgba32Image(24, 24);
        for (int y = 4; y < 20; y++)
        {
            for (int x = 4; x < 20; x++)
            {
                lit[x, y] = Bgra32.Pack(240, 180, 60, 255);
                unlit[x, y] = Bgra32.Pack(40, 30, 10, 255);
            }
        }

        GlowSprite glow = GlowBaker.Bake(lit.ToPremultiplied(), unlit.ToPremultiplied(), GlowBaker.SigmaFor(lit.Size, 1.0))!;

        double emitted = 16 * 16 * 240.0;
        double halo = glow.Image.Pixels.Sum(p => (double)Bgra32.R(p));
        Assert.InRange(halo, emitted * 0.98, emitted * 1.02);
    }

    [Fact]
    public void Bake_SpreadsAPointAsTheNormalizedGaussian()
    {
        var lit = new Rgba32Image(9, 9);
        var unlit = new Rgba32Image(9, 9);
        lit[4, 4] = 0xFFFFFFFF;
        unlit[4, 4] = 0xFF000000;
        const double sigma = 2.0;
        int margin = GlowBaker.MarginFor(sigma);
        float[] kernel = GlowBaker.GaussianKernel(sigma, margin);

        GlowSprite glow = GlowBaker.Bake(lit.ToPremultiplied(), unlit.ToPremultiplied(), sigma)!;

        int centre = 4 + margin;
        for (int dy = -margin; dy <= margin; dy++)
        {
            for (int dx = -margin; dx <= margin; dx++)
            {
                double expected = 255.0 * kernel[dx + margin] * kernel[dy + margin];
                Assert.InRange(Bgra32.G(glow.Image[centre + dx, centre + dy]), Math.Round(expected) - 1, Math.Round(expected) + 1);
                Assert.Equal(glow.Image[centre + dx, centre + dy], glow.Image[centre - dx, centre - dy]);
            }
        }

        Assert.Equal(1.0, kernel.Sum(k => (double)k), 5);
    }

    [Fact]
    public void Bake_TheStandardBulbHaloIsColouredByItsGlass()
    {
        // Standard Bulbs, red flavor, at 150 %: the halo is mostly red light (the glass), not the black socket.
        TestBuiltInBulb bulb = BuiltInTestBulbs.Instance["standard-bulbs"];
        PremultipliedImage lit = PixelArtScaler.Scale(bulb.GetCell(CellSlot.Top, 0, 0).Image, 1.5, SpriteStyle.Smooth);
        PremultipliedImage unlit = PixelArtScaler.Scale(bulb.GetCell(CellSlot.Top, 0, 1).Image, 1.5, SpriteStyle.Smooth);

        GlowSprite glow = GlowBaker.Bake(lit, unlit, GlowBaker.SigmaFor(lit.Size, 1.5))!;

        Assert.Equal(new SizeI(48 + 64, 48 + 64), glow.Image.Size);
        double red = glow.Image.Pixels.Sum(p => (double)Bgra32.R(p));
        double green = glow.Image.Pixels.Sum(p => (double)Bgra32.G(p));
        Assert.True(red > 2 * green, $"red {red}, green {green}");
    }

    [Fact]
    public void Bake_RejectsFramesOfDifferentSizes() =>
        Assert.Throws<ArgumentException>(() => GlowBaker.Bake(new PremultipliedImage(4, 4), new PremultipliedImage(4, 5), 1.0));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    public void Bake_RejectsUnusableSigmas(double sigma) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GlowBaker.Bake(new PremultipliedImage(4, 4), new PremultipliedImage(4, 4), sigma));
}
