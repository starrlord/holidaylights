using HolidayLights.App.BulbFactory;
using HolidayLights.Core.Imaging;
using HolidayLights.Tests.Shared;
using static HolidayLights.Tests.BulbFactory.WritingTestData;

namespace HolidayLights.Tests.BulbFactory;

public sealed class PictureImportTests : IDisposable
{
    private readonly TempDataRoot root = new();

    public void Dispose() => root.Dispose();

    [Fact]
    public void FileNames_SortLikeExplorer()
    {
        string[] names = ["frame10.png", "Frame2.png", "frame1.png", "frame02b.png", "a.png", "frame.png"];

        Assert.Equal(["a.png", "frame.png", "frame1.png", "Frame2.png", "frame02b.png", "frame10.png"], names.Order(NaturalFileNameComparer.Instance));
    }

    [Fact]
    public async Task APng_BecomesAStillGif_OnAPoolThread()
    {
        Rgba32Image picture = Solid(5, 3, Green);
        picture.Pixels[0] = 0;
        string path = TestPictures.WritePng(Path.Combine(root.Root, "holly.png"), picture);

        PictureImportResult result = await Task.Run(() => PictureImport.Load([path]));

        Assert.Null(result.Error);
        GifAnimation decoded = GifDecoder.DecodeClassic(result.Gif!.Bytes.Span);
        Assert.Equal(picture.Pixels, Assert.Single(decoded.Frames).Pixels);
    }

    [Fact]
    public void PicturesLargerThan1024_AreRefused()
    {
        string path = TestPictures.WritePng(Path.Combine(root.Root, "huge.png"), Solid(1025, 2, Red));

        PictureImportResult result = PictureImport.Load([path]);

        Assert.Null(result.Gif);
        Assert.Equal($"Cannot Import Picture: Pictures for bulbs can be at most {1024:N0} x {1024:N0} pixels.", result.Error);
    }

    [Fact]
    public void ADamagedPng_IsRefusedWithThePictureMessage()
    {
        string path = Path.Combine(root.Root, "broken.png");
        File.WriteAllBytes(path, [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3]);

        Assert.Equal(
            "Cannot Import Picture: A problem occurred when importing the picture. It cannot be used with this bulb.",
            PictureImport.Load([path]).Error);
    }

    [Fact]
    public void AGifThatIsTooBig_IsRefusedBeforeItIsRead()
    {
        string path = Path.Combine(root.Root, "big.gif");
        using (FileStream file = File.Create(path))
        {
            file.SetLength((PictureImport.MaxGifMegabytes * 1024L * 1024L) + 1);
        }

        Assert.Equal("Cannot Import GIF File: This GIF is too big for a bulb. Bulb animations can be at most 16 MB.", PictureImport.Load([path]).Error);
    }
}
