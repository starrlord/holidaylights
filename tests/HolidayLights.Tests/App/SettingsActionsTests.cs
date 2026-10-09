using HolidayLights.App.Shell;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.App.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.App;

/// <summary>The settings changes of the tray, the hot keys, the pipe and the Welcome card, with their Undo texts (PRODUCT-SPEC 2.2, 6.6.4, 6.1.1).</summary>
public sealed class SettingsActionsTests
{
    private static AppSettings Apply(SettingsEdit edit, AppSettings? settings = null)
    {
        var store = new InMemorySettingsStore(settings);
        edit.ApplyTo(store);
        return store.Current;
    }

    [Fact]
    public void ShowLightsIsAnUndoableEdit()
    {
        SettingsEdit off = SettingsActions.SetLightsOn(false);
        Assert.Equal(SettingsChange.Edit("Turn off the lights"), off.Change);
        Assert.False(Apply(off).Lights.On);
    }

    [Fact]
    public void AnEditThatChangesNothingIsNoChange()
    {
        var store = new InMemorySettingsStore();
        SettingsActions.SetLightsOn(true).ApplyTo(store);
        SettingsActions.SetDrawing(BulbDrawing.Desktop).ApplyTo(store);
        SettingsActions.SetMusicEnabled(false).ApplyTo(store);
        SettingsActions.SetStartup(true).ApplyTo(store);
        SettingsActions.SetAutomaticThemes(true).ApplyTo(store);
        Assert.Empty(store.History);
    }

    [Fact]
    public void TheLocationHotKeySwapsAndKeepsBehindTheIcons()
    {
        AppSettings onTop = Apply(SettingsActions.ToggleLocation(new AppSettings()));
        Assert.Equal(BulbDrawing.OnTop, onTop.Lights.Drawing);
        Assert.True(onTop.Lights.BehindIcons);
        Assert.Equal(BulbDrawing.Desktop, Apply(SettingsActions.ToggleLocation(onTop), onTop).Lights.Drawing);
    }

    [Fact]
    public void TheLocationHotKeyTurnsTheLightsOnOnTop()
    {
        AppSettings off = new AppSettings() with { Lights = new LightsSettings { On = false, Drawing = BulbDrawing.OnTop } };
        SettingsEdit edit = SettingsActions.ToggleLocation(off);
        AppSettings result = Apply(edit, off);
        Assert.True(result.Lights.On);
        Assert.Equal(BulbDrawing.OnTop, result.Lights.Drawing);
        Assert.Equal("Draw the bulbs on top of all windows", edit.Change.Description);
    }

    [Fact]
    public void PlayHolidayMusicInNeverModeSwitchesToAlways()
    {
        AppSettings never = new AppSettings() with { Current = new ThemeableSettings { Music = new CurrentMusic { Mode = PlayMode.Never } } };
        AppSettings result = Apply(SettingsActions.SetMusicEnabled(true), never);
        Assert.True(result.Music.Enabled);
        Assert.Equal(PlayMode.Always, result.Current.Music.Mode);

        AppSettings intermittently = new AppSettings() with { Current = new ThemeableSettings { Music = new CurrentMusic { Mode = PlayMode.Intermittently } } };
        Assert.Equal(PlayMode.Intermittently, Apply(SettingsActions.SetMusicEnabled(true), intermittently).Current.Music.Mode);
    }

    [Fact]
    public void Classic2003IsTheLookOf2003()
    {
        AppSettings result = Apply(SettingsActions.UseClassicLook());
        Assert.Equal(LookPreset.Classic2003, LookPresets.Detect(result.Look));
    }

    [Fact]
    public void LoadingAThemeByHandTurnsAutomaticThemesOffAndRecordsRecentSettings()
    {
        using var root = new TempDataRoot();
        var themes = new ThemeLibrary(root.Paths, new TestHoldingFolder(root.Paths), new RecordingLog());
        themes.Load();
        var service = new ThemeService(themes, new BundledSongs(), new BuiltInResolver());
        ThemeDefinition christmas = themes.Find(ShippedThemeNames.Christmas1)!;
        AppSettings start = new AppSettings() with { Current = new ThemeableSettings { Flash = new FlashSettings { Pattern = FlashPatternId.Twinkle } } };

        SettingsEdit edit = SettingsActions.LoadTheme(service, christmas, new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        AppSettings result = Apply(edit, start);

        Assert.Equal(new SettingsChange(SettingsChangeKind.ThemeLoaded, "Load the Christmas 1 theme"), edit.Change);
        Assert.True(service.Matches(christmas, result));
        Assert.False(result.Calendar.Enabled);
        Assert.Equal("Before Christmas 1", result.RecentSettings[0].Label);
        Assert.Equal("Christmas 1", result.Themes.LastName);
    }
}
