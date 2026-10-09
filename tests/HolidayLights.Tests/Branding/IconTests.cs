using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Branding;

/// <summary>The icons of PRODUCT-SPEC 4.7: the files named by <see cref="AppAssets"/>, their sizes, format and look.</summary>
public sealed class IconTests
{
    private static readonly int[] AppSizes = [16, 20, 24, 32, 40, 48, 64, 256];
    private static readonly int[] TraySizes = [16, 20, 24, 32];

    public static TheoryData<string, int[]> IconFiles() => new()
    {
        { "HolidayLights.ico", AppSizes },
        { "BulbDocument.ico", AppSizes },
        { "Tray/TrayLitLight.ico", TraySizes },
        { "Tray/TrayLitDark.ico", TraySizes },
        { "Tray/TrayUnlitLight.ico", TraySizes },
        { "Tray/TrayUnlitDark.ico", TraySizes },
    };

    [Fact]
    public void ContractUrisNameTheIconFiles()
    {
        string[] uris = [AppAssets.AppIcon, AppAssets.TrayLitLight, AppAssets.TrayLitDark, AppAssets.TrayUnlitLight, AppAssets.TrayUnlitDark];
        Assert.All(uris, uri => Assert.True(File.Exists(AssetPath(uri["pack://application:,,,/Assets/".Length..])), uri));
    }

    [Theory]
    [MemberData(nameof(IconFiles))]
    public void IconHoldsEveryRequiredSizeAs32BitArt(string file, int[] sizes)
    {
        byte[] ico = File.ReadAllBytes(AssetPath(file));
        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        int count = BitConverter.ToUInt16(ico, 4);
        var found = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + 16 * i;
            int size = ico[entry] == 0 ? 256 : ico[entry];
            Assert.Equal(32, BitConverter.ToUInt16(ico, entry + 6));
            int offset = BitConverter.ToInt32(ico, entry + 12);
            bool png = ico[offset] == 0x89 && ico[offset + 1] == (byte)'P';
            Assert.Equal(size >= 256, png);
            found.Add(size);
        }

        Assert.Equal(sizes, found);
        StaThread.Run(() =>
        {
            BitmapDecoder decoder = Decode(file);
            Assert.Equal(sizes, decoder.Frames.Select(f => f.PixelWidth).Order());
            Assert.All(decoder.Frames, f =>
            {
                uint[] pixels = Pixels(f);
                Assert.Contains(pixels, p => p >> 24 == 0);
                Assert.Contains(pixels, p => p >> 24 == 0xFF);
            });
        });
    }

    [Fact]
    public void LitTrayBulbsAreBrighterThanUnlitOnes() => StaThread.Run(() =>
    {
        foreach (string taskbar in new[] { "Light", "Dark" })
        {
            foreach (int size in TraySizes)
            {
                double lit = GlassBrightness(Frame($"Tray/TrayLit{taskbar}.ico", size));
                double unlit = GlassBrightness(Frame($"Tray/TrayUnlit{taskbar}.ico", size));
                Assert.True(lit > unlit + 40, $"{taskbar} {size}px: lit {lit:F0}, unlit {unlit:F0}");
            }
        }
    });

    [Fact]
    public void UnlitBulbGetsALightRimOnlyOnDarkTaskbars() => StaThread.Run(() =>
    {
        foreach (int size in TraySizes)
        {
            Assert.Contains(Pixels(Frame("Tray/TrayUnlitDark.ico", size)), IsLightRim);
            Assert.DoesNotContain(Pixels(Frame("Tray/TrayUnlitLight.ico", size)), IsLightRim);
        }
    });

    [Fact]
    public void AppIconGlowsFrom48Pixels() => StaThread.Run(() =>
    {
        foreach (int size in AppSizes)
        {
            BitmapFrame frame = Frame("HolidayLights.ico", size);
            uint[] pixels = Pixels(frame);
            uint beside = pixels[(int)(size * 0.62) * size + (int)(size * 0.15)];
            Assert.True((beside >> 24 != 0) == size >= 48, $"{size}px: alpha {beside >> 24} beside the glass");
        }
    });

    [Fact]
    public void DocumentIconIsAPageWithTheBulb() => StaThread.Run(() =>
    {
        foreach (int size in AppSizes)
        {
            uint[] pixels = Pixels(Frame("BulbDocument.ico", size));
            Assert.Contains(pixels, p => p >> 24 == 0xFF && R(p) > 235 && G(p) > 235 && B(p) > 235);
            Assert.Contains(pixels, p => p >> 24 == 0xFF && R(p) > 150 && G(p) < 90 && B(p) < 90);
        }
    });

    [Fact]
    public void DocumentIconIsCopiedNextToTheProgram() =>
        Assert.Equal(File.ReadAllBytes(AssetPath("BulbDocument.ico")), File.ReadAllBytes(Path.Combine(TestPaths.OutputFolder, "Assets", "BulbDocument.ico")));

    internal static string AssetPath(string relative) =>
        Path.Combine(TestPaths.RepoRoot, "src", "HolidayLights.App", "Assets", relative.Replace('/', Path.DirectorySeparatorChar));

    private static BitmapDecoder Decode(string file)
    {
        using FileStream stream = File.OpenRead(AssetPath(file));
        return BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    }

    private static BitmapFrame Frame(string file, int size) => Decode(file).Frames.Single(f => f.PixelWidth == size);

    private static uint[] Pixels(BitmapSource frame)
    {
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new uint[converted.PixelWidth * converted.PixelHeight];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    /// <summary>Mean luma of the opaque, red-dominant pixels (the glass).</summary>
    private static double GlassBrightness(BitmapSource frame) =>
        Pixels(frame).Where(p => p >> 24 == 0xFF && R(p) > G(p) + 30 && R(p) > B(p) + 30).Average(p => 0.299 * R(p) + 0.587 * G(p) + 0.114 * B(p));

    private static bool IsLightRim(uint p) => p >> 24 is >= 20 and <= 160 && R(p) > 180 && G(p) > 180 && B(p) > 180;

    private static int R(uint p) => (int)((p >> 16) & 0xFF);

    private static int G(uint p) => (int)((p >> 8) & 0xFF);

    private static int B(uint p) => (int)(p & 0xFF);
}
