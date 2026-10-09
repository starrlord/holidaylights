using HolidayLights.Core.Legacy;
using HolidayLights.Core.Seasons;
using HolidayLights.Core.Settings;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.Settings.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings;

/// <summary>
/// The commissioning PC (golden <c>legacy-registry.json</c>): 5.4 at its factory defaults with its 11 installer themes,
/// its 46 bundled songs and 11 bundled pictures, no add-on bulbs (PRODUCT-SPEC 2.5.2, 7.5 #1 and #2).
/// </summary>
public sealed class LegacyGoldenImportTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly TempDataRoot root = new();
    private readonly string programFolder;
    private readonly ThemeLibrary library;
    private readonly FakeSongLibrary songs;
    private readonly FakePictureLibrary pictures;
    private readonly ThemeService service;

    public LegacyGoldenImportTests()
    {
        programFolder = Path.Combine(root.Root, "Utils", "HolidayLights");
        CopyFolder(Path.Combine(TestPaths.ContentFolder, "Music"), Path.Combine(programFolder, "Holiday Lights Music"));
        CopyFolder(Path.Combine(TestPaths.ContentFolder, "Pictures"), Path.Combine(programFolder, "Holiday Lights Pictures"));
        library = new ThemeLibrary(root.Paths, new TestHoldingFolder(root.Paths), new RecordingLog());
        library.Load();
        songs = FakeSongLibrary.Bundled(root.Paths.MyMusicFolder);
        pictures = FakePictureLibrary.Bundled(root.Paths.MyPicturesFolder);
        service = new ThemeService(library, songs, new FakeBulbCatalog());
    }

    public void Dispose() => root.Dispose();

    [Fact]
    public void Analyze_TheCommissioningPc_IsAtFactoryDefaults()
    {
        LegacyImportPreview preview = Importer(GoldenRegistry.Load(programFolder)).Analyze()!;

        Assert.True(preview.IsFactoryDefault);
        Assert.Equal(programFolder, preview.LegacyFolder);
        Assert.Equal(11, preview.ThemeCount);
        Assert.Equal((0, 0, 0), (preview.BulbFileCount, preview.SongFileCount, preview.PictureFileCount));
    }

    [Fact]
    public void Analyze_The54CurrentSettings_AreTheChristmas1LookWithThe54ScreenSaverDefaults()
    {
        ThemeableSettings values = Importer(GoldenRegistry.Load(programFolder)).Analyze()!.LegacyValues;

        Assert.Equal(SlotAssignment.Classic54Default, values.Arrangement);
        Assert.Equal(new FlashSettings { Pattern = FlashPatternId.FlashTogether, Interval = 5 }, values.Flash);
        Assert.Empty(values.Music.DisabledSongs);
        Assert.Equal(PlayMode.Always, values.Music.Mode);
        Assert.Equal(new SaverLook(), values.Saver);
    }

    [Fact]
    public void Import_FirstRun_KeepsTheNewcomerLook_AndThe54SettingsGoToRecentSettings()
    {
        AppSettings newcomer = Newcomer();
        LegacyImporter importer = Importer(GoldenRegistry.Load(programFolder));
        LegacyImportPreview preview = importer.Analyze()!;

        LegacyImportResult result = importer.Import(preview, newcomer, LegacyImportMode.FirstRun);
        AppSettings s = result.Settings;

        Assert.Same(newcomer.Current, s.Current);
        Assert.True(s.Calendar.Enabled);
        Assert.False(s.Music.Enabled);
        Assert.True(s.Startup.Auto);
        Assert.True(service.Matches(library.Find("Halloween")!, s));
        Assert.Equal(LegacyImporter.RecentSettingsLabel, s.RecentSettings[0].Label);
        Assert.True(ThemeValues.Equal(preview.LegacyValues, s.RecentSettings[0].Values, KnownSongs.From(songs)));
        Assert.Equal(new HotKeyBinding { Enabled = true, Key = "B" }, s.HotKeys.Location);
        Assert.All(s.Colors.Custom, c => Assert.Equal(RgbColor.Black, c));
        Assert.Same(result.Record, s.Import54);
        Assert.Equal(new DateOnly(2026, 10, 8), result.Record.Date);
        Assert.True(result.Record.FactoryDefaults);
        Assert.Equal((11, 0, 0, 0), (result.Record.Themes, result.Record.Bulbs, result.Record.Songs, result.Record.Pictures));
        Assert.Empty(library.GetMissingOrChangedShipped());
        Assert.Empty(songs.AddedSources);
        Assert.Empty(pictures.AddedSources);
    }

    [Fact]
    public void UseMy2003Lights_RestoresTheExact54State()
    {
        LegacyImporter importer = Importer(GoldenRegistry.Load(programFolder));
        AppSettings imported = importer.Import(importer.Analyze()!, Newcomer(), LegacyImportMode.FirstRun).Settings;

        AppSettings classic = service.RestoreRecent(imported, imported.RecentSettings[0], Now);

        Assert.Equal(SlotAssignment.Classic54Default, classic.Current.Arrangement);
        Assert.Equal(5, classic.Current.Flash.Interval);
        Assert.Equal(("Snow", "Happy Holidays!", SaverPictures.Default), (classic.Current.Saver.Animation, classic.Current.Saver.Message, classic.Current.Saver.Picture));
        Assert.Null(service.FindMatching(classic));
    }

    [Fact]
    public void Import_FirstRun_ReportsEveryItem()
    {
        LegacyImporter importer = Importer(GoldenRegistry.Load(programFolder));

        IReadOnlyList<ImportReportItem> report = importer.Import(importer.Analyze()!, Newcomer(), LegacyImportMode.FirstRun).Record.Report;

        Assert.Equal(ImportItemStatus.NotImported, Item(report, "Path").Status);
        Assert.Equal("obsolete", Item(report, "Current Version").Reason);
        Assert.Equal(ImportItemStatus.Imported, Item(report, "Bulb Settings").Status);
        Assert.Equal(ImportItemStatus.Imported, Item(report, "Bulb Location Hot Key Char").Status);
        Assert.Equal(ImportItemStatus.AlreadyIncluded, Item(report, "Enabled Music").Status);
        Assert.Equal(11, report.Count(i => i.Item.StartsWith(@"Themes\", StringComparison.Ordinal) && i.Status == ImportItemStatus.AlreadyIncluded));
        Assert.Equal(46, report.Count(i => i.Item.StartsWith(@"Holiday Lights Music\", StringComparison.Ordinal) && i.Status == ImportItemStatus.AlreadyIncluded));
        Assert.Equal(11, report.Count(i => i.Item.StartsWith(@"Holiday Lights Pictures\", StringComparison.Ordinal) && i.Status == ImportItemStatus.AlreadyIncluded));
        Assert.Equal(19 + 11 + 46 + 11, report.Count);
        Assert.Equal("Path", report[0].Item);
    }

    [Theory]
    [InlineData("Flash Interval", 4u)]
    [InlineData("Music Play", 4u)]
    [InlineData("Bulb Location", 1u)]
    [InlineData("Bulb Location Hot Key Char", 0x48u)]
    [InlineData("Custom Color  3", 0x00FF00u)]
    [InlineData("Prevent Slow Bulb Warning", 1u)]
    [InlineData("Screen Saver Movement Type", 2u)]
    public void FactoryDefaultRule_AnyChangedValue_IsACustomizedInstall(string name, uint value)
    {
        LegacyRegistrySnapshot golden = GoldenRegistry.Load(programFolder);

        Assert.False(LegacyFactoryDefaults.IsFactoryDefault(With(golden, name, LegacyValue.OfDWord(value))));
    }

    [Fact]
    public void FactoryDefaultRule_IgnoresRegistrationVersionAndCaches_AndCountsMissingValuesAsDefaults()
    {
        LegacyRegistrySnapshot golden = GoldenRegistry.Load(programFolder);
        LegacyRegistrySnapshot changed = With(With(With(golden, "Serial Number", LegacyValue.OfString("123456789012")), "Current Version", LegacyValue.OfString("5.3")), "Last Converted Picture Name", LegacyValue.OfString("x.jpg"));
        var missing = golden with { Values = golden.Values.Where(v => v.Key != "Flash Pattern" && v.Key != "Screen Saver Message Font").ToDictionary() };

        Assert.True(LegacyFactoryDefaults.IsFactoryDefault(changed));
        Assert.True(LegacyFactoryDefaults.IsFactoryDefault(missing));
        Assert.False(LegacyFactoryDefaults.IsFactoryDefault(With(golden, "Disabled Music", LegacyRaw.SongList("Clementine.mid"))));
        Assert.False(LegacyFactoryDefaults.IsFactoryDefault(With(golden, "Screen Saver Message Font", LegacyRaw.LogFont("Arial", -48, 400))));
        Assert.False(LegacyFactoryDefaults.IsFactoryDefault(With(golden, "Bulb Settings", LegacyRaw.BulbSettings(8))));
    }

    [Fact]
    public void FactoryDefaultRule_AnyUserThemeOrCategoryOverride_IsACustomizedInstall()
    {
        LegacyRegistrySnapshot golden = GoldenRegistry.Load(programFolder);
        KeyValuePair<string, IReadOnlyDictionary<string, LegacyValue>> christmas = golden.Themes.Single(t => t.Key == "Christmas 1");
        var changedTheme = new Dictionary<string, LegacyValue>(christmas.Value) { ["Flash Interval"] = LegacyValue.OfDWord(9) };

        Assert.False(LegacyFactoryDefaults.IsFactoryDefault(golden with { Themes = [.. golden.Themes, new("Grandma's Lights", christmas.Value)] }));
        Assert.False(LegacyFactoryDefaults.IsFactoryDefault(golden with { Themes = [.. golden.Themes.Where(t => t.Key != "Christmas 1"), new("Christmas 1", changedTheme)] }));
        Assert.True(LegacyFactoryDefaults.IsFactoryDefault(golden with { Themes = [.. golden.Themes.Where(t => t.Key != "Easter Eggs")] }));
        Assert.False(LegacyFactoryDefaults.IsFactoryDefault(golden with { IncludedBulbCategories = new Dictionary<string, string> { ["Standard Bulbs"] = "Christmas" } }));
    }

    private static LegacyRegistrySnapshot With(LegacyRegistrySnapshot snapshot, string name, LegacyValue value) =>
        snapshot with { Values = new Dictionary<string, LegacyValue>(snapshot.Values, StringComparer.OrdinalIgnoreCase) { [name] = value } };

    private static ImportReportItem Item(IReadOnlyList<ImportReportItem> report, string item) => report.Single(i => i.Item == item);

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }
    }

    private AppSettings Newcomer() =>
        NewcomerSettings.Create(new AppSettings(), new FirstRunContext(new DateOnly(2026, 10, 8), "US", true, Now), new SeasonCalendar(), library, service);

    private LegacyImporter Importer(LegacyRegistrySnapshot snapshot) =>
        new(new FakeRegistrySource(snapshot), new FakeBulbCatalog(), songs, pictures, library, service, new FakeShellOperations(), new RecordingLog(), new FakeBulbHeaders(), new FixedClock(Now));
}
