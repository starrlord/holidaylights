using HolidayLights.Core.Seasons;
using HolidayLights.Core.Settings;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.Settings.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings;

public sealed class NewcomerSettingsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly TempDataRoot root = new();
    private readonly ThemeLibrary library;
    private readonly ThemeService service;
    private readonly SeasonCalendar calendar = new();

    public NewcomerSettingsTests()
    {
        library = new ThemeLibrary(root.Paths, new TestHoldingFolder(root.Paths), new RecordingLog());
        library.Load();
        service = new ThemeService(library, FakeSongLibrary.Bundled(), new FakeBulbCatalog());
    }

    public void Dispose() => root.Dispose();

    [Fact]
    public void OnOctober8_ANewcomerGetsHalloween_AutomaticThemesOn_MusicOff_StartupOn()
    {
        AppSettings s = Create(new AppSettings(), new DateOnly(2026, 10, 8));

        Assert.True(service.Matches(library.Find("Halloween")!, s));
        Assert.True(s.Calendar.Enabled);
        Assert.Equal("halloween", s.Calendar.ActiveEntryId);
        Assert.Equal("US", s.Calendar.Region);
        Assert.Equal(CalendarEntryIds.All, s.Calendar.Entries.Select(e => e.Id));
        Assert.False(s.Music.Enabled);
        Assert.True(s.Startup.Auto);
        Assert.False(s.Accessibility.LimitFlashing);
        Assert.Equal(BulbDrawing.Desktop, s.Lights.Drawing);
        Assert.True(s.Lights.BehindIcons);
        Assert.Equal("Halloween", s.Themes.LastName);
        Assert.Empty(s.RecentSettings);
        Assert.Same(s, SettingsSanitizer.Sanitize(s));
    }

    [Fact]
    public void BetweenHolidays_ANewcomerGetsClassicLights()
    {
        AppSettings s = Create(new AppSettings(), new DateOnly(2026, 7, 15));

        Assert.True(service.Matches(library.Find("Classic Lights")!, s));
        Assert.Equal(CalendarEntryIds.Between, s.Calendar.ActiveEntryId);
    }

    [Fact]
    public void WithAnimationEffectsOff_LimitFlashingStartsOn()
    {
        AppSettings s = NewcomerSettings.Create(new AppSettings(), new FirstRunContext(new DateOnly(2026, 10, 8), "US", AnimationsEnabled: false, Now), calendar, library, service);

        Assert.True(s.Accessibility.LimitFlashing);
    }

    [Fact]
    public void TheRegionDecidesTheCalendar()
    {
        AppSettings australia = NewcomerSettings.Create(new AppSettings(), new FirstRunContext(new DateOnly(2026, 7, 4), "au", true, Now), calendar, library, service);
        AppSettings canada = NewcomerSettings.Create(new AppSettings(), new FirstRunContext(new DateOnly(2026, 10, 10), "CA", true, Now), calendar, library, service);

        Assert.Equal("AU", australia.Calendar.Region);
        Assert.False(australia.Calendar.Entries.Single(e => e.Id == "july4th").Use);
        Assert.Equal("06-01", australia.Calendar.Entries.Single(e => e.Id == "winter").Rule.From);
        Assert.Equal(CalendarEntryIds.Between, australia.Calendar.ActiveEntryId);
        Assert.Equal("thanksgiving", canada.Calendar.ActiveEntryId);
        Assert.True(service.Matches(library.Find("Thanksgiving")!, canada));
    }

    [Fact]
    public void ResetAllSettings_KeepsTheUsersDataAndHistory_AndRecordsTheReplacedValues()
    {
        var baseline = new AppSettings
        {
            Current = new ThemeableSettings { Flash = new FlashSettings { Pattern = FlashPatternId.Twinkle, Interval = 2 } },
            Music = new MusicSettings { Enabled = true, Volume = 20 },
            HotKeys = new HotKeySettings { Location = new HotKeyBinding { Enabled = false, Key = "H" } },
            Calendar = new CalendarSettings { Enabled = false },
            Colors = new ColorSettings { Custom = [.. Enumerable.Repeat(RgbColor.White, 16)] },
            Bulbs = new BulbPreferences { Favorites = ["addon:Arrow"] },
            Saver = new SaverDeviceSettings { ShowOn = SaverDisplays.MainOnly, Previous = new ScreenSaverPrevious { ScrnsaveExe = "x.scr" } },
            RecentSettings = [new RecentSettingsEntry { Label = "Older", Values = new ThemeableSettings() }],
            Import54 = new Import54Record { Themes = 11 },
            Onboarding = new OnboardingState { WelcomeShown = true },
        };

        AppSettings s = Create(baseline, new DateOnly(2026, 10, 8), "Before Reset");

        Assert.True(service.Matches(library.Find("Halloween")!, s));
        Assert.False(s.Music.Enabled);
        Assert.Equal(60, s.Music.Volume);
        Assert.True(s.HotKeys.Location.Enabled);
        Assert.Equal("B", s.HotKeys.Location.Key);
        Assert.True(s.Calendar.Enabled);
        Assert.Equal(SaverDisplays.All, s.Saver.ShowOn);
        Assert.Equal("x.scr", s.Saver.Previous!.ScrnsaveExe);
        Assert.Equal(RgbColor.White, s.Colors.Custom[0]);
        Assert.Equal(["addon:Arrow"], s.Bulbs.Favorites);
        Assert.Equal(11, s.Import54!.Themes);
        Assert.True(s.Onboarding.WelcomeShown);
        Assert.Equal(["Before Reset", "Older"], s.RecentSettings.Select(e => e.Label));
        Assert.Same(baseline.Current, s.RecentSettings[0].Values);
        Assert.Equal(Now, s.RecentSettings[0].Date);
    }

    [Fact]
    public void ADeletedTodaysTheme_FallsBackToItsShippedOriginal()
    {
        library.Delete("Halloween");

        AppSettings s = Create(new AppSettings(), new DateOnly(2026, 10, 8));

        Assert.True(service.Matches(ShippedThemes.Find("Halloween")!, s));
    }

    [Fact]
    public void ALibraryWithoutTodaysTheme_GivesThe54DefaultValues()
    {
        var baseline = new AppSettings { Current = new ThemeableSettings { Flash = new FlashSettings { Pattern = FlashPatternId.Twinkle } } };

        AppSettings s = NewcomerSettings.Create(baseline, new FirstRunContext(new DateOnly(2026, 7, 15), "US", true, Now), calendar, new NoThemes(), service);

        Assert.Equal(new ThemeableSettings().Flash, s.Current.Flash);
        Assert.Equal(SlotAssignment.Classic54Default, s.Current.Arrangement);
        Assert.Equal(ShippedThemeNames.ClassicLights, s.Calendar.Between);
    }

    private AppSettings Create(AppSettings baseline, DateOnly today, string? label = null) =>
        NewcomerSettings.Create(baseline, new FirstRunContext(today, "US", AnimationsEnabled: true, Now), calendar, library, service, label);

    /// <summary>A theme library without any theme.</summary>
    private sealed class NoThemes : IThemeLibrary
    {
        public event EventHandler? Changed;

        public IReadOnlyList<ThemeDefinition> Themes => [];

        public IReadOnlyList<ThemeFileProblem> Problems => [];

        public IReadOnlyList<ThemeDefinition> ShippedOriginals => [];

        public void Load() => Changed?.Invoke(this, EventArgs.Empty);

        public ThemeDefinition? Find(string name) => null;

        public void Save(ThemeDefinition theme) => throw new NotSupportedException();

        public HeldItem Delete(string name) => throw new NotSupportedException();

        public void Restore(HeldItem item) => throw new NotSupportedException();

        public void Rename(string oldName, string newName) => throw new NotSupportedException();

        public bool IsChangedFromOriginal(ThemeDefinition theme) => false;

        public IReadOnlyList<ThemeDefinition> GetMissingOrChangedShipped() => [];

        public void RestoreShipped(IEnumerable<string> names) => throw new NotSupportedException();
    }
}
