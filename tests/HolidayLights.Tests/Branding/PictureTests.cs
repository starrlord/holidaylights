using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Branding;

/// <summary>
/// The 2003 banners enlarged with MMPX (PRODUCT-SPEC 3.9, 4.3.5) and the pictures of the Help topics (drawn at twice
/// their display size, 192 DPI).
/// </summary>
public sealed class PictureTests
{
    /// <summary>The edition line added to the About banner, in banner pixels (x, y, width, height at 4x).</summary>
    private static readonly (int X, int Y, int Width, int Height) EditionArea = (336 * 4, 70 * 4, 114 * 4, 16 * 4);

    public static TheoryData<string, string, int, int> Banners() => new()
    {
        { "AboutBanner.png", "bitmaps/ABOUT.bmp", 480, 94 },
        { "AboutFlash1.png", "bitmaps/ABOUTFLASH1.bmp", 296, 15 },
        { "AboutFlash2.png", "bitmaps/ABOUTFLASH2.bmp", 296, 15 },
        { "HelpBanner.png", "help/bm0.png", 216, 53 },
    };

    [Fact]
    public void ContractUrisNameTheBannerAndTooltipFiles()
    {
        string[] uris = [AppAssets.AboutBanner, AppAssets.AboutFlash1, AppAssets.AboutFlash2, AppAssets.HelpBanner, AppAssets.TooltipsDictionary];
        Assert.All(uris, uri => Assert.True(File.Exists(IconTests.AssetPath(uri["pack://application:,,,/Assets/".Length..])), uri));
    }

    [Theory]
    [MemberData(nameof(Banners))]
    public void BannerIsTheHeritageArtEnlargedFourTimes(string file, string heritage, int width, int height) => StaThread.Run(() =>
    {
        BitmapSource banner = Load(IconTests.AssetPath($"Banner/{file}"));
        BitmapSource original = Load(Path.Combine(TestPaths.RepoRoot, "assets", "heritage", heritage.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal((width, height), (original.PixelWidth, original.PixelHeight));
        Assert.Equal((width * 4, height * 4), (banner.PixelWidth, banner.PixelHeight));

        // MMPX only copies colours of the art, so the enlargement has no colour the 2003 bitmap lacks.
        var palette = new HashSet<uint>(Pixels(original));
        uint[] big = Pixels(banner);
        for (int y = 0; y < banner.PixelHeight; y++)
        {
            for (int x = 0; x < banner.PixelWidth; x++)
            {
                bool edition = file == "AboutBanner.png" && x >= EditionArea.X && x < EditionArea.X + EditionArea.Width && y >= EditionArea.Y && y < EditionArea.Y + EditionArea.Height;
                Assert.True(edition || palette.Contains(big[y * banner.PixelWidth + x]), $"{file}: new colour at {x},{y}");
            }
        }
    });

    [Fact]
    public void AboutBannerSaysModernEdition() => StaThread.Run(() =>
    {
        uint[] banner = Pixels(Load(IconTests.AssetPath("Banner/AboutBanner.png")));
        uint[] original = Pixels(Load(Path.Combine(TestPaths.RepoRoot, "assets", "heritage", "bitmaps", "ABOUT.bmp")));
        int navy = 0;
        for (int y = EditionArea.Y; y < EditionArea.Y + EditionArea.Height; y++)
        {
            for (int x = EditionArea.X; x < EditionArea.X + EditionArea.Width; x++)
            {
                uint p = banner[y * 1920 + x];
                if ((p & 0xFF) > 100 && ((p >> 16) & 0xFF) < 60 && ((p >> 8) & 0xFF) < 60)
                {
                    navy++;
                }
            }
        }

        // The edition line sits on sky that was empty in 2003, right of the "g" and under "Lights".
        Assert.Equal(0xFFDDEEFFu, original[78 * 480 + 400]);
        Assert.InRange(navy, 2000, 60000);
    });

    [Fact]
    public void HelpPicturesAreDrawnAtTwiceTheirSize() => StaThread.Run(() =>
    {
        string[] pictures = [.. HelpTopicTests.EmbeddedHelpFiles("images/")];

        Assert.Equal(5, pictures.Length);
        Assert.All(pictures, name =>
        {
            using Stream stream = HelpContentStore.Open(name);
            BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal(192, frame.DpiX, 0);
            Assert.Equal(192, frame.DpiY, 0);
            Assert.Equal(0, frame.PixelWidth % 2);
            Assert.InRange(frame.Width, 200, 700);
        });
    });

    private static BitmapSource Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    }

    private static uint[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new uint[converted.PixelWidth * converted.PixelHeight];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }
}
