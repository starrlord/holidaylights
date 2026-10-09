using HolidayLights.Core.Legacy;
using HolidayLights.Core.Seasons;
using HolidayLights.Core.Settings;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.Settings.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings;

/// <summary>
/// A customized 5.4 installation (PRODUCT-SPEC 7.5 #3): its own arrangement with add-on bulbs (one equal to a bundled bulb,
/// one that is not bundled, one damaged file), Bulb Chase at interval 3, Intermittently, the hot key letter H turned off,
/// On Top, user themes, a song and a picture that are not bundled, category overrides and a Startup shortcut.
/// </summary>
public sealed class LegacyImporterTests : IDisposable
{
    private const int ArrowId = 12546545;
    private const int StarId = 12546546;
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly TempDataRoot root = new();
    private readonly string programFolder;
    private readonly ThemeLibrary library;
    private readonly FakeSongLibrary songs;
    private readonly FakePictureLibrary pictures;
    private readonly FakeBulbCatalog bulbs = new();
    private readonly FakeShellOperations shell = new();
    private readonly FakeBulbHeaders headers = new();
    private readonly ThemeService service;
    private readonly RecordingLog log = new();

    public LegacyImporterTests()
    {
        programFolder = Path.Combine(root.Root, "Old", "HolidayLights");
        library = new ThemeLibrary(root.Paths, new TestHoldingFolder(root.Paths), log);
        library.Load();
        songs = FakeSongLibrary.Bundled(root.Paths.MyMusicFolder);
        pictures = FakePictureLibrary.Bundled(root.Paths.MyPicturesFolder);
        service = new ThemeService(library, songs, bulbs);
        CreateLegacyFiles();
    }

    public void Dispose() => root.Dispose();

    [Fact]
    public void Analyze_ACustomizedInstall()
    {
        LegacyImportPreview preview = Importer(Snapshot()).Analyze()!;

        Assert.False(preview.IsFactoryDefault);
        Assert.Equal(programFolder, preview.LegacyFolder);
        Assert.Equal(4, preview.ThemeCount);
        Assert.Equal((3, 1, 2), (preview.BulbFileCount, preview.SongFileCount, preview.PictureFileCount));
        Assert.Equal(["builtin:jack-o-lanterns", "addon:Arrow", "user:Star"], preview.LegacyValues.Arrangement.Top);
        Assert.Empty(bulbs.Imported);
        Assert.Empty(songs.AddedSources);
    }

    [Fact]
    public void Import_FirstRun_AppliesThe54SettingsAsTheyAre()
    {
        AppSettings newcomer = Newcomer();
        LegacyImporter importer = Importer(Snapshot());

        AppSettings s = importer.Import(importer.Analyze()!, newcomer, LegacyImportMode.FirstRun).Settings;

        SlotAssignment arrangement = s.Current.Arrangement;
        Assert.Equal(["builtin:jack-o-lanterns", "addon:Arrow", "user:Star"], arrangement.Top);
        Assert.Equal(["builtin:jack-o-lanterns"], arrangement.Right);
        Assert.Equal(("builtin:autumn-leaves", "builtin:autumn-leaves", (string?)null), (arrangement.TopLeft, arrangement.TopRight, arrangement.BottomLeft));
        Assert.Equal(new FlashSettings { Pattern = FlashPatternId.BulbChase, Interval = 3 }, s.Current.Flash);
        Assert.Equal(PlayMode.Intermittently, s.Current.Music.Mode);
        Assert.Equal(["user:My Song.mid", "bundled:Jingle Bells.mid"], s.Current.Music.DisabledSongs);
        Assert.Equal(SaverAnimations.ForBulb("user:Star"), s.Current.Saver.Animation);
        Assert.Equal(SaverMovementStyle.BounceOffSides, s.Current.Saver.Style);
        Assert.Equal("Boo!", s.Current.Saver.Message);
        Assert.Equal(new SaverFont { Family = "Georgia", SizePt = 48, Bold = false, Italic = true }, s.Current.Saver.Font);
        Assert.Equal(new RgbColor(0, 0, 255), s.Current.Saver.Color);
        Assert.Equal("user:Family.bmp", s.Current.Saver.Picture);
        Assert.Equal(PicturePlacement.Tile, s.Current.Saver.Placement);

        Assert.Equal(BulbDrawing.OnTop, s.Lights.Drawing);
        Assert.True(s.Music.Enabled);
        Assert.False(s.Calendar.Enabled);
        Assert.True(s.Startup.Auto);
        Assert.Equal(new HotKeyBinding { Enabled = false, Key = "H" }, s.HotKeys.Location);
        Assert.Equal(new RgbColor(0x40, 0x80, 0xFF), s.Colors.Custom[3]);
        Assert.Equal(["Christmas", "Favorites"], s.Bulbs.CategoryOverrides["builtin:standard-bulbs"]);
        Assert.Equal(LegacyImporter.RecentSettingsLabel, s.RecentSettings[0].Label);
        Assert.Same(s.Current.Saver, s.RecentSettings[0].Values.Saver);
        Assert.Null(service.FindMatching(s));
    }

    [Fact]
    public void Import_FirstRun_ImportsThemesUnderTheirOwnNames()
    {
        LegacyImporter importer = Importer(Snapshot());

        Import54Record record = importer.Import(importer.Analyze()!, Newcomer(), LegacyImportMode.FirstRun).Record;

        ThemeDefinition grandma = library.Find("Grandma's Lights")!;
        Assert.Null(grandma.Shipped);
        Assert.Equal(FlashPatternId.Alternating, grandma.Flash!.Pattern);
        Assert.Null(grandma.Flash.Interval);
        Assert.Equal(["user:My Song.mid", "bundled:Clementine.mid"], grandma.Music!.EnabledSongs);
        Assert.NotNull(library.Find("Mom-Dad"));
        Assert.Equal(9, library.Find("Christmas 1")!.Flash!.Interval);
        Assert.True(library.IsChangedFromOriginal(library.Find("Christmas 1")!));
        Assert.False(library.IsChangedFromOriginal(library.Find("Halloween")!));
        Assert.Equal(4, record.Themes);
        Assert.Equal(ImportItemStatus.AlreadyIncluded, Item(record, @"Themes\Halloween").Status);
        Assert.Equal(ImportItemStatus.Imported, Item(record, @"Themes\Mom/Dad").Status);
    }

    [Fact]
    public void Import_FirstRun_RecreatesAddOnIdsAndImportsFiles()
    {
        LegacyImporter importer = Importer(Snapshot());

        Import54Record record = importer.Import(importer.Analyze()!, Newcomer(), LegacyImportMode.FirstRun).Record;

        Assert.Equal([Path.Combine(programFolder, "Holiday Lights Bulbs", "Star.bul")], bulbs.Imported);
        Assert.Equal(ImportItemStatus.AlreadyIncluded, Item(record, @"Holiday Lights Bulbs\Arrow Copy.bul").Status);
        Assert.Equal(ImportItemStatus.Imported, Item(record, @"Holiday Lights Bulbs\Star.bul").Status);
        Assert.Equal("the file is damaged", Item(record, @"Holiday Lights Bulbs\Broken.bul").Reason);
        Assert.Equal(ImportItemStatus.AlreadyIncluded, Item(record, @"Holiday Lights Music\Jingle Bells.mid").Status);
        Assert.Equal(ImportItemStatus.Imported, Item(record, @"Holiday Lights Music\My Song.mid").Status);
        Assert.DoesNotContain(record.Report, i => i.Item.EndsWith("reset.mid", StringComparison.Ordinal) || i.Item.EndsWith("notes.txt", StringComparison.Ordinal));
        Assert.Equal(ImportItemStatus.Imported, Item(record, @"Holiday Lights Pictures\Family.lnk").Status);
        Assert.Equal(ImportItemStatus.NotImported, Item(record, @"Holiday Lights Pictures\Gone.lnk").Status);
        Assert.Equal(ImportItemStatus.AlreadyIncluded, Item(record, @"Holiday Lights Pictures\Santa Candle.BMP").Status);
        Assert.True(File.Exists(Path.Combine(root.Paths.MyMusicFolder, "My Song.mid")));
        Assert.True(File.Exists(Path.Combine(root.Paths.MyPicturesFolder, "Family.bmp")));
        Assert.Equal((2, 1, 1), (record.Bulbs, record.Songs, record.Pictures));
        Assert.False(record.FactoryDefaults);
    }

    [Fact]
    public void Import_ReportsEveryItem()
    {
        LegacyImporter importer = Importer(Snapshot());

        Import54Record record = importer.Import(importer.Analyze()!, Newcomer(), LegacyImportMode.FirstRun).Record;

        Assert.Equal("unknown setting", Item(record, "Mystery Value").Reason);
        Assert.Equal("obsolete", Item(record, "Serial Number").Reason);
        Assert.Equal("the bulb isn't installed", Item(record, "Bulb Settings (bulb id 999999)").Reason);
        Assert.Equal(ImportItemStatus.Imported, Item(record, @"Included Bulb Categories\Standard Bulbs").Status);
        Assert.Equal(ImportItemStatus.NotImported, Item(record, @"Included Bulb Categories\Nonexistent Bulb").Status);
        Assert.Equal(ImportItemStatus.Imported, Item(record, @"Startup\Holiday Lights.lnk").Status);
        Assert.Equal(ImportItemStatus.Imported, Item(record, @"Themes\Grandma's Lights").Status);
        Assert.All(record.Report, i => Assert.True(i.Status != ImportItemStatus.NotImported || i.Reason is not null, i.Item));
    }

    [Fact]
    public void ImportAgain_ReplacesTheCurrentSettings_KeepsExistingThemes_AddsMissingOnes()
    {
        LegacyImporter importer = Importer(Snapshot());
        AppSettings first = importer.Import(importer.Analyze()!, Newcomer(), LegacyImportMode.FirstRun).Settings;
        library.Save(library.Find("Grandma's Lights")! with { Flash = new ThemeFlash { Pattern = FlashPatternId.Twinkle } });
        library.Delete("Mom-Dad");
        AppSettings edited = first with { Current = first.Current with { Flash = new FlashSettings() }, Calendar = first.Calendar with { Enabled = true } };

        LegacyImportResult again = importer.Import(importer.Analyze()!, edited, LegacyImportMode.Again);

        Assert.Equal(new FlashSettings { Pattern = FlashPatternId.BulbChase, Interval = 3 }, again.Settings.Current.Flash);
        Assert.False(again.Settings.Calendar.Enabled);
        Assert.Equal(LegacyImporter.BeforeImportLabel, again.Settings.RecentSettings[0].Label);
        Assert.Equal(new FlashSettings(), again.Settings.RecentSettings[0].Values.Flash);
        Assert.Equal(FlashPatternId.Twinkle, library.Find("Grandma's Lights")!.Flash!.Pattern);
        Assert.NotNull(library.Find("Mom-Dad"));
        Assert.Equal("you already have a theme with this name", Item(again.Record, @"Themes\Grandma's Lights").Reason);
        Assert.Equal(ImportItemStatus.Imported, Item(again.Record, @"Themes\Mom/Dad").Status);
        Assert.Equal(ImportItemStatus.AlreadyIncluded, Item(again.Record, @"Themes\Christmas 1").Status);
        Assert.Equal(ImportItemStatus.AlreadyIncluded, Item(again.Record, @"Holiday Lights Bulbs\Star.bul").Status);
    }

    [Fact]
    public void Import_WithoutAPreviewOfThisImporter_ReadsTheRegistryAgain()
    {
        var registry = new FakeRegistrySource(Snapshot());
        LegacyImporter importer = Importer(registry);
        LegacyImportPreview foreign = Importer(Snapshot()).Analyze()!;

        importer.Import(foreign, Newcomer(), LegacyImportMode.FirstRun);

        Assert.Equal(1, registry.Reads);
        Assert.Throws<InvalidOperationException>(() => Importer(new FakeRegistrySource(null)).Import(foreign, Newcomer(), LegacyImportMode.Again));
    }

    [Fact]
    public void Absent54_IsNotPresent_AndHasNoPreview()
    {
        LegacyImporter importer = Importer(new FakeRegistrySource(null));

        Assert.False(importer.IsLegacyInstallPresent());
        Assert.Null(importer.Analyze());
        Assert.True(Importer(Snapshot()).IsLegacyInstallPresent());
    }

    [Fact]
    public void AddOnIds_AreRecreatedInNameOrder_SkippingBuiltInsAndTakenIds()
    {
        string folder = Path.Combine(root.Root, "Ids");
        string bulbsFolder = Path.Combine(folder, "Holiday Lights Bulbs");
        Directory.CreateDirectory(bulbsFolder);
        var reader = new FakeBulbHeaders();
        foreach ((string file, int id) in new[] { ("b.bul", 7), ("A.bul", 100), ("c.bul", -1), ("_x.bul", 100), ("D.BUL", 48) })
        {
            File.WriteAllText(Path.Combine(bulbsFolder, file), "x");
            reader.Headers[file] = new LegacyBulbHeader(id, file);
        }

        LegacyAddOnFiles files = LegacyAddOnFiles.Scan(folder, reader);

        Assert.Equal(["A.bul", "b.bul", "c.bul", "D.BUL", "_x.bul"], files.Loaded.Select(f => Path.GetFileName(f.FilePath)));
        Assert.Equal([100, 49, 50, 51, 101], files.Loaded.Select(f => f.RuntimeId));
    }

    [Fact]
    public void BulbSettings_DecodeLikeTheOriginalLoader()
    {
        Assert.Equal(LegacyBulbSettings.Default, LegacyBulbSettings.Decode(null));
        Assert.Equal(LegacyBulbSettings.Default, LegacyBulbSettings.Decode([]));
        Assert.Equal(LegacyBulbSettings.Default, LegacyBulbSettings.Decode(new byte[129]));

        int[] legacy = LegacyBulbSettings.Decode([8, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1, 1]);
        Assert.Equal([8, -1, -1, -1, -1, -1, 1, 1], legacy[..8]);
        Assert.All(legacy[8..], id => Assert.Equal(-1, id));

        byte[] partial = new byte[64];
        Array.Fill(partial, (byte)0xFF);
        (partial[0], partial[1], partial[2], partial[3]) = (5, 0, 0, 0);
        int[] mixed = LegacyBulbSettings.Decode(partial);
        Assert.Equal(5, mixed[0]);
        Assert.Equal(-1, mixed[8]);
        Assert.Equal(LegacyBulbSettings.Default.Skip(16), mixed.Skip(16));
    }

    [Theory]
    [InlineData(-48, 700, 36, true)]
    [InlineData(-64, 400, 48, false)]
    [InlineData(-56, 600, 42, true)]
    [InlineData(-80, 599, 60, false)]
    [InlineData(-10, 700, 18, true)]
    [InlineData(-200, 700, 100, true)]
    [InlineData(48, 700, 36, true)]
    public void Fonts_AreConvertedFromLogFont(int height, int weight, int points, bool bold)
    {
        SaverFont font = LegacyFont.Decode(LegacyRaw.LogFont("Comic Sans MS", height, weight, underline: true).Bytes);

        Assert.Equal(new SaverFont { Family = "Comic Sans MS", SizePt = points, Bold = bold, Underline = true }, font);
    }

    [Fact]
    public void UnknownAnimations_AreSnow_AndReported()
    {
        LegacyRegistrySnapshot snapshot = Snapshot();
        snapshot = snapshot with { Values = new Dictionary<string, LegacyValue>(snapshot.Values) { ["Screen Saver Module"] = LegacyValue.OfString("Fireworks") } };
        LegacyImporter importer = Importer(snapshot);

        LegacyImportResult result = importer.Import(importer.Analyze()!, Newcomer(), LegacyImportMode.FirstRun);

        Assert.Equal(SaverAnimations.Snow, result.Settings.Current.Saver.Animation);
        Assert.Equal(ImportItemStatus.NotImported, Item(result.Record, "Screen Saver Module").Status);
    }

    private static ImportReportItem Item(Import54Record record, string item) => record.Report.Single(i => i.Item == item);

    private LegacyRegistrySnapshot Snapshot()
    {
        int[] ids = LegacyRaw.Ids(top: [23, ArrowId, StarId], right: [23, 999999], bottom: [23, 24], left: [23, 25], topLeft: 27, topRight: 27);
        var values = new Dictionary<string, LegacyValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Path"] = LegacyValue.OfString(Path.Combine(programFolder, "HOLIDA~1.EXE")),
            ["Current Version"] = LegacyValue.OfString("5.4"),
            ["Serial Number"] = LegacyValue.OfString("123456789012"),
            ["Bulb Settings"] = LegacyRaw.BulbSettings(ids),
            ["Flash Pattern"] = LegacyValue.OfDWord(3),
            ["Flash Interval"] = LegacyValue.OfDWord(3),
            ["Bulb Location"] = LegacyValue.OfDWord(1),
            ["Disabled Music"] = LegacyRaw.SongList("My Song.mid", "Jingle Bells.mid", "Nonexistent.mid"),
            ["Enabled Music"] = LegacyRaw.SongList("Clementine.mid"),
            ["Music Play"] = LegacyValue.OfDWord(4),
            ["Screen Saver Module"] = LegacyValue.OfString("Star"),
            ["Screen Saver Message"] = LegacyValue.OfString("Boo!"),
            ["Screen Saver Message Font"] = LegacyRaw.LogFont("Georgia", -64, 400, italic: true),
            ["Screen Saver Message Color"] = LegacyValue.OfDWord(0x00FF0000),
            ["Screen Saver Picture Name"] = LegacyValue.OfString("Family.lnk"),
            ["Screen Saver Picture Display Type"] = LegacyValue.OfDWord(1),
            ["Bulb Location Hot Key On or Off"] = LegacyValue.OfDWord(0),
            ["Bulb Location Hot Key Char"] = LegacyValue.OfDWord('H'),
            ["Custom Color  3"] = LegacyValue.OfDWord(0x00FF8040),
            ["Mystery Value"] = LegacyValue.OfDWord(1),
        };
        IReadOnlyDictionary<string, LegacyValue> halloween = GoldenRegistry.InstallerThemes().Single(t => t.Key == "Halloween").Value;
        var christmas = new Dictionary<string, LegacyValue>(GoldenRegistry.InstallerThemes().Single(t => t.Key == "Christmas 1").Value) { ["Flash Interval"] = LegacyValue.OfDWord(9) };
        var grandma = new Dictionary<string, LegacyValue>
        {
            ["Flash Pattern"] = LegacyValue.OfDWord(2),
            ["Enabled Music"] = LegacyRaw.SongList("My Song.mid", "Clementine.mid", "Lost Song.mid"),
            ["Bulb Settings"] = LegacyRaw.BulbSettings(LegacyRaw.Ids([0, StarId], [0], [0], [0])),
        };
        return new LegacyRegistrySnapshot
        {
            Values = values,
            Themes = [new("Christmas 1", christmas), new("Grandma's Lights", grandma), new("Halloween", halloween), new("Mom/Dad", grandma)],
            IncludedBulbCategories = new Dictionary<string, string> { ["Standard Bulbs"] = "Christmas|Favorites", ["Nonexistent Bulb"] = "X" },
            StartupShortcuts = [new(@"C:\Users\Pat\Startup\Holiday Lights.lnk", Path.Combine(programFolder, "HOLIDA~1.EXE"))],
            ProgramFolder = programFolder,
        };
    }

    private void CreateLegacyFiles()
    {
        string bulbFolder = Directory.CreateDirectory(Path.Combine(programFolder, "Holiday Lights Bulbs")).FullName;
        foreach (string file in new[] { "Arrow Copy.bul", "Star.bul", "Broken.bul" })
        {
            File.WriteAllText(Path.Combine(bulbFolder, file), file);
        }

        headers.Headers["Arrow Copy.bul"] = new LegacyBulbHeader(ArrowId, "Arrow");
        headers.Headers["Star.bul"] = new LegacyBulbHeader(ArrowId, "Star");
        bulbs.ContentMatches["Arrow Copy.bul"] = "addon:Arrow";

        string musicFolder = Directory.CreateDirectory(Path.Combine(programFolder, "Holiday Lights Music")).FullName;
        File.Copy(Path.Combine(TestPaths.ContentFolder, "Music", "Jingle Bells.mid"), Path.Combine(musicFolder, "Jingle Bells.mid"));
        File.WriteAllText(Path.Combine(musicFolder, "My Song.mid"), "MThd my own song");
        File.WriteAllText(Path.Combine(musicFolder, "notes.txt"), "not a song");
        string reset = Path.Combine(musicFolder, "reset.mid");
        File.WriteAllText(reset, "MThd");
        File.SetAttributes(reset, FileAttributes.Hidden);

        string pictureFolder = Directory.CreateDirectory(Path.Combine(programFolder, "Holiday Lights Pictures")).FullName;
        File.Copy(Path.Combine(TestPaths.ContentFolder, "Pictures", "Santa Candle.BMP"), Path.Combine(pictureFolder, "Santa Candle.BMP"));
        string elsewhere = Directory.CreateDirectory(Path.Combine(root.Root, "Elsewhere")).FullName;
        File.WriteAllText(Path.Combine(elsewhere, "Family.bmp"), "BM family");
        File.WriteAllText(Path.Combine(pictureFolder, "Family.lnk"), "shortcut");
        File.WriteAllText(Path.Combine(pictureFolder, "Gone.lnk"), "shortcut");
        shell.Shortcuts[Path.Combine(pictureFolder, "Family.lnk")] = Path.Combine(elsewhere, "Family.bmp");
    }

    private AppSettings Newcomer() =>
        NewcomerSettings.Create(new AppSettings(), new FirstRunContext(new DateOnly(2026, 10, 8), "US", true, Now), new SeasonCalendar(), library, service);

    private LegacyImporter Importer(LegacyRegistrySnapshot snapshot) => Importer(new FakeRegistrySource(snapshot));

    private LegacyImporter Importer(ILegacyRegistrySource registry) =>
        new(registry, bulbs, songs, pictures, library, service, shell, log, headers, new FixedClock(Now));
}
