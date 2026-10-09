using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Tests.Bulbs;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.BulbFactory;

/// <summary>PNG files and GIFs with particular decoding behaviour for the Bulb Editing tests.</summary>
internal static class TestPictures
{
    /// <summary>
    /// A 2 x 1 GIF whose second frame stores only the pixel that changes and marks the other transparent: a standard
    /// viewer shows the first frame through it, the faithful 5.4 decoder punches a hole (the GIF tip applies).
    /// </summary>
    public static byte[] OptimizedGif { get; } = TestGif.Encode(2, 1, [(0, 0, 0), (255, 0, 0), (0, 0, 255)],
    [
        new TestGifFrame { Width = 2, Height = 1, Pixels = [1, 1], TransparentIndex = 0, Disposal = 1 },
        new TestGifFrame { Width = 2, Height = 1, Pixels = [0, 2], TransparentIndex = 0, Disposal = 1 },
    ]);

    /// <summary>Writes a picture as a PNG file (Windows Imaging, on an STA thread).</summary>
    /// <param name="path">The file.</param>
    /// <param name="image">The picture (straight alpha).</param>
    /// <returns><paramref name="path"/>.</returns>
    public static string WritePng(string path, Rgba32Image image)
    {
        StaThread.Run(() =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4)));
            using FileStream file = File.Create(path);
            encoder.Save(file);
        });
        return path;
    }
}
