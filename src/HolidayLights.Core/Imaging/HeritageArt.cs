namespace HolidayLights.Core.Imaging;

/// <summary>
/// The decoded original 5.4 art of <c>assets/heritage</c> (see <see cref="HeritageAssets"/>), decoded once and cached.
/// Owner: core-bulbs. Callers must not modify the returned images.
/// </summary>
/// <remarks>Thread-safe: each picture is decoded on first use by exactly one thread.</remarks>
public static class HeritageArt
{
    private static readonly Lazy<Rgba32Image> WarningImage = new(() => Masked(HeritageAssets.Warning, HeritageAssets.WarningMask));
    private static readonly Lazy<Rgba32Image> DitherImage = new(() => Opaque(HeritageAssets.Dither));
    private static readonly Lazy<Rgba32Image> FlakesImage = new(() => Masked(HeritageAssets.Flake, HeritageAssets.FlakeMask));
    private static readonly Lazy<Rgba32Image> BalloonsImage = new(() => Masked(HeritageAssets.Balloon, HeritageAssets.BalloonMask));
    private static readonly Lazy<Rgba32Image> SmallBalloonsImage =
        new(() => Masked(HeritageAssets.BalloonSmall, HeritageAssets.BalloonSmallMask));

    private static readonly Lazy<Rgba32Image> AboutBannerImage = new(() => Opaque(HeritageAssets.About));
    private static readonly Lazy<IReadOnlyList<Rgba32Image>> AboutFlashImages =
        new(() => [Opaque(HeritageAssets.AboutFlash1), Opaque(HeritageAssets.AboutFlash2)]);

    /// <summary>The 5.4 <c>WARNING</c> picture with its mask (32 x 32): damaged or undecodable bulb art.</summary>
    public static Rgba32Image Warning => WarningImage.Value;

    /// <summary>The 8 x 8 <c>DITHER</c> checkerboard as an opaque black-and-white image (Bulb Editing white square).</summary>
    public static Rgba32Image Dither => DitherImage.Value;

    /// <summary>The <c>FLAKE</c> strip with its mask: five 9 x 9 flakes (Snow Flakes module).</summary>
    public static Rgba32Image Flakes => FlakesImage.Value;

    /// <summary>The <c>BALLOON</c> strip with its mask: six 21 x 50 balloons (Balloons module).</summary>
    public static Rgba32Image Balloons => BalloonsImage.Value;

    /// <summary>The <c>BALLOONSMALL</c> strip with its mask: six 5 x 7 balloons (5.4 preview art).</summary>
    public static Rgba32Image SmallBalloons => SmallBalloonsImage.Value;

    /// <summary>The <c>ABOUT</c> banner (480 x 94, opaque).</summary>
    public static Rgba32Image AboutBanner => AboutBannerImage.Value;

    /// <summary>The two <c>ABOUTFLASH</c> strips (296 x 15, opaque) that alternate every 500 ms.</summary>
    public static IReadOnlyList<Rgba32Image> AboutFlash => AboutFlashImages.Value;

    private static Rgba32Image Opaque(string asset) => BmpDecoder.Decode(EmbeddedAssets.ReadAllBytes(asset));

    private static Rgba32Image Masked(string art, string mask) =>
        BmpDecoder.DecodeMasked(EmbeddedAssets.ReadAllBytes(art), EmbeddedAssets.ReadAllBytes(mask));
}
