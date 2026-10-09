using System.Diagnostics;
using HolidayLights.App.ScreenSaver.Rendering;
using HolidayLights.App.ScreenSaver.Scene;
using HolidayLights.App.ScreenSaver.Simulation;
using HolidayLights.Core.Sprites;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.ScreenSaver;

/// <summary>
/// The background picture as one layer of the part on screen: a panorama larger than any GPU surface still shows (its
/// visible part), and a small tiled pattern becomes one image instead of one piece per tile.
/// </summary>
public sealed class PictureLayerTests
{
    private static readonly SaverField Reference = new(2560, 1440);
    private static readonly SaverPixels ReferencePixels = new(1.5, true);
    private static readonly RectI ReferenceScreen = new(0, 0, 3840, 2160);

    [Fact]
    public void Panorama_WiderThanAnySurface_BecomesALayerNoLargerThanTheScreen_WithTheRightPixels()
    {
        // 12000 x 800 centred on the reference display is 18000 x 1200 pixels: wider than the 16384 a surface may be.
        Rgba32Image panorama = Gradient(12000, 800);
        var picture = new SaverPicture(panorama, false, SaverLayoutRules.PlacePicture(Reference, panorama.Size, PicturePlacement.Center));
        Assert.Equal(RectI.FromXYWH(-7080, 320, 18000, 1200), ReferencePixels.Snap(picture.Layout.Rectangles[0]));

        PlacedImage? layer = null;
        StaThread.Run(() => layer = PictureLayer.Compose(picture, ReferencePixels, ReferenceScreen, SpriteStyle.Smooth, new CpuCompositor()));

        Assert.NotNull(layer);
        Assert.Equal(new PointI(0, 320), layer.At);
        Assert.Equal(new SizeI(3840, 1200), layer.Image.Size);
        foreach ((int x, int y) in new[] { (0, 0), (1920, 600), (3839, 1199), (100, 1100) })
        {
            // Screen pixel x lands on picture pixel (x + 7080) / 1.5.
            uint expected = panorama[(int)((x + 7080) / 1.5), (int)(y / 1.5)];
            AssertClose(expected, layer.Image[x, y], 3);
        }
    }

    [Fact]
    public void ScaleRegion_MatchesThePartOfTheWholeScaledPicture()
    {
        Rgba32Image picture = Gradient(400, 300);
        var size = new SizeI(600, 450);
        var region = new RectI(100, 50, 400, 300);
        PremultipliedImage whole = null!;
        PremultipliedImage part = null!;
        StaThread.Run(() =>
        {
            whole = PictureScaler.Scale(picture, size, false, SpriteStyle.Smooth);
            part = PictureScaler.ScaleRegion(picture, size, region, false, SpriteStyle.Smooth);
        });

        Assert.Equal(region.Size, part.Size);
        for (int y = 0; y < region.Height; y += 7)
        {
            for (int x = 0; x < region.Width; x += 7)
            {
                AssertClose(whole[region.Left + x, region.Top + y], part[x, y], 3);
            }
        }
    }

    [Fact]
    public void TinyTiledPattern_IsOneLayerOverTheWholeScreen()
    {
        var tile = new Rgba32Image(8, 8);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                tile[x, y] = Bgra32.Pack((byte)(x * 32), (byte)(y * 32), 200, 255);
            }
        }

        var picture = new SaverPicture(tile, false, SaverLayoutRules.PlacePicture(Reference, tile.Size, PicturePlacement.Tile));
        Assert.Equal(57600, picture.Layout.Rectangles.Count);

        PlacedImage? layer = null;
        var watch = Stopwatch.StartNew();
        StaThread.Run(() => layer = PictureLayer.Compose(picture, ReferencePixels, ReferenceScreen, SpriteStyle.Smooth, new CpuCompositor()));
        watch.Stop();

        Assert.NotNull(layer);
        Assert.Equal(new PointI(0, 0), layer.At);
        Assert.Equal(new SizeI(3840, 2160), layer.Image.Size);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Composing the tiles took {watch.Elapsed}.");
        foreach ((int x, int y) in new[] { (0, 0), (5, 7), (1000, 999), (3800, 2100), (3827, 2147) })
        {
            // Every tile is 12 x 12 pixels and the same.
            Assert.Equal(255u, Bgra32.A(layer.Image[x, y]));
            Assert.Equal(layer.Image[x % 12, y % 12], layer.Image[x, y]);
        }
    }

    [Fact]
    public void PictureOutsideTheTarget_GivesNoLayer()
    {
        Rgba32Image picture = Gradient(100, 100);
        var layout = new SaverPicture(picture, false, SaverLayoutRules.PlacePicture(Reference, picture.Size, PicturePlacement.Center));

        PlacedImage? layer = PictureLayer.Compose(layout, ReferencePixels, new RectI(0, 0, 50, 50), SpriteStyle.Smooth, new CpuCompositor());

        Assert.Null(layer);
    }

    /// <summary>An opaque picture whose red follows x and green follows y.</summary>
    private static Rgba32Image Gradient(int width, int height)
    {
        var image = new Rgba32Image(width, height);
        for (int y = 0; y < height; y++)
        {
            byte green = (byte)(y * 255 / Math.Max(1, height - 1));
            Span<uint> row = image.GetRow(y);
            for (int x = 0; x < width; x++)
            {
                row[x] = Bgra32.Pack((byte)(x * 255L / Math.Max(1, width - 1)), green, 128, 255);
            }
        }

        return image;
    }

    private static void AssertClose(uint expected, uint actual, int tolerance)
    {
        Assert.True(
            Math.Abs(Bgra32.R(expected) - Bgra32.R(actual)) <= tolerance && Math.Abs(Bgra32.G(expected) - Bgra32.G(actual)) <= tolerance
            && Math.Abs(Bgra32.B(expected) - Bgra32.B(actual)) <= tolerance && Math.Abs(Bgra32.A(expected) - Bgra32.A(actual)) <= tolerance,
            $"Expected about {expected:X8}, got {actual:X8}.");
    }
}
