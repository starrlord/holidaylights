using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Sprites;

/// <summary>Image files for the sprite tests: PNG and BMP decoding and PNG writing through WPF imaging.</summary>
internal static class TestImages
{
    /// <summary>
    /// The reference images of the upscaling prototype (<c>Sprites/Golden/upscale</c>): <c>1x.png</c> (the bulb sheet
    /// 1001/1002) and its MMPX, nearest-neighbour and area-averaging results that the scaler must reproduce.
    /// </summary>
    public static string UpscaleResult(string name) => SpritesGolden(Path.Combine("upscale", name));

    /// <summary>A golden image of this test area (<c>tests/HolidayLights.Tests/Sprites/Golden</c>).</summary>
    public static string SpritesGolden(string name) =>
        Path.Combine(TestPaths.RepoRoot, "tests", "HolidayLights.Tests", "Sprites", "Golden", name);

    /// <summary>Decodes an image file to straight-alpha BGRA.</summary>
    public static Rgba32Image Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Decode(stream);
    }

    /// <summary>Decodes an image stream (PNG, BMP) to straight-alpha BGRA.</summary>
    public static Rgba32Image Decode(Stream stream)
    {
        BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new uint[bgra.PixelWidth * bgra.PixelHeight];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return new Rgba32Image(bgra.PixelWidth, bgra.PixelHeight, pixels);
    }

    /// <summary>Reads an image whose pixels are all opaque or canonical transparent as premultiplied pixels (they are the same).</summary>
    public static PremultipliedImage LoadPremultiplied(string path) => Load(path).ToPremultiplied();

    /// <summary>Writes a premultiplied image as a PNG (straight alpha; additive light with alpha 0 is lost).</summary>
    public static void SavePng(string path, PremultipliedImage image)
    {
        var straight = new uint[image.Pixels.Length];
        for (int i = 0; i < straight.Length; i++)
        {
            straight[i] = Bgra32.Unpremultiply(image.Pixels[i]);
        }

        BitmapSource source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, straight, image.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Counts the pixels that differ and the largest channel difference.</summary>
    public static (int Pixels, int MaxChannel) Compare(uint[] expected, uint[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        int pixels = 0;
        int max = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] == actual[i])
            {
                continue;
            }

            pixels++;
            for (int shift = 0; shift < 32; shift += 8)
            {
                max = Math.Max(max, Math.Abs((int)((expected[i] >> shift) & 0xFF) - (int)((actual[i] >> shift) & 0xFF)));
            }
        }

        return (pixels, max);
    }

    /// <summary>Asserts two images are identical; on failure writes the actual image to a temporary folder for inspection.</summary>
    public static void AssertIdentical(BgraPixelBuffer expected, PremultipliedImage actual, string name)
    {
        Assert.Equal(expected.Size, actual.Size);
        (int pixels, int max) = Compare(expected.Pixels, actual.Pixels);
        if (pixels != 0)
        {
            Assert.Fail($"{pixels} pixels differ (largest channel difference {max}); the actual image is {SaveActual(name, actual)}.");
        }
    }

    /// <summary>
    /// Asserts an opaque image equals a golden of this test area. A missing golden fails with the actual image saved for
    /// inspection (copy it to <c>Sprites/Golden</c> once it is verified).
    /// </summary>
    public static void AssertMatchesGolden(string goldenName, PremultipliedImage actual)
    {
        string path = SpritesGolden(goldenName);
        if (!File.Exists(path))
        {
            Assert.Fail($"The golden {goldenName} is missing; the actual image is {SaveActual(goldenName, actual)}.");
        }

        AssertIdentical(LoadPremultiplied(path), actual, goldenName);
    }

    private static string SaveActual(string name, PremultipliedImage actual)
    {
        string path = Path.Combine(Path.GetTempPath(), "HolidayLightsTests", "Sprites", Path.GetFileNameWithoutExtension(name) + ".actual.png");
        SavePng(path, actual);
        return path;
    }
}
