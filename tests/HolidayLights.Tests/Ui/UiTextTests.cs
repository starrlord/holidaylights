using System.Globalization;
using HolidayLights.App.Controls;
using HolidayLights.App.Gallery;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Dialogs;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Audio;
using HolidayLights.Core.Seasons;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>The sentences and small rules of the Settings window (PRODUCT-SPEC 3.1-3.8, Appendix D), in US English.</summary>
[Collection(nameof(WpfCollection))]
public sealed class UiTextTests : IDisposable
{
    private static readonly BulbCatalogStatus Complete = new(1501, 1501, true);
    private readonly CultureInfo culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo uiCulture = CultureInfo.CurrentUICulture;
    private readonly UiThread ui;

    public UiTextTests(UiThread ui)
    {
        this.ui = ui;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture;
    }

    [Fact]
    public void ResultLineCountsAndQuotesTheSearch()
    {
        Assert.Equal("1,550 bulbs", BulbShowOptions.ResultLine(1550, "", Complete));
        Assert.Equal("1 bulb", BulbShowOptions.ResultLine(1, "  ", Complete));
        Assert.Equal("37 bulbs match \"snow\"", BulbShowOptions.ResultLine(37, " snow ", Complete));
        Assert.Equal("1 bulb matches \"zombie dog\"", BulbShowOptions.ResultLine(1, "zombie dog", Complete));
        Assert.Equal("Loading add-on bulbs… 640 of 1,501", BulbShowOptions.ResultLine(689, "", new BulbCatalogStatus(640, 1501, false)));
        Assert.Equal("Search 1,550 bulbs", BulbShowOptions.Placeholder(1550));
    }

    [Fact]
    public void EmptyStatesFollowTheShowFilterAndTheSearch()
    {
        ShowOption Option(string key) => new(key, BulbFilter.All, key, 0, []);
        Assert.Equal("No bulbs match \"zombie dog\".", BulbShowOptions.EmptyText(Option(ShowOption.AllKey), "zombie dog"));
        Assert.Equal("No favorites yet. Click the star on any bulb to keep it here.", BulbShowOptions.EmptyText(Option(ShowOption.FavoritesKey), ""));
        Assert.Equal("No bulbs are on your screen. Double-click a bulb, or drag bulbs into the boxes.", BulbShowOptions.EmptyText(Option(ShowOption.InUseKey), ""));
        Assert.StartsWith("Bulbs you add or make appear here.", BulbShowOptions.EmptyText(Option(ShowOption.MyBulbsKey), ""), StringComparison.Ordinal);
    }

    [Fact]
    public void InUseListsEveryEdgeAndCornerOnce()
    {
        IReadOnlySet<string> ids = BulbShowOptions.InUse(SlotAssignment.Classic54Default);
        Assert.Contains(BulbIds.BuiltIn("standard-bulbs"), ids);
        Assert.Contains(BulbIds.BuiltIn("jolly-holly"), ids);
        Assert.Contains(BulbIds.BuiltIn("snow-family"), ids);
        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public void ImportSummariesFollowAppendixD()
    {
        Assert.Null(BulbImportTexts.Summary(0, 2));
        Assert.Equal("Added 3 bulbs.", BulbImportTexts.Summary(3, 0));
        Assert.Equal("Added 3 bulbs. 1 file was already in your bulbs.", BulbImportTexts.Summary(3, 1));
        Assert.Equal("Added 1 bulb. 2 files were already in your bulbs.", BulbImportTexts.Summary(1, 2));
        Assert.Equal("Star is already in your bulbs.", BulbImportTexts.AlreadyPresent("Star"));
        Assert.Equal("Couldn't copy \"Star.bul\": Access is denied.", BulbImportTexts.CopyFailed(@"C:\x\Star.bul", "Access is denied."));
        Assert.Equal("Problem Importing File: Sorry, that bulb file is damaged and can't be used.", BulbImportTexts.DamagedText);
        Assert.Equal("Cannot Import GIF File: Sorry, this GIF file can't be imported. It may be damaged in some way.", BulbImportTexts.GifText);
    }

    [Theory]
    [InlineData("Star.bul", true, false, false)]
    [InlineData("Snow.GIF", true, false, false)]
    [InlineData("Jingle.mid", false, true, false)]
    [InlineData("Song.M4A", false, true, false)]
    [InlineData("Tree.jpeg", false, false, true)]
    [InlineData("Notes.txt", false, false, false)]
    public void DroppedFilesAreSortedByType(string file, bool bulb, bool song, bool picture)
    {
        Assert.Equal(bulb, DroppedFiles.IsBulbFile(file));
        Assert.Equal(song, DroppedFiles.IsSong(file));
        Assert.Equal(picture, DroppedFiles.IsPicture(file));
        Assert.Equal("Added 2 songs.", DroppedFiles.Added(2, "song"));
        Assert.Equal("Added 1 picture.", DroppedFiles.Added(1, "picture"));
    }

    [Fact]
    public void MusicStatusTextsFollowTheMusicBoxTable()
    {
        DateTimeOffset now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Music is off.", MusicStatusText.Text(MusicState.Initial, now, forHome: false));
        Assert.Equal("Next song in 1:24", MusicStatusText.Text(new MusicState { Status = MusicStatus.BetweenSongs, NextSongAt = now.AddSeconds(84) }, now, forHome: false));
        Assert.Equal("Music plays only while the screen saver is on.", MusicStatusText.Text(new MusicState { Status = MusicStatus.WaitingForScreenSaver }, now, forHome: true));
        Assert.Equal("Music plays only while the Holiday Lights screen saver is showing.", MusicStatusText.Text(new MusicState { Status = MusicStatus.WaitingForScreenSaver }, now, forHome: false));
        Assert.Equal("No songs are checked, so no music will play.", MusicStatusText.Text(new MusicState { Status = MusicStatus.NoSongs }, now, forHome: true));
        Assert.NotEmpty(MusicStatusText.Text(new MusicState { Status = MusicStatus.WaitingForScreenSaverToEnd }, now, forHome: false));
        Assert.Equal("0:05", MusicStatusText.Duration(TimeSpan.FromSeconds(4.2)));
        Assert.Equal("2:13", MusicStatusText.Duration(TimeSpan.FromSeconds(133)));
    }

    [Fact]
    public void PausedMusicNamesTheRuleThatHoldsIt()
    {
        DateTimeOffset now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var paused = new MusicState { Status = MusicStatus.PausedByRules };
        var rest = new RestSettings();
        LightsStatus running = LightsStatus.Initial with { Health = LightsHealth.Running };

        // A full-screen app or a presentation shows in the lights' status; otherwise the Focus session is the reason.
        LightsStatus fullScreen = running with { Displays = [new DisplayLayerStatus("2", null, Resting: true)] };
        Assert.Equal(MusicPauseReason.FullScreen, MusicStatusText.PauseReason(rest, fullScreen));
        Assert.Equal(MusicPauseReason.Presentation, MusicStatusText.PauseReason(rest, running with { Paused = PauseReasons.Presentation }));
        Assert.Equal(MusicPauseReason.FocusSession, MusicStatusText.PauseReason(rest, running));
        Assert.Equal("Paused while a full-screen app is open", MusicStatusText.Text(paused, now, forHome: false, MusicPauseReason.FullScreen));
        Assert.Equal("Paused during your Focus session", MusicStatusText.Text(paused, now, forHome: false, MusicPauseReason.FocusSession));

        // When the lights do not watch full-screen apps, an unseen one could be the reason too.
        Assert.Equal(MusicPauseReason.Unknown, MusicStatusText.PauseReason(rest with { FullScreen = false }, running));
        Assert.Equal(MusicPauseReason.FullScreen, MusicStatusText.PauseReason(rest with { FullScreen = false, MusicFocus = false }, running));
        Assert.Equal(MusicPauseReason.FocusSession, MusicStatusText.PauseReason(rest with { MusicFullScreen = false }, fullScreen));
        Assert.Equal(
            "Paused while a full-screen app, a presentation or a Focus session is on",
            MusicStatusText.Text(paused, now, forHome: false, MusicPauseReason.Unknown));
    }

    [Theory]
    [InlineData("Happy Halloween!", "Happy Halloween!")]
    [InlineData("\r\n  Merry Christmas!\r\nfrom us", "Merry Christmas!")]
    [InlineData("", "Holiday Lights")]
    [InlineData("A message that is much longer than forty characters in all", "A message that is much longer than forty")]
    public void HomeGreetingIsTheFirstLineOfTheMessage(string message, string greeting) =>
        Assert.Equal(greeting, HomePage.Greeting(message));

    [Fact]
    public void ScreenSaverMessageLosesOnlyLeadingBlanks()
    {
        Assert.Equal("Happy Holidays!", ScreenSaverPage.CleanMessage("   Happy Holidays!"));
        // 5.4 removes only the characters below '!' at the very start; blank lines inside the message stay.
        Assert.Equal("Line one\r\n\r\nLine two", ScreenSaverPage.CleanMessage("\r\n\r\nLine one\r\n\r\nLine two"));
        Assert.Equal("Line one\r\n", ScreenSaverPage.CleanMessage("Line one\r\n"));
        Assert.Equal("", ScreenSaverPage.CleanMessage("  \r\n "));
    }

    [Fact]
    public void StartAfterChoicesAreNamedLikeWindows()
    {
        Assert.Equal("1 minute", ScreenSaverPage.MinutesText(1));
        Assert.Equal("45 minutes", ScreenSaverPage.MinutesText(45));
        Assert.Equal("1 hour", ScreenSaverPage.MinutesText(60));
        Assert.Equal("2 hours", ScreenSaverPage.MinutesText(120));
    }

    [Fact]
    public void CalendarRulesReadAsTheDialogShowsThem()
    {
        Assert.Equal("14 days before Easter to Easter Monday", ThemeCalendarDialog.RuleText(new CalendarRule { Type = CalendarRuleType.Easter, FromOffset = -14, ToOffset = 1 }));
        Assert.Equal("Nov 1 to Thanksgiving Day", ThemeCalendarDialog.RuleText(new CalendarRule { Type = CalendarRuleType.UsThanksgiving, From = "11-01" }));
        Assert.Equal("Day after Thanksgiving to Dec 30", ThemeCalendarDialog.RuleText(new CalendarRule { Type = CalendarRuleType.AfterUsThanksgiving, To = "12-30" }));
        Assert.Equal("The eight nights of Chanukah", ThemeCalendarDialog.RuleText(new CalendarRule { Type = CalendarRuleType.Chanukah }));
        Assert.Equal("Dec 31 to Jan 1", ThemeCalendarDialog.RuleText(new CalendarRule { From = "12-31", To = "01-01" }));
        Assert.Equal("", ThemeCalendarDialog.MonthDayText("13-40"));
    }

    [Fact]
    public void AutomaticThemesStatusLineNamesTodayAndTheNextChange()
    {
        var seasons = new SeasonCalendar();
        var calendar = new CalendarSettings();
        Assert.Equal("Today: Halloween (until Oct 31). Next: Thanksgiving on Nov 1.", ThemesPage.CalendarText(seasons, calendar, new DateOnly(2026, 10, 8)));
        Assert.Equal("Automatic themes are off. Your lights stay as they are.", ThemesPage.CalendarText(seasons, calendar with { Enabled = false }, new DateOnly(2026, 10, 8)));
        Assert.StartsWith("Today: between holidays (Classic Lights).", ThemesPage.CalendarText(seasons, calendar, new DateOnly(2026, 7, 15)), StringComparison.Ordinal);
    }

    [Fact]
    public void ColorConversionsRoundTrip()
    {
        foreach (RgbColor color in ColorDialog.BasicColors)
        {
            (double h, double s, double l) = ColorDialog.ToHsl(color);
            RgbColor back = ColorDialog.FromHsl(h, s, l);
            Assert.InRange(Math.Abs(back.R - color.R), 0, 1);
            Assert.InRange(Math.Abs(back.G - color.G), 0, 1);
            Assert.InRange(Math.Abs(back.B - color.B), 0, 1);
        }

        Assert.Equal(48, ColorDialog.BasicColors.Count);
    }

    [Fact]
    public void HotKeyMessagesFollowTheChangeHotKeyDialog()
    {
        var binding = new HotKeyBinding { Modifiers = HotKeyModifiers.Ctrl | HotKeyModifiers.Shift, Key = "B" };
        Assert.Equal("Ctrl+Shift+B is the bookmarks-bar shortcut in web browsers. Holiday Lights would take it from them.",
            ChangeHotKeyDialog.MessageFor(new HotKeyCheck(HotKeyValidity.BrowserShortcut), binding));
        Assert.Equal("That would stop you from typing capital letters.", ChangeHotKeyDialog.MessageFor(new HotKeyCheck(HotKeyValidity.ShiftLetterOnly), binding));
        Assert.Equal("This combination types \"Ł\" on your Polish (Programmers) keyboard. Choose another.",
            ChangeHotKeyDialog.MessageFor(new HotKeyCheck(HotKeyValidity.TypesCharacter, "Ł", "Polish (Programmers)"), binding));
        Assert.Equal("", ChangeHotKeyDialog.MessageFor(new HotKeyCheck(HotKeyValidity.Ok), binding));
        Assert.Equal("Ctrl+Alt+Shift+B", HotKeyText.Compact(new HotKeyBinding()));
        Assert.Equal("Ctrl Alt Shift B", HotKeyText.Spoken(new HotKeyBinding()));
    }

    [Fact]
    public void ImportDetailsDescribeEveryStatus()
    {
        Assert.Equal("Imported", ImportDetailsDialog.StatusText(new ImportReportItem { Status = ImportItemStatus.Imported }));
        Assert.Equal("Already included", ImportDetailsDialog.StatusText(new ImportReportItem { Status = ImportItemStatus.AlreadyIncluded }));
        Assert.Equal("Not imported - obsolete", ImportDetailsDialog.StatusText(new ImportReportItem { Status = ImportItemStatus.NotImported, Reason = "obsolete" }));
    }

    [Fact]
    public void RecentlyLoadedThemesComeFromRecentSettingsLabels()
    {
        var settings = new AppSettings
        {
            RecentSettings =
            [
                new RecentSettingsEntry { Label = "Before Halloween (automatic)", Date = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) },
                new RecentSettingsEntry { Label = "Holiday Lights 5.4 Settings", Date = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero) },
                new RecentSettingsEntry { Label = "Before Christmas 1", Date = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero) },
            ],
        };
        Assert.Equal(["Christmas 1", "Halloween"], ThemeActions.RecentlyLoaded(settings));
        Assert.Equal("Before Halloween", ThemeActions.BeforeLabel("Halloween"));
        Assert.Equal("Snow", ThemeActions.AnimationName("Snow", null));
        Assert.Equal("Bulb: Star", ThemeActions.AnimationName(SaverAnimations.ForBulb(BulbIds.User("Star")), null));
    }

    [Fact]
    public void ThemeSummaryNamesPatternMusicAndAnimation()
    {
        var songs = new FakeSongs(46);
        var theme = new ThemeDefinition
        {
            Name = "Grandma's Lights",
            Flash = new ThemeFlash { Pattern = FlashPatternId.Twinkle },
            Music = new ThemeMusic { EnabledSongs = [.. Enumerable.Range(0, 12).Select(i => $"bundled:{i}.mid")], Mode = PlayMode.Always },
            Saver = new ThemeSaver { Animation = "Santa" },
        };
        Assert.Equal("Twinkle - 12 songs, Always - Santa", ThemeActions.Summary(theme, songs));
        Assert.Equal("Flash Together - no music - Snow", ThemeActions.Summary(new ThemeDefinition { Name = "Quiet", Music = new ThemeMusic { EnabledSongs = [] } }, songs));
    }

    [Fact]
    public void StatusLinesFollowTheHomeTable()
    {
        var status = new LightsStatus { Health = LightsHealth.Running, Displays = [] };
        var scene = LightsScene.Empty with { LightsOn = true };
        var settings = new AppSettings();
        Assert.Equal(new StatusLine("The lights are off.", "Turn On", StatusAction.TurnOn),
            LightsStatusText.Home(settings with { Lights = settings.Lights with { On = false } }, scene, status, null));
        Assert.Equal(new StatusLine("No bulbs yet.", "Change Bulbs", StatusAction.ChangeBulbs),
            LightsStatusText.Home(settings with { Current = settings.Current with { Arrangement = SlotAssignment.Empty } }, scene, status, null));
        Assert.Equal("Your lights can't be drawn right now (no graphics device). Holiday Lights keeps trying.",
            LightsStatusText.Home(settings, scene, status with { Health = LightsHealth.NoGraphicsDevice }, null).Text);
        Assert.Equal("Your lights are on behind your desktop icons on 1 display. Next: Thanksgiving on Nov 1.",
            LightsStatusText.Home(settings, scene, status, (new DateOnly(2026, 11, 1), "Thanksgiving")).Text);
        Assert.Equal("Your lights are on in front of your desktop icons on 1 display.",
            LightsStatusText.Home(settings with { Lights = settings.Lights with { BehindIcons = false } }, scene, status, null).Text);

        // On Top never reads "on on top".
        Assert.Equal("Your lights are shining on top of all windows on 1 display.",
            LightsStatusText.Home(settings with { Lights = settings.Lights with { Drawing = BulbDrawing.OnTop } }, scene, status, null).Text);
    }

    [Fact]
    public void BulbItemsDescribeThemselves()
    {
        var info = new BulbInfo
        {
            Id = BulbIds.BuiltIn("candy-canes"),
            Origin = BulbOrigin.BuiltIn,
            Name = "Candy Canes",
            Author = "Joe Lachoff\r\njoe@example.com",
            Copyright = "Copyright 2003",
            TopFlavorCount = 4,
            TopKind = BulbAnimationKind.Animation,
            LargestTopCell = new SizeI(32, 32),
        };
        var item = new BulbItem(info, isFavorite: true, isInUse: true, isRemoved: false, DateTimeOffset.Now);
        Assert.Equal("Candy Canes, built-in bulb, 4 colors, animated, favorite, in use", item.AccessibleName);
        Assert.Equal("4 colors - animated - 32 x 32 px", item.FactsText);
        Assert.Equal("Art: Joe Lachoff - Copyright 2003", item.CreditsText);
        Assert.Equal("Built-In - Joe Lachoff", item.DetailsCaption);
        Assert.False(item.IsNew);
        Assert.True(new BulbItem(info with { AddedDate = DateTimeOffset.Now.AddHours(-2) }, false, false, false, DateTimeOffset.Now).IsNew);
    }

    [Fact]
    public void PlacementOpensCenteredOnThePointerDisplayOrWhereItWas()
    {
        IReadOnlyList<DisplayInfo> displays = Fakes.FakeDisplayService.ReferencePc();
        DisplayInfo main = displays.First(d => d.IsPrimary);
        WindowPlacementPlan fresh = WindowPlacement.Plan(null, displays, main);
        Assert.Equal(new System.Windows.Size(1240, 800), fresh.Size);
        Assert.Equal(main, fresh.Display);
        Assert.Equal((main.WorkArea.Width - 1860) / 2, fresh.TopLeft.X - main.WorkArea.Left);

        var remembered = new WindowPlacementSettings { Left = -2000, Top = 100, Width = 900, Height = 600, Maximized = true };
        WindowPlacementPlan back = WindowPlacement.Plan(remembered, displays, main);
        Assert.False(back.Display.IsPrimary);
        Assert.True(back.Maximized);
        Assert.Equal(new System.Windows.Size(900, 600), back.Size);
        Assert.Equal(new System.Windows.Size(760, 560), WindowPlacement.ClampSize(new System.Windows.Size(100, 100), main));
    }

    [Fact]
    public void HighContrastTurnsNightWellsAndTheirTextIntoSystemColors() => ui.Run(() =>
    {
        var resources = new System.Windows.ResourceDictionary();
        HolidayLights.App.Styles.HighContrastResources.Apply(resources, highContrast: true);
        Assert.Same(System.Windows.SystemColors.WindowBrush, resources[AppResourceKeys.NightWellBrush]);
        Assert.Same(System.Windows.SystemColors.WindowTextBrush, resources[HolidayLights.App.Styles.ThemeKeys.OnNightWellBrush]);
        Assert.Same(System.Windows.SystemColors.GrayTextBrush, resources[HolidayLights.App.Styles.ThemeKeys.OnNightWellSecondaryBrush]);
        Assert.Same(System.Windows.SystemColors.WindowBrush, resources[HolidayLights.App.Styles.ThemeKeys.StagePillBrush]);
        HolidayLights.App.Styles.HighContrastResources.Apply(resources, highContrast: false);
        Assert.Empty(resources.Keys.Cast<object>());
    });

    /// <summary>A song library with a number of songs (theme summaries count every song when a theme lists none).</summary>
    private sealed class FakeSongs(int count) : ISongLibrary
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<SongInfo> Songs { get; } =
            [.. Enumerable.Range(0, count).Select(i => new SongInfo { Id = $"bundled:{i}.mid", Title = $"Song {i}", FilePath = $"{i}.mid", Origin = MediaOrigin.Bundled, Kind = SongKind.Midi, SortKey = $"{i}" })];

        public IReadOnlyList<SongInfo> HiddenSongs => [];

        public void Start()
        {
        }

        public bool TryGetSong(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SongInfo? song)
        {
            song = Songs.FirstOrDefault(s => s.Id == id);
            return song is not null;
        }

        public IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths) => [];

        public HeldItem? Remove(string id) => null;

        public void Restore(HeldItem item)
        {
        }

        public void RestoreHiddenSongs()
        {
        }
    }
}
