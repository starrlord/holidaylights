using System.Buffers.Binary;
using HolidayLights.Core.Bulbs;

namespace HolidayLights.Tests.Bulbs;

public sealed class BulbCatalogFileTests : IDisposable
{
    private readonly CatalogHarness harness;

    public BulbCatalogFileTests()
    {
        harness = new CatalogHarness(CatalogHarness.SmallBundledSet, new AppSettings { Bulbs = new BulbPreferences { Hidden = ["addon:Snow2"] } });
        harness.Start();
        harness.ClearEvents();
    }

    private BulbCatalog Catalog => harness.Catalog;

    public void Dispose() => harness.Dispose();

    [Fact]
    public void ImportFile_CopiesANewBulbIntoMyBulbsWithAUniqueName()
    {
        string source = new TestBul { Gifs = [TestBulbs.RedGif], Name = "Star" }.WriteTo(harness.Scratch("Star.bul"));

        BulbImportResult added = Catalog.ImportFile(source);

        Assert.Equal(new BulbImportResult(source, BulbImportOutcome.Added, "user:Star", null), added);
        Assert.True(File.Exists(Path.Combine(harness.Paths.MyBulbsFolder, "Star.bul")));
        Assert.True(Catalog.TryGetInfo("user:Star", out BulbInfo? info));
        Assert.Equal(BulbOrigin.UserAddOn, info.Origin);
        Assert.Equal(CatalogHarness.Now, info.AddedDate);
        Assert.Contains(Catalog.All, b => b.Id == "user:Star");
        harness.WaitFor(BulbCatalogChange.Added, "user:Star");

        Assert.Equal(new BulbImportResult(source, BulbImportOutcome.AlreadyPresent, "user:Star", null), Catalog.ImportFile(source));

        string other = new TestBul { Gifs = [TestBulbs.BlueGif], Name = "Another Star" }.WriteTo(harness.Scratch("Star.bul"));
        Assert.Equal("user:Star (2)", Catalog.ImportFile(other).BulbId);
        Assert.True(File.Exists(Path.Combine(harness.Paths.MyBulbsFolder, "Star (2).bul")));
        Assert.Empty(Directory.EnumerateFiles(harness.Paths.MyBulbsFolder, "*.tmp"));
    }

    [Fact]
    public void ImportFile_RecognizesBundledBulbsEvenWithA54RegionCacheAppended()
    {
        byte[] arrow = File.ReadAllBytes(Path.Combine(BulbGoldens.BundledBulbsFolder, "Arrow.bul"));
        byte[] cached = [.. arrow, 0x6E, 0x67, 0x65, 0x52, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32LittleEndian(cached.AsSpan(0x578), cached.Length);
        string copy = harness.Scratch("Arrow.bul");
        File.WriteAllBytes(copy, cached);
        string snow2 = harness.Scratch("Snow2.bul");
        File.Copy(Path.Combine(BulbGoldens.BundledBulbsFolder, "Snow2.bul"), snow2);

        Assert.Equal(BulbImportOutcome.AlreadyPresent, Catalog.ImportFile(copy).Outcome);
        Assert.Equal("addon:Arrow", Catalog.ImportFile(copy).BulbId);
        Assert.Equal("addon:Snow2", Catalog.ImportFile(snow2).BulbId);
        Assert.Equal("addon:Arrow", Catalog.FindByContent(copy));
        Assert.False(Directory.Exists(harness.Paths.MyBulbsFolder) && Directory.EnumerateFiles(harness.Paths.MyBulbsFolder).Any());
    }

    [Fact]
    public void ImportFile_ReportsDamagedUnsupportedAndUnreadableFiles()
    {
        string damaged = new TestBul { Gifs = [TestBulbs.RedGif], PreviewEntry = -1 }.WriteTo(harness.Scratch("Bad.bul"));
        string text = harness.Scratch("notes.txt");
        File.WriteAllText(text, "hello");

        Assert.Equal(new BulbImportResult(damaged, BulbImportOutcome.Damaged, null, null), Catalog.ImportFile(damaged));
        Assert.Equal(new BulbImportResult(text, BulbImportOutcome.Failed, null, BulbCatalog.UnsupportedFileText), Catalog.ImportFile(text));
        BulbImportResult missing = Catalog.ImportFile(harness.Scratch("missing.bul"));
        Assert.Equal(BulbImportOutcome.Failed, missing.Outcome);
        Assert.False(string.IsNullOrEmpty(missing.Error));
        Assert.False(File.Exists(Path.Combine(harness.Paths.MyBulbsFolder, "Bad.bul")));
    }

    [Fact]
    public void ImportFile_TurnsAGifIntoABulbThroughTheBulbFactoryWriter()
    {
        string gif = harness.Scratch("Candle.GIF");
        File.WriteAllBytes(gif, TestBulbs.RedGif);
        string broken = harness.Scratch("Broken.gif");
        File.WriteAllBytes(broken, [1, 2, 3]);

        BulbImportResult made = Catalog.ImportFile(gif);
        BulbImportResult twice = Catalog.ImportFile(gif);

        Assert.Equal(new BulbImportResult(gif, BulbImportOutcome.Added, "user:Candle", null), made);
        Assert.Equal("user:Candle 1", twice.BulbId);
        Assert.Equal(("Pat Smith", 2026), (harness.GifWriter.Calls[0].Author, harness.GifWriter.Calls[0].Year));
        Assert.NotEqual(harness.GifWriter.Calls[0].BulbId, harness.GifWriter.Calls[1].BulbId);
        Assert.True(Catalog.TryGetInfo("user:Candle", out BulbInfo? info));
        Assert.True(info.IsEditable);
        Assert.Equal(new BulbImportResult(broken, BulbImportOutcome.Damaged, null, BulbCatalog.GifCannotBeImportedText), Catalog.ImportFile(broken));
    }

    [Fact]
    public void LoadUserBulb_AddsUpdatesAndReportsDamagedFiles()
    {
        string path = harness.WriteMyBulb("Mine.bul", new TestBul { Gifs = [TestBulbs.RedGif], Name = "Mine" });

        BulbInfo? first = Catalog.LoadUserBulb(path);
        Assert.Equal("Mine", first?.Name);
        harness.WaitFor(BulbCatalogChange.Added, "user:Mine");
        Assert.True(Catalog.TryGetBulb("user:Mine", out IBulb? before));

        new TestBul { Gifs = [TestBulbs.BlueGif], Name = "Mind" }.WriteTo(path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(2));
        Assert.Equal("Mind", Catalog.LoadUserBulb(path)?.Name);
        harness.WaitFor(BulbCatalogChange.Updated, "user:Mine");
        Assert.True(Catalog.TryGetBulb("user:Mine", out IBulb? after));
        Assert.NotEqual(before.ContentKey, after.ContentKey);
        Assert.Equal("Mind", after.Name);

        new TestBul { Gifs = [TestBulbs.BlueGif], Corners = [0, -1, 0, 0] }.WriteTo(path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(4));
        Assert.Null(Catalog.LoadUserBulb(path));
        harness.WaitFor(BulbCatalogChange.Removed, "user:Mine");
        Assert.Equal(path, Assert.Single(Catalog.DamagedFiles).FilePath);
        Assert.False(Catalog.TryGetBulb("user:Mine", out _));

        Assert.Throws<ArgumentException>(() => Catalog.LoadUserBulb(harness.Scratch("Elsewhere.bul")));
    }

    [Fact]
    public void RemoveUserBulb_HoldsTheFileAndRestoreBringsItBack()
    {
        string source = new TestBul { Gifs = [TestBulbs.GreenGif], Name = "Keep Me" }.WriteTo(harness.Scratch("KeepMe.bul"));
        string id = Catalog.ImportFile(source).BulbId!;
        string path = Path.Combine(harness.Paths.MyBulbsFolder, "KeepMe.bul");

        HeldItem held = Catalog.RemoveUserBulb(id);

        Assert.Equal(path, held.OriginalPath);
        Assert.False(File.Exists(path));
        Assert.Single(harness.Holding.Items);
        Assert.False(Catalog.TryGetInfo(id, out _));
        Assert.False(Catalog.TryGetBulb(id, out _));
        harness.WaitFor(BulbCatalogChange.Removed, id);

        Catalog.RestoreUserBulb(held);

        Assert.True(File.Exists(path));
        Assert.True(Catalog.TryGetInfo(id, out _));
        harness.WaitFor(BulbCatalogChange.Added, id);
        Assert.Throws<ArgumentException>(() => Catalog.RemoveUserBulb("addon:Arrow"));
        Assert.Throws<ArgumentException>(() => Catalog.RemoveUserBulb("builtin:sun"));
    }

    [Fact]
    public void Watcher_PicksUpFilesCopiedIntoMyBulbsByHand()
    {
        Assert.False(Directory.Exists(harness.Paths.MyBulbsFolder));

        string path = harness.WriteMyBulb("ByHand.bul", new TestBul { Gifs = [TestBulbs.RedGif], Name = "By Hand" });
        harness.WaitFor(BulbCatalogChange.Added, "user:ByHand");
        Assert.True(Catalog.TryGetInfo("user:ByHand", out _));

        new TestBul { Gifs = [TestBulbs.RedGif], Name = "By Hand Too" }.WriteTo(path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(3));
        harness.WaitFor(BulbCatalogChange.Updated, "user:ByHand");
        Assert.True(Catalog.TryGetInfo("user:ByHand", out BulbInfo? updated));
        Assert.Equal("By Hand Too", updated.Name);

        File.Delete(path);
        harness.WaitFor(BulbCatalogChange.Removed, "user:ByHand");
        Assert.False(Catalog.TryGetInfo("user:ByHand", out _));
    }

    [Fact]
    public void FindByContent_IgnoresUnknownDamagedAndMissingFiles()
    {
        string unknown = new TestBul { Gifs = [TestBulbs.RedGif], Name = "Unknown" }.WriteTo(harness.Scratch("Unknown.bul"));
        string damaged = new TestBul { Gifs = [TestBulbs.RedGif], PreviewEntry = -1 }.WriteTo(harness.Scratch("Damaged.bul"));

        Assert.Null(Catalog.FindByContent(unknown));
        Assert.Null(Catalog.FindByContent(damaged));
        Assert.Null(Catalog.FindByContent(harness.Scratch("missing.bul")));
        Assert.Equal("addon:Gemstones", Catalog.FindByContent(Path.Combine(BulbGoldens.BundledBulbsFolder, "Gemstones.bul")));
    }

    [Fact]
    public void AllocateLegacyId_ReturnsFreshIds()
    {
        HashSet<int> used = [.. Catalog.All.Select(b => Catalog.TryGetBulb(b.Id, out IBulb? bulb) ? bulb.LegacyId : -1)];

        int first = Catalog.AllocateLegacyId();
        int second = Catalog.AllocateLegacyId();

        Assert.NotEqual(first, second);
        Assert.True(first >= 49 && second >= 49);
        Assert.DoesNotContain(first, used);
        Assert.DoesNotContain(second, used);
    }
}
