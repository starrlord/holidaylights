using System.Text.Json.Nodes;
using HolidayLights.Core.Settings;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempDataRoot root = new();
    private readonly RecordingLog log = new();

    public void Dispose() => root.Dispose();

    [Fact]
    public async Task Load_MissingFile_IsAFirstRunWithDefaults_WrittenOnFlush()
    {
        Assert.False(JsonSettingsStore.Exists(root.Paths));

        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);

        Assert.Equal(SettingsLoadOutcome.Created, store.LoadOutcome);
        Assert.Equal(HolidayLightsJson.Serialize(new AppSettings()), HolidayLightsJson.Serialize(store.Current));
        Assert.False(File.Exists(root.Paths.SettingsFile));

        await store.FlushAsync();

        Assert.True(JsonSettingsStore.Exists(root.Paths));
    }

    [Fact]
    public async Task Update_PublishesAtOnce_RaisesChanged_AndWritesTheFile()
    {
        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);
        SettingsChangedEventArgs? raised = null;
        store.Changed += (_, e) => raised = e;
        AppSettings before = store.Current;

        store.Update(s => s with { Music = s.Music with { Volume = 35 } }, SettingsChange.Edit("Change the volume"));

        Assert.Equal(35, store.Current.Music.Volume);
        Assert.NotNull(raised);
        Assert.Same(before, raised.OldSettings);
        Assert.Same(store.Current, raised.NewSettings);
        Assert.Equal("Change the volume", raised.Change.Description);

        await store.FlushAsync();
        Assert.Equal(35, HolidayLightsJson.DeserializeSettings(File.ReadAllText(root.Paths.SettingsFile)).Music.Volume);
    }

    [Fact]
    public void Update_TheSameInstance_IsNoChange()
    {
        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);
        int changes = 0;
        store.Changed += (_, _) => changes++;

        store.Update(s => s, SettingsChange.Internal);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Update_SanitizesTheNewSettings()
    {
        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);

        store.Update(s => s with { Current = s.Current with { Flash = new FlashSettings { Interval = 42 } }, Music = s.Music with { Volume = 400 } }, SettingsChange.Internal);

        Assert.Equal(FlashSettings.DefaultInterval, store.Current.Current.Flash.Interval);
        Assert.Equal(100, store.Current.Music.Volume);
    }

    [Fact]
    public async Task Update_WritesAfterTheDebounceWithoutAFlush()
    {
        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);

        store.Update(s => s with { Lights = s.Lights with { On = false } }, SettingsChange.Edit("Turn the lights off"));

        await WaitUntil(() => File.Exists(root.Paths.SettingsFile));

        Assert.False(HolidayLightsJson.DeserializeSettings(File.ReadAllText(root.Paths.SettingsFile)).Lights.On);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripEveryKindOfValue()
    {
        AppSettings settings = NonDefaultSettings();
        using (JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log))
        {
            store.Update(_ => settings, SettingsChange.Internal);
            await store.FlushAsync();
        }

        using JsonSettingsStore reloaded = JsonSettingsStore.Load(root.Paths, log);

        Assert.Equal(SettingsLoadOutcome.Loaded, reloaded.LoadOutcome);
        Assert.Equal(HolidayLightsJson.Serialize(settings), HolidayLightsJson.Serialize(reloaded.Current));
        Assert.Equal(["Halloween", "Autumn"], reloaded.Current.Bulbs.CategoryOverrides["BUILTIN:JACK-O-LANTERNS"]);
        Assert.Empty(Directory.EnumerateFiles(root.Paths.RoamingRoot, "*.tmp"));
    }

    [Fact]
    public void Load_UnreadableFile_IsRenamedAndDefaultsAreUsed()
    {
        WriteSettingsFile("{ this is not json");

        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log, SettingsMigrator.Default, new DateTime(2026, 10, 8, 9, 0, 0));

        Assert.Equal(SettingsLoadOutcome.ReplacedDamaged, store.LoadOutcome);
        Assert.Equal(new AppSettings().Music.Volume, store.Current.Music.Volume);
        string damaged = Path.Combine(root.Paths.RoamingRoot, "settings.damaged-2026-10-08.json");
        Assert.Equal("{ this is not json", File.ReadAllText(damaged));
        Assert.False(File.Exists(root.Paths.SettingsFile));
    }

    [Fact]
    public void Load_DamagedTwiceTheSameDay_KeepsBothCopies()
    {
        var day = new DateTime(2026, 10, 8, 9, 0, 0);
        WriteSettingsFile("[1, 2, 3]");
        JsonSettingsStore.Load(root.Paths, log, SettingsMigrator.Default, day).Dispose();
        WriteSettingsFile("");

        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log, SettingsMigrator.Default, day);

        Assert.Equal(SettingsLoadOutcome.ReplacedDamaged, store.LoadOutcome);
        Assert.True(File.Exists(Path.Combine(root.Paths.RoamingRoot, "settings.damaged-2026-10-08 (2).json")));
    }

    [Fact]
    public void Load_AValueOfTheWrongType_TakesItsDefaultAndKeepsTheRest()
    {
        WriteSettingsFile(
            """
            {
              "version": 1,
              "lights": { "on": false, "drawing": "sideways" },
              "music": { "volume": "loud", "enabled": true },
              "current": { "saver": { "color": "red", "message": "Hello" }, "arrangement": { "top": ["builtin:candy-canes", 7] } },
              "hotkeys": { "location": { "modifiers": ["Ctrl", "Hyper"], "key": "Q" } }
            }
            """);

        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);

        Assert.Equal(SettingsLoadOutcome.Loaded, store.LoadOutcome);
        AppSettings s = store.Current;
        Assert.False(s.Lights.On);
        Assert.Equal(BulbDrawing.Desktop, s.Lights.Drawing);
        Assert.True(s.Music.Enabled);
        Assert.Equal(60, s.Music.Volume);
        Assert.Equal(RgbColor.Red, s.Current.Saver.Color);
        Assert.Equal("Hello", s.Current.Saver.Message);
        Assert.Equal(["builtin:candy-canes"], s.Current.Arrangement.Top);
        Assert.Equal("Q", s.HotKeys.Location.Key);
        Assert.Equal(HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift, s.HotKeys.Location.Modifiers);
        Assert.Contains(log.Entries, e => e.Level == AppLogLevel.Warning && e.Message.Contains("$.music.volume", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_NullSections_AreRepaired()
    {
        WriteSettingsFile("""{ "lights": null, "current": { "music": { "disabledSongs": null }, "arrangement": null }, "colors": { "custom": ["#FFFFFF"] }, "recentSettings": null }""");

        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);

        Assert.True(store.Current.Lights.On);
        Assert.Empty(store.Current.Current.Music.DisabledSongs);
        Assert.Equal(SlotAssignment.Classic54Default, store.Current.Current.Arrangement);
        Assert.Equal(ColorSettings.CustomColorCount, store.Current.Colors.Custom.Count);
        Assert.Equal(RgbColor.White, store.Current.Colors.Custom[0]);
        Assert.Empty(store.Current.RecentSettings);
    }

    [Fact]
    public void Load_AFileOfANewerVersion_KeepsACopyBeforeItIsRewritten()
    {
        WriteSettingsFile("""{ "version": 7, "music": { "volume": 20 }, "lights": { "frameMode": "spiral" }, "futureThing": { "a": 1 } }""");

        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);

        Assert.Equal(20, store.Current.Music.Volume);
        Assert.Equal(FrameMode.EachDisplay, store.Current.Lights.FrameMode);
        Assert.Equal(AppSettings.CurrentVersion, store.Current.Version);
        Assert.Contains("futureThing", File.ReadAllText(Path.Combine(root.Paths.RoamingRoot, "settings.v7.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveFailure_RaisesSaveFailed_KeepsTheChange_AndRetriesWithTheNextChange()
    {
        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);
        var failures = new List<Exception>();
        store.SaveFailed += (_, e) => failures.Add(e.Exception);
        Directory.CreateDirectory(root.Paths.SettingsFile);

        store.Update(s => s with { Music = s.Music with { Volume = 10 } }, SettingsChange.Edit("Change the volume"));
        await store.FlushAsync();

        // SaveFailed is posted to the synchronization context of the caller of Update.
        await WaitUntil(() => failures.Count > 0);
        Assert.Single(failures);
        Assert.Equal(10, store.Current.Music.Volume);

        Directory.Delete(root.Paths.SettingsFile);
        store.Update(s => s with { Music = s.Music with { Volume = 11 } }, SettingsChange.Edit("Change the volume"));
        await store.FlushAsync();

        Assert.Single(failures);
        Assert.Equal(11, HolidayLightsJson.DeserializeSettings(File.ReadAllText(root.Paths.SettingsFile)).Music.Volume);
    }

    [Fact]
    public void Dispose_WritesPendingChanges()
    {
        JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);
        store.Update(s => s with { Startup = new StartupSettings { Auto = false } }, SettingsChange.Edit("Don't start automatically"));

        store.Dispose();

        Assert.False(HolidayLightsJson.DeserializeSettings(File.ReadAllText(root.Paths.SettingsFile)).Startup.Auto);
        Assert.Throws<ObjectDisposedException>(() => store.Update(s => s with { Version = 1 }, SettingsChange.Internal));
    }

    [Fact]
    public void Load_RemovesTemporaryFilesACrashLeftBehind()
    {
        Directory.CreateDirectory(root.Paths.RoamingRoot);
        string leftover = Path.Combine(root.Paths.RoamingRoot, "settings.json.0123456789abcdef.tmp");
        File.WriteAllText(leftover, "{}");

        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log);

        Assert.False(File.Exists(leftover));
    }

    [Fact]
    public void Load_MigratesOldFormats()
    {
        WriteSettingsFile("""{ "version": 1, "music": { "loudness": 25 } }""");
        var migrator = new SettingsMigrator(
            [new SettingsMigration(1, "music.loudness became music.volume", doc => MoveMusicValue(doc, "loudness", "volume"))],
            currentVersion: 2);

        using JsonSettingsStore store = JsonSettingsStore.Load(root.Paths, log, migrator, DateTime.Now);

        Assert.Equal(25, store.Current.Music.Volume);
        Assert.Contains(log.Entries, e => e.Message.Contains("music.loudness became music.volume", StringComparison.Ordinal));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    private static void MoveMusicValue(JsonObject document, string from, string to)
    {
        var music = (JsonObject)document["music"]!;
        JsonNode? value = music[from];
        music.Remove(from);
        music[to] = value;
    }

    private void WriteSettingsFile(string text)
    {
        Directory.CreateDirectory(root.Paths.RoamingRoot);
        File.WriteAllText(root.Paths.SettingsFile, text);
    }

    private static AppSettings NonDefaultSettings() => new()
    {
        Lights = new LightsSettings { On = false, Drawing = BulbDrawing.OnTop, BehindIcons = false, Displays = new DisplaySelection { Disabled = [@"\\?\DISPLAY#DEL41B8#5&1f0b9f0&0&UID4352"] }, FrameMode = FrameMode.AllDisplaysTogether, Size = BulbSize.Large, PatternBeforeDance = FlashPatternId.BulbChase },
        Look = LookPresets.ValuesOf(LookPreset.Classic2003)!,
        Current = new ThemeableSettings
        {
            Arrangement = SlotAssignment.Empty.WithEdge(Side.Top, ["builtin:candy-canes", "addon:MulticolorBubbleLights"]).WithCorner(Corner.BottomLeft, "user:Star"),
            Flash = new FlashSettings { Pattern = FlashPatternId.Twinkle, Interval = 2 },
            Music = new CurrentMusic { DisabledSongs = ["bundled:Jingle Bells.mid"], Mode = PlayMode.Intermittently },
            Saver = new SaverLook { Animation = SaverAnimations.ForBulb("addon:Arrow"), Style = SaverMovementStyle.Attraction, Message = "Ho ho ho", Font = new SaverFont { Family = "Georgia", SizePt = 42, Italic = true, Bold = false }, Color = new RgbColor(1, 2, 3), Background = new RgbColor(4, 5, 6), Picture = "user:Family.jpg", Placement = PicturePlacement.Tile },
        },
        Music = new MusicSettings { Enabled = true, Volume = 45, Muted = true, MidiDevice = "Microsoft GS Wavetable Synth", SyncOffsetMs = 55 },
        Saver = new SaverDeviceSettings { ShowOn = SaverDisplays.MainOnly, Previous = new ScreenSaverPrevious { ScrnsaveExe = @"C:\WINDOWS\system32\HOLIDA~1.SCR", Active = true, TimeoutSeconds = 1200 } },
        Colors = new ColorSettings { Custom = [.. Enumerable.Range(0, 16).Select(i => new RgbColor((byte)i, (byte)(i * 2), (byte)(i * 3)))] },
        Calendar = new CalendarSettings { Enabled = false, Region = "CA", Between = "Bubble Lights", Notify = false, ActiveEntryId = "between" },
        HotKeys = new HotKeySettings { Location = new HotKeyBinding { Enabled = false, Key = "H" }, Lights = new HotKeyBinding { Enabled = true, Modifiers = HotKeyModifiers.Win | HotKeyModifiers.Shift, Key = "F12" } },
        Startup = new StartupSettings { Auto = false },
        Rest = new RestSettings { FullScreen = false, Presentation = false, EnergySaver = EnergySaverChoice.TurnOffLights, MusicFullScreen = false, MusicFocus = false, MusicLock = false },
        Accessibility = new AccessibilitySettings { LimitFlashing = true },
        Ui = new UiSettings { DecorateWindow = false, Settings = new SettingsWindowSettings { LastPage = SettingsPageId.Themes, Window = new WindowPlacementSettings { Left = -3000, Top = 40, Width = 1300, Height = 900, Maximized = true } }, Gallery = new GallerySettings { View = BulbListView.Details, Sort = BulbSortOrder.Newest } },
        Bulbs = new BulbPreferences { Favorites = ["addon:Arrow"], Hidden = ["addon:2pac"], CategoryOverrides = new Dictionary<string, IReadOnlyList<string>>(BulbIds.Comparer) { ["builtin:jack-o-lanterns"] = ["Halloween", "Autumn"] } },
        Songs = new HiddenItems { Hidden = ["bundled:Clementine.mid"] },
        Pictures = new HiddenItems { Hidden = ["bundled:Witch.BMP"] },
        Files = new FileSettings { AssociateBul = false },
        Themes = new ThemePreferences { LastName = "Grandma's Lights" },
        RecentSettings = [new RecentSettingsEntry { Label = "Before Halloween (automatic)", Date = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-5)), Values = new ThemeableSettings() }],
        Import54 = new Import54Record { Date = new DateOnly(2026, 10, 8), FactoryDefaults = true, Themes = 11, Report = [new ImportReportItem { Item = "Path", Status = ImportItemStatus.NotImported, Reason = "obsolete" }] },
        Onboarding = new OnboardingState { WelcomeShown = true, CloseNotified = true, LocationHotKeyHints = 2, AutomaticEditHintShown = true, HotKeyImportNoticeDismissed = true, LastNotified = new Dictionary<string, DateOnly> { ["ThemeChanged"] = new(2026, 10, 1) } },
    };
}
