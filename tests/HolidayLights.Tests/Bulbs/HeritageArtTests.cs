using HolidayLights.Core.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

public sealed class HeritageArtTests
{
    [Fact]
    public void MaskedArt_MatchesTheReferenceDecoding()
    {
        // Hashes from Pillow with the reference exporter's mask rule (black = opaque, colour 0 where transparent).
        AssertImage(HeritageArt.Warning, 32, 32, "a977a8ab7f4c2c9e24336fd6ff153a06c85b959da8308034c95578793441662e");
        AssertImage(HeritageArt.Flakes, 45, 9, "f9a6289fe8e1ab8094b91649889a09a2872784f65ec6d9ec689a304ef68c7ac5");
        AssertImage(HeritageArt.Balloons, 126, 50, "cfa3f24883acf04b089ac52f9f699f26ab98eb0ff644f58ece81ae298e3d05ed");
        AssertImage(HeritageArt.SmallBalloons, 30, 7, "5f76e2347f5c63a8e6bd37b9ac97761c3ca21beebb9f9f491f8c238bb3d6ac74");
        Assert.Contains(HeritageArt.Warning.Pixels, p => p == 0);
        Assert.Contains(HeritageArt.Warning.Pixels, p => Bgra32.A(p) == 255);
    }

    [Fact]
    public void OpaqueArt_MatchesTheReferenceDecoding()
    {
        AssertImage(HeritageArt.Dither, 8, 8, "3281c54ad8ad2f9305c8847637abbbf68ebfb479202e4399ca09f3ca3bc745da");
        AssertImage(HeritageArt.AboutBanner, 480, 94, "a793435566f15402aa0bb68d9e76ab3593f72075c90e1badad3698d34010bcf1");
        Assert.Equal(2, HeritageArt.AboutFlash.Count);
        AssertImage(HeritageArt.AboutFlash[0], 296, 15, "6f753c5d4efe2c410474e1827d5472962ad1c04ec82192687f1a8eed85d23921");
        AssertImage(HeritageArt.AboutFlash[1], 296, 15, "a53155bc34c6eceb3a3ea4cb67615b26d20258fc2027693ed431b55745de454e");
        Assert.All(HeritageArt.AboutBanner.Pixels, p => Assert.Equal(255, Bgra32.A(p)));
        Assert.All(HeritageArt.Dither.Pixels, p => Assert.True(p is 0xFF000000 or 0xFFFFFFFF));
    }

    [Fact]
    public void Images_AreDecodedOnce()
    {
        Assert.Same(HeritageArt.Warning, HeritageArt.Warning);
        Assert.Same(HeritageArt.AboutFlash, HeritageArt.AboutFlash);
    }

    private static void AssertImage(Rgba32Image image, int width, int height, string sha256)
    {
        Assert.Equal(new SizeI(width, height), image.Size);
        Assert.Equal(sha256, GoldenData.RgbaSha256(image));
    }
}
