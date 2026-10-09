using HolidayLights.App.FirstRun;
using HolidayLights.App.Shell;
using HolidayLights.Core.Legacy;
using HolidayLights.Core.Seasons;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.App.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.App;

/// <summary>
/// The first run (PRODUCT-SPEC 2.5, 6.8.4; acceptance 7.5 #1, #4): the newcomer settings, the 5.4 import on top of them,
/// the Welcome card's theme choices and 5.4 lines.
/// </summary>
public sealed class FirstRunTests : IDisposable
{
    private static readonly DateTimeOffset October8 = new(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(-7));

    private readonly TempDataRoot root = new();
    private readonly InMemorySettingsStore settings = new();
    private readonly FakeServices services;

    public FirstRunTests()
    {
        var themes = new ThemeLibrary(root.Paths, new TestHoldingFolder(root.Paths), new RecordingLog());
        themes.Load();
        services = new FakeServices
        {
            Paths = root.Paths,
            Settings = settings,
            Calendar = new SeasonCalendar(),
            Themes = themes,
            ThemeService = new ThemeService(themes, new BundledSongs(), new BuiltInResolver()),
            SystemInfo = new FakeSystemInfo { RegionCode = "US", AnimationsEnabled = true },
        };
    }

    public void Dispose() => root.Dispose();

    private FirstRunOutcome Run(ILegacyImporter importer)
    {
        services.LegacyImporter = importer;
        return new FirstRunSetup(services, new ManualTime(October8)).RunAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void ANewcomerGetsTodaysHolidayWithoutMusic()
    {
        FirstRunOutcome outcome = Run(new FakeImporter(preview: null));

        Assert.Equal(WelcomeVariant.Newcomer, outcome.Variant);
        Assert.False(outcome.HoldFirstSong);
        AppSettings now = settings.Current;
        Assert.True(now.Calendar.Enabled);
        Assert.Equal(CalendarEntryIds.Halloween, now.Calendar.ActiveEntryId);
        Assert.True(services.ThemeService.Matches(services.Themes.Find("Halloween")!, now));
        Assert.False(now.Music.Enabled);
        Assert.True(now.Startup.Auto);
        Assert.Equal(BulbDrawing.Desktop, now.Lights.Drawing);
        Assert.True(now.Lights.BehindIcons);
        Assert.False(now.Accessibility.LimitFlashing);
    }

    [Fact]
    public void ReducedMotionAtFirstRunLimitsFlashing()
    {
        ((FakeSystemInfo)services.SystemInfo).AnimationsEnabled = false;
        Run(new FakeImporter(preview: null));
        Assert.True(settings.Current.Accessibility.LimitFlashing);
    }

    [Theory]
    [InlineData(true, WelcomeVariant.Imported54FactoryDefaults)]
    [InlineData(false, WelcomeVariant.Imported54Customized)]
    public void A54ImportStartsFromTheNewcomerSettingsAndHoldsTheFirstSong(bool factoryDefaults, WelcomeVariant variant)
    {
        var importer = new FakeImporter(new LegacyImportPreview { IsFactoryDefault = factoryDefaults, LegacyValues = new ThemeableSettings() });
        FirstRunOutcome outcome = Run(importer);

        Assert.Equal(variant, outcome.Variant);
        Assert.True(outcome.HoldFirstSong);
        Assert.Equal(LegacyImportMode.FirstRun, importer.Mode);
        Assert.True(importer.Current!.Calendar.Enabled, "The import starts from the newcomer settings.");
        Assert.Equal(SettingsChangeKind.Import, settings.History[^1].Kind);
        Assert.Equal("imported", settings.Current.Themes.LastName);
    }

    [Fact]
    public void AFailedImportStillLightsTheDesktop()
    {
        FirstRunOutcome outcome = Run(new FakeImporter(new LegacyImportPreview { IsFactoryDefault = true, LegacyValues = new ThemeableSettings() }, fail: true));
        Assert.Equal(WelcomeVariant.Newcomer, outcome.Variant);
        Assert.True(settings.Current.Calendar.Enabled);
    }

    [Fact]
    public void OnOctober8TheCardsAreAutomaticChristmasOneThanksgivingAndNewYear()
    {
        WelcomeThemeChoices choices = WelcomeThemeChoices.For(new CalendarSettings(), new SeasonCalendar(), new DateOnly(2026, 10, 8));
        Assert.Equal("Halloween", choices.TodayTheme);
        Assert.Equal(["Christmas 1", "Thanksgiving", "New Year"], choices.Themes);
    }

    [Fact]
    public void InDecemberChristmasOneIsNotRepeated()
    {
        WelcomeThemeChoices choices = WelcomeThemeChoices.For(new CalendarSettings(), new SeasonCalendar(), new DateOnly(2026, 12, 10));
        Assert.Equal("Christmas 1", choices.TodayTheme);
        Assert.Equal(["Christmas 1", "New Year", "Valentine's Day"], choices.Themes);
    }

    [Fact]
    public void BetweenHolidaysTheNextHolidaysAreOffered()
    {
        WelcomeThemeChoices choices = WelcomeThemeChoices.For(new CalendarSettings(), new SeasonCalendar(), new DateOnly(2026, 7, 15));
        Assert.Equal("Classic Lights", choices.TodayTheme);
        Assert.Equal(["Christmas 1", "Halloween", "Thanksgiving"], choices.Themes);
    }

    [Fact]
    public void TheFactoryDefaultLineCountsThe54Themes()
    {
        var record = new Import54Record
        {
            Report =
            [
                .. Enumerable.Range(1, 11).Select(i => new ImportReportItem { Item = $@"Themes\Theme {i}", Status = ImportItemStatus.AlreadyIncluded }),
                new ImportReportItem { Item = "Flash Interval", Status = ImportItemStatus.Imported },
                new ImportReportItem { Item = @"Themes\Broken", Status = ImportItemStatus.NotImported, Reason = "damaged" },
            ],
        };
        Assert.Equal(" Its 11 themes are included.", WelcomeSituation.ThemesSentence(record));
        Assert.Equal(" Its theme is included.", WelcomeSituation.ThemesSentence(new Import54Record { Report = [new ImportReportItem { Item = @"Themes\A" }] }));
        Assert.Equal("", WelcomeSituation.ThemesSentence(null));
    }

    /// <summary>A 5.4 importer that reports a fixed preview and marks its result.</summary>
    private sealed class FakeImporter(LegacyImportPreview? preview, bool fail = false) : ILegacyImporter
    {
        public LegacyImportMode? Mode { get; private set; }

        public AppSettings? Current { get; private set; }

        public bool IsLegacyInstallPresent() => preview is not null;

        public LegacyImportPreview? Analyze() => preview;

        public LegacyImportResult Import(LegacyImportPreview preview, AppSettings current, LegacyImportMode mode)
        {
            if (fail)
            {
                throw new IOException("The 5.4 folder could not be read.");
            }

            Mode = mode;
            Current = current;
            var record = new Import54Record { FactoryDefaults = preview.IsFactoryDefault };
            return new LegacyImportResult(current with { Import54 = record, Themes = new ThemePreferences { LastName = "imported" } }, record);
        }
    }
}
