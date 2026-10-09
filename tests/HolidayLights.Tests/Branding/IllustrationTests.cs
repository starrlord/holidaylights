using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Branding;

/// <summary>
/// <c>Assets/Illustrations.xaml</c> as App.xaml merges it: the <c>AppResourceKeys.Illustration*</c> drawings (PRODUCT-SPEC
/// 3.7, 3.11) and the merged tooltip texts.
/// </summary>
public sealed class IllustrationTests
{
    public static TheoryData<string, double, double> Illustrations() => new()
    {
        { AppResourceKeys.IllustrationBehindIcons, 160, 96 },
        { AppResourceKeys.IllustrationInFrontOfIcons, 160, 96 },
        { AppResourceKeys.IllustrationOnTop, 160, 96 },
        { AppResourceKeys.IllustrationTaskbarCornerLight, 320, 120 },
        { AppResourceKeys.IllustrationTaskbarCornerDark, 320, 120 },
    };

    [Theory]
    [MemberData(nameof(Illustrations))]
    public void IllustrationIsAFullDrawingOfItsSize(string key, double width, double height) => StaThread.Run(() =>
    {
        var image = Assert.IsType<DrawingImage>(LoadDictionary()[key]);
        Assert.Equal(width, image.Width, 3);
        Assert.Equal(height, image.Height, 3);

        // Opaque from corner to corner except the rounded corners of the picture.
        uint[] pixels = Render(image, (int)width, (int)height);
        int w = (int)width;
        Assert.Equal(0xFFu, pixels[(int)(height / 2) * w + w / 2] >> 24);
        Assert.Equal(0xFFu, pixels[10 * w + 10] >> 24);
        Assert.Equal(0u, pixels[0] >> 24);
    });

    [Fact]
    public void LayerModePicturesDifferOnlyInWhatCoversWhat() => StaThread.Run(() =>
    {
        ResourceDictionary dictionary = LoadDictionary();
        uint[] behind = Render((DrawingImage)dictionary[AppResourceKeys.IllustrationBehindIcons], 160, 96);
        uint[] inFront = Render((DrawingImage)dictionary[AppResourceKeys.IllustrationInFrontOfIcons], 160, 96);
        uint[] onTop = Render((DrawingImage)dictionary[AppResourceKeys.IllustrationOnTop], 160, 96);

        // The folder icon at the top-left: behind the bulbs in front of the icons, in front of them otherwise.
        Assert.True(Differences(behind, inFront, new Int32Rect(4, 4, 24, 46)) > 20);
        Assert.Equal(0, Differences(inFront, onTop, new Int32Rect(4, 4, 24, 46)));

        // The window at the top-right: only "On Top" draws bulbs over it.
        Assert.Equal(0, Differences(behind, inFront, new Int32Rect(86, 10, 70, 54)));
        Assert.True(Differences(inFront, onTop, new Int32Rect(86, 10, 70, 54)) > 20);
    });

    [Fact]
    public void TaskbarCornerFollowsTheWindowsMode() => StaThread.Run(() =>
    {
        ResourceDictionary dictionary = LoadDictionary();
        uint[] light = Render((DrawingImage)dictionary[AppResourceKeys.IllustrationTaskbarCornerLight], 320, 120);
        uint[] dark = Render((DrawingImage)dictionary[AppResourceKeys.IllustrationTaskbarCornerDark], 320, 120);
        int taskbar = 110 * 320 + 40;

        Assert.True(Luma(light[taskbar]) > 200);
        Assert.True(Luma(dark[taskbar]) < 60);
    });

    /// <summary>Loads the dictionary through its pack URI, exactly as App.xaml does.</summary>
    internal static ResourceDictionary LoadDictionary() =>
        (ResourceDictionary)Application.LoadComponent(new Uri("/HolidayLights;component/Assets/Illustrations.xaml", UriKind.Relative));

    private static uint[] Render(DrawingImage image, int width, int height)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var pixels = new uint[width * height];
        new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static int Differences(uint[] a, uint[] b, Int32Rect area)
    {
        int count = 0;
        for (int y = area.Y; y < area.Y + area.Height; y++)
        {
            for (int x = area.X; x < area.X + area.Width; x++)
            {
                if (a[y * 160 + x] != b[y * 160 + x])
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static double Luma(uint p) => 0.299 * ((p >> 16) & 0xFF) + 0.587 * ((p >> 8) & 0xFF) + 0.114 * (p & 0xFF);
}
