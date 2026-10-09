using System.Buffers.Binary;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Legacy;
using HolidayLights.Core.Seasons;
using HolidayLights.Core.Settings;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.Settings.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings;

/// <summary>
/// The 5.4 import with the real bulb catalog (PRODUCT-SPEC 6.8, 7.5 #3): a copy of a bundled bulb maps to its <c>addon:</c>
/// id by content, a bulb that is not bundled is copied into My Bulbs, a damaged file is reported. The import runs right
/// after the catalog was started, as on a first run before the first frame.
/// </summary>
public sealed class LegacyImportCatalogTests : IDisposable
{
    private const int StarId = 777_777_777;
    private const int NameOffset = 0x1C;
    private const int NameLength = 0x50;
    private const int IdOffset = 0x0C;
    private const int CategoriesOffset = 0x574;
    private const int FileSizeOffset = 0x578;

    private readonly TempDataRoot root = new();
    private readonly RecordingLog log = new();
    private readonly BulbCatalog catalog;
    private readonly string programFolder;

    public LegacyImportCatalogTests()
    {
        catalog = new BulbCatalog(root.Paths, new InMemorySettingsStore(), new TestHoldingFolder(root.Paths), log);
        programFolder = Path.Combine(root.Root, "Old", "HolidayLights");
    }

    public void Dispose()
    {
        catalog.Dispose();
        root.Dispose();
    }

    [Fact]
    public void FirstRunImport_MapsBundledCopiesByContentAndCopiesTheOthersIntoMyBulbs()
    {
        string bundledArrow = Path.Combine(TestPaths.ContentFolder, "Bulbs", "Arrow.bul");
        int arrowId = BulFile.Read(bundledArrow).LegacyId;
        string legacyBulbs = Directory.CreateDirectory(Path.Combine(programFolder, "Holiday Lights Bulbs")).FullName;
        File.Copy(bundledArrow, Path.Combine(legacyBulbs, "Arrow Copy.bul"));
        File.WriteAllBytes(Path.Combine(legacyBulbs, "Star.bul"), Renamed(File.ReadAllBytes(bundledArrow), "Star", StarId));
        File.WriteAllText(Path.Combine(legacyBulbs, "Broken.bul"), "not a bulb");
        _ = catalog.StartAsync();

        var library = new ThemeLibrary(root.Paths, new TestHoldingFolder(root.Paths), log);
        library.Load();
        FakeSongLibrary songs = FakeSongLibrary.Bundled(root.Paths.MyMusicFolder);
        FakePictureLibrary pictures = FakePictureLibrary.Bundled(root.Paths.MyPicturesFolder);
        var themes = new ThemeService(library, songs, catalog);
        var importer = new LegacyImporter(new FakeRegistrySource(Snapshot(arrowId)), catalog, songs, pictures, library, themes, new FakeShellOperations(), log);
        AppSettings newcomer = NewcomerSettings.Create(
            new AppSettings(), new FirstRunContext(new DateOnly(2026, 10, 8), "US", true, DateTimeOffset.Now), new SeasonCalendar(), library, themes);

        LegacyImportResult result = importer.Import(importer.Analyze()!, newcomer, LegacyImportMode.FirstRun);

        Assert.Equal([LegacyBuiltInBulbs.ById(0)!, BulbIds.AddOn("Arrow"), BulbIds.User("Star")], result.Settings.Current.Arrangement.Top);
        Assert.Equal(ImportItemStatus.AlreadyIncluded, Item(result.Record, @"Holiday Lights Bulbs\Arrow Copy.bul").Status);
        Assert.Equal(ImportItemStatus.Imported, Item(result.Record, @"Holiday Lights Bulbs\Star.bul").Status);
        Assert.Equal(ImportItemStatus.NotImported, Item(result.Record, @"Holiday Lights Bulbs\Broken.bul").Status);
        Assert.Equal(["Star.bul"], Directory.GetFiles(root.Paths.MyBulbsFolder).Select(Path.GetFileName));
        Assert.True(catalog.TryGetInfo(BulbIds.User("Star"), out BulbInfo? star));
        Assert.Equal("Star", star!.Name);

        // The copy has the bundled bulb's categories: nothing to keep.
        Assert.False(result.Settings.Bulbs.CategoryOverrides.ContainsKey(BulbIds.AddOn("Arrow")));
        Assert.DoesNotContain(result.Record.Report, i => i.Item.EndsWith("(categories)", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_AnAddOnWhose54CategoriesDiffer_KeepsThemAsACategoryOverrideOfTheBundledBulb()
    {
        string bundledArrow = Path.Combine(TestPaths.ContentFolder, "Bulbs", "Arrow.bul");
        int arrowId = BulFile.Read(bundledArrow).LegacyId;
        string legacyBulbs = Directory.CreateDirectory(Path.Combine(programFolder, "Holiday Lights Bulbs")).FullName;

        // What 5.4's Edit Categories leaves in an add-on file: a new categ: record, the header re-pointed to it.
        File.WriteAllBytes(Path.Combine(legacyBulbs, "Arrow Copy.bul"), WithCategories(File.ReadAllBytes(bundledArrow), "Winter|My Favorites"));
        _ = catalog.StartAsync();

        var library = new ThemeLibrary(root.Paths, new TestHoldingFolder(root.Paths), log);
        library.Load();
        FakeSongLibrary songs = FakeSongLibrary.Bundled(root.Paths.MyMusicFolder);
        FakePictureLibrary pictures = FakePictureLibrary.Bundled(root.Paths.MyPicturesFolder);
        var themes = new ThemeService(library, songs, catalog);
        var importer = new LegacyImporter(new FakeRegistrySource(Snapshot(arrowId)), catalog, songs, pictures, library, themes, new FakeShellOperations(), log);
        AppSettings newcomer = NewcomerSettings.Create(
            new AppSettings(), new FirstRunContext(new DateOnly(2026, 10, 8), "US", true, DateTimeOffset.Now), new SeasonCalendar(), library, themes);

        LegacyImportResult result = importer.Import(importer.Analyze()!, newcomer, LegacyImportMode.FirstRun);

        Assert.Equal(BulbIds.AddOn("Arrow"), result.Settings.Current.Arrangement.Top[1]);
        Assert.Equal(["Winter", "My Favorites"], result.Settings.Bulbs.CategoryOverrides[BulbIds.AddOn("Arrow")]);
        Assert.Equal(ImportItemStatus.AlreadyIncluded, Item(result.Record, @"Holiday Lights Bulbs\Arrow Copy.bul").Status);
        Assert.Equal(ImportItemStatus.Imported, Item(result.Record, @"Holiday Lights Bulbs\Arrow Copy.bul (categories)").Status);
        Assert.False(Directory.Exists(root.Paths.MyBulbsFolder) && Directory.EnumerateFiles(root.Paths.MyBulbsFolder).Any(), "Nothing is copied.");
    }

    /// <summary>A customized 5.4 registry: the top edge holds built-in 0, the Arrow copy and the Star bulb.</summary>
    private LegacyRegistrySnapshot Snapshot(int arrowId) => new()
    {
        Values = new Dictionary<string, LegacyValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Path"] = LegacyValue.OfString(Path.Combine(programFolder, "HOLIDA~1.EXE")),
            ["Current Version"] = LegacyValue.OfString("5.4"),
            ["Bulb Settings"] = LegacyRaw.BulbSettings(LegacyRaw.Ids(top: [0, arrowId, StarId], right: [0], bottom: [0], left: [0])),
        },
        ProgramFolder = programFolder,
    };

    /// <summary>The same art under another name and header id: different content, so not equal to any bundled bulb.</summary>
    private static byte[] Renamed(byte[] bul, string name, int id)
    {
        byte[] copy = (byte[])bul.Clone();
        copy.AsSpan(NameOffset, NameLength).Clear();
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(copy, NameOffset);
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(IdOffset), id);
        return copy;
    }

    /// <summary>Appends a <c>categ:</c> record and re-points the header to it, as 5.4 saved categories.</summary>
    private static byte[] WithCategories(byte[] bul, string categories)
    {
        byte[] copy = [.. bul, .. "categ:"u8, .. System.Text.Encoding.ASCII.GetBytes(categories), 0];
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(CategoriesOffset), (uint)bul.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(FileSizeOffset), (uint)copy.Length);
        return copy;
    }

    private static ImportReportItem Item(Import54Record record, string item) => record.Report.Single(i => i.Item == item);
}
