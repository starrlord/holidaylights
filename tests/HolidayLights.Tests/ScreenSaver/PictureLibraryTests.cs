using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.App.ScreenSaver;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.ScreenSaver;

public sealed class PictureLibraryTests : IDisposable
{
    private readonly TempDataRoot root = new();
    private readonly InMemorySettingsStore settings = new();
    private readonly TestHoldingFolder holding;
    private readonly PictureLibrary library;
    private readonly string sources;

    public PictureLibraryTests()
    {
        holding = new TestHoldingFolder(root.Paths);
        library = new PictureLibrary(root.Paths, settings, holding, new RecordingLog());
        sources = Directory.CreateDirectory(Path.Combine(root.Root, "Sources")).FullName;
    }

    public void Dispose()
    {
        library.Dispose();
        root.Dispose();
    }

    [Fact]
    public void Start_ListsThe11BundledPicturesAToZ()
    {
        library.Start();

        Assert.Equal(
            ["Cake", "Easter Bunny", "Flag", "Hearts", "New Year Clock", "Pot of Gold", "Pumpkin", "Santa Candle", "Snowman", "Turkey", "Witch"],
            library.Pictures.Select(p => p.Title));
        Assert.All(library.Pictures, p => Assert.Equal(MediaOrigin.Bundled, p.Origin));
        Assert.Contains(library.Pictures, p => p.Id == SaverPictures.Default);

        // My Pictures is created on first use (PRODUCT-SPEC 6.10; review r1 #55), not to be watched.
        Assert.False(Directory.Exists(root.Paths.MyPicturesFolder));
    }

    [Fact]
    public void AFolderCreatedLater_IsWatched_AndItsPicturesAppear()
    {
        library.Start();
        Assert.False(Directory.Exists(root.Paths.MyPicturesFolder));

        Directory.CreateDirectory(root.Paths.MyPicturesFolder);
        File.Copy(WritePng("Later.png", Colors.Tomato, 12, 12), Path.Combine(root.Paths.MyPicturesFolder, "Later.png"));

        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!library.Pictures.Any(p => p.Id == "user:Later.png") && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }

        Assert.Contains(library.Pictures, p => p.Id == "user:Later.png");
    }

    [Theory]
    [InlineData("Santa Candle", 282, 297)]
    [InlineData("Snowman", 300, 256)]
    [InlineData("Cake", 200, 221)]
    public void BundledPictures_DecodeTheSecondBitmap_NotTheLicenceNotice(string title, int width, int height)
    {
        library.Start();
        Rgba32Image? image = library.LoadImage(MediaIds.Bundled(title + ".BMP"));
        Assert.NotNull(image);
        Assert.Equal(new SizeI(width, height), image.Size);
        Assert.True(image.Pixels.Distinct().Count() > 16);
    }

    [Fact]
    public void AddFiles_CopiesNewPictures_KeepsOneCopyOfTheSameContent_AndRenamesClashes()
    {
        library.Start();
        string photo = WritePng("Photo.png", Colors.SteelBlue, 40, 30);
        string same = CopyTo(photo, "Copy of photo.png");
        string clash = WritePng(Path.Combine("Other", "Photo.png"), Colors.Gold, 10, 10);
        string text = Path.Combine(sources, "notes.txt");
        File.WriteAllText(text, "not a picture");

        IReadOnlyList<MediaImportResult> results = library.AddFiles([photo, same, clash, text]);

        Assert.Equal(new MediaImportResult(photo, MediaImportOutcome.Added, "user:Photo.png", null), results[0]);
        Assert.Equal(new MediaImportResult(same, MediaImportOutcome.AlreadyPresent, "user:Photo.png", null), results[1]);
        Assert.Equal(new MediaImportResult(clash, MediaImportOutcome.Renamed, "user:Photo (2).png", null), results[2]);
        Assert.Equal(MediaImportOutcome.Unsupported, results[3].Outcome);
        Assert.Equal(["Photo", "Photo (2)"], library.Pictures.Where(p => p.Origin == MediaOrigin.User).Select(p => p.Title));
        Assert.Equal(new SizeI(40, 30), library.LoadImage("user:Photo.png")!.Size);
    }

    [Fact]
    public void AddingABundledPictureAgain_FindsTheBundledOne()
    {
        library.Start();
        string copy = CopyTo(Path.Combine(TestPaths.ContentFolder, "Pictures", "Pumpkin.BMP"), "My Pumpkin.bmp");
        MediaImportResult result = Assert.Single(library.AddFiles([copy]));
        Assert.Equal(MediaImportOutcome.AlreadyPresent, result.Outcome);
        Assert.Equal("bundled:Pumpkin.BMP", result.Id);
    }

    [Fact]
    public void RemovingABundledPicture_HidesIt_AndRestoreBringsItBack()
    {
        library.Start();
        int changes = 0;
        library.Changed += (_, _) => changes++;

        Assert.Null(library.Remove("bundled:Witch.BMP"));

        Assert.DoesNotContain(library.Pictures, p => p.Title == "Witch");
        Assert.Equal(["bundled:Witch.BMP"], settings.Current.Pictures.Hidden);
        Assert.Equal("Remove Witch", settings.History[^1].Description);
        Assert.True(library.TryGetPicture("bundled:Witch.BMP", out PictureInfo? hidden));
        Assert.NotNull(library.LoadImage(hidden.Id));

        library.RestoreHiddenPictures();

        Assert.Contains(library.Pictures, p => p.Title == "Witch");
        Assert.Empty(settings.Current.Pictures.Hidden);
        Assert.True(changes >= 2);
    }

    [Fact]
    public void RemovingYourPicture_MovesItToTheHoldingFolder_AndRestoreBringsItBack()
    {
        library.Start();
        library.AddFiles([WritePng("Tree.png", Colors.ForestGreen, 8, 8)]);
        string file = Path.Combine(root.Paths.MyPicturesFolder, "Tree.png");

        HeldItem held = Assert.IsType<HeldItem>(library.Remove("user:Tree.png"));

        Assert.False(File.Exists(file));
        Assert.DoesNotContain(library.Pictures, p => p.Id == "user:Tree.png");
        Assert.False(library.TryGetPicture("user:Tree.png", out _));
        Assert.Null(library.LoadImage("user:Tree.png"));

        library.Restore(held);

        Assert.True(File.Exists(file));
        Assert.Contains(library.Pictures, p => p.Id == "user:Tree.png");
    }

    [Fact]
    public void PicturesCopiedInByHand_AppearThroughTheWatcher()
    {
        Directory.CreateDirectory(root.Paths.MyPicturesFolder);
        library.Start();
        using var changed = new ManualResetEventSlim();
        library.Changed += (_, _) => changed.Set();

        File.Copy(WritePng("Lights.png", Colors.Red, 4, 4), Path.Combine(root.Paths.MyPicturesFolder, "Lights.png"));

        Assert.True(changed.Wait(TimeSpan.FromSeconds(10)));
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!library.Pictures.Any(p => p.Id == "user:Lights.png") && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }

        Assert.Contains(library.Pictures, p => p.Id == "user:Lights.png");
    }

    [Fact]
    public void UnknownOrMissingPictures_GiveNoImage()
    {
        library.Start();
        Assert.Null(library.LoadImage(SaverPictures.None));
        Assert.Null(library.LoadImage("user:Gone.png"));
        Assert.False(library.TryGetPicture("bundled:Nothing.BMP", out _));
    }

    [Fact]
    public void AnUndecodableFile_GivesNoImage()
    {
        Directory.CreateDirectory(root.Paths.MyPicturesFolder);
        File.WriteAllBytes(Path.Combine(root.Paths.MyPicturesFolder, "Broken.jpg"), [0xFF, 0xD8, 0x00, 0x01, 0x02]);
        library.Start();

        Assert.True(library.TryGetPicture("user:Broken.jpg", out _));
        Assert.Null(library.LoadImage("user:Broken.jpg"));
    }

    [Fact]
    public void JpegPhotos_AreTurnedUprightByTheirExifOrientation()
    {
        library.Start();
        string photo = WriteJpegWithOrientation("Portrait.jpg", width: 60, height: 20, orientation: 6);
        library.AddFiles([photo]);

        Rgba32Image? image = library.LoadImage("user:Portrait.jpg");

        Assert.NotNull(image);
        Assert.Equal(new SizeI(20, 60), image.Size);
    }

    private string CopyTo(string source, string name)
    {
        string target = Path.Combine(sources, name);
        File.Copy(source, target);
        return target;
    }

    private string WritePng(string relativePath, Color color, int width, int height)
    {
        string path = Path.Combine(sources, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Solid(color, width, height)));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private string WriteJpegWithOrientation(string name, int width, int height, ushort orientation)
    {
        string path = Path.Combine(sources, name);
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Solid(Colors.Orange, width, height), null, metadata, null));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static BitmapSource Solid(Color color, int width, int height)
    {
        uint pixel = (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(pixel, width * height).ToArray(), width * 4);
    }
}
