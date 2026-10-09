using HolidayLights.App.Shell;
using HolidayLights.App.Tray;
using HolidayLights.Core.Seasons;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.App.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.App;

/// <summary>
/// Automatic themes in the running app (PRODUCT-SPEC 5.11, acceptance 7.5 #19): switching only at boundaries, waiting
/// while Settings is open, Recent Settings, the notification.
/// </summary>
public sealed class AutomaticThemeTests : IDisposable
{
    private static readonly TimeSpan Pacific = TimeSpan.FromHours(-7);

    private readonly TempDataRoot root = new();
    private readonly SeasonCalendar calendar = new();
    private readonly InMemorySettingsStore settings = new(new AppSettings() with { Calendar = new CalendarSettings { ActiveEntryId = null } });
    private readonly FakeSettingsWindow window = new();
    private readonly List<(string Title, string Text)> balloons = [];
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 8, 9, 0, 0, Pacific));
    private readonly FakeServices services;

    public AutomaticThemeTests()
    {
        var themes = new ThemeLibrary(root.Paths, new TestHoldingFolder(root.Paths), new RecordingLog());
        themes.Load();
        services = new FakeServices
        {
            Paths = root.Paths,
            Settings = settings,
            Calendar = calendar,
            Themes = themes,
            ThemeService = new ThemeService(themes, new BundledSongs(), new BuiltInResolver()),
            SettingsWindow = window,
        };
    }

    public void Dispose() => root.Dispose();

    private AutomaticThemeScheduler Scheduler() =>
        new(services, new NotificationService(services, (title, text) =>
        {
            balloons.Add((title, text));
            return true;
        }, () => PauseState.None, time), time);

    private static CalendarResolution Resolve(int month, int day, int year = 2026) =>
        new SeasonCalendar().Resolve(new CalendarSettings(), new DateOnly(year, month, day));

    [Theory]
    [InlineData(10, 31, "Halloween")]
    [InlineData(11, 1, "Thanksgiving")]
    [InlineData(11, 27, "Christmas 1")]
    [InlineData(12, 31, "New Year")]
    [InlineData(7, 15, "Classic Lights")]
    public void TheUnitedStatesCalendar(int month, int day, string theme) => Assert.Equal(theme, Resolve(month, day).ThemeName);

    [Fact]
    public void OffMeansNothing()
    {
        CalendarSettings off = new() { Enabled = false };
        Assert.Equal(AutomaticThemeAction.None, AutomaticThemeRules.Decide(off, calendar, new DateOnly(2026, 10, 8), false, turnedOn: true).Action);
    }

    [Fact]
    public void OnlyABoundarySwitches()
    {
        var halloween = new CalendarSettings { ActiveEntryId = CalendarEntryIds.Halloween };
        Assert.Equal(AutomaticThemeAction.None, AutomaticThemeRules.Decide(halloween, calendar, new DateOnly(2026, 10, 20), false, false).Action);

        AutomaticThemeDecision november = AutomaticThemeRules.Decide(halloween, calendar, new DateOnly(2026, 11, 1), false, false);
        Assert.Equal(AutomaticThemeAction.Switch, november.Action);
        Assert.Equal(CalendarEntryIds.Thanksgiving, november.ActiveEntryId);
        Assert.Equal("Thanksgiving", november.Resolution!.ThemeName);

        Assert.Equal(AutomaticThemeAction.Wait, AutomaticThemeRules.Decide(halloween, calendar, new DateOnly(2026, 11, 1), settingsOpen: true, false).Action);
    }

    [Fact]
    public void BetweenHolidaysIsAnEntryOfItsOwn()
    {
        AutomaticThemeDecision july = AutomaticThemeRules.Decide(new CalendarSettings { ActiveEntryId = CalendarEntryIds.July4th }, calendar, new DateOnly(2026, 7, 15), false, false);
        Assert.Equal(CalendarEntryIds.Between, july.ActiveEntryId);
        Assert.Equal("Classic Lights", july.Resolution!.ThemeName);
    }

    [Fact]
    public void TurningAutomaticThemesOnAppliesTodaysThemeAtOnce()
    {
        var halloween = new CalendarSettings { ActiveEntryId = CalendarEntryIds.Halloween };
        Assert.Equal(AutomaticThemeAction.Switch, AutomaticThemeRules.Decide(halloween, calendar, new DateOnly(2026, 10, 20), settingsOpen: true, turnedOn: true).Action);
    }

    [Fact]
    public void TheNotificationTitleIsTheThemesMessage()
    {
        var theme = new ThemeDefinition { Name = "Halloween", Saver = new ThemeSaver { Message = "Happy Halloween!" } };
        Assert.Equal(("Happy Halloween!", "Your lights switched to the Halloween theme. Click to undo or choose another."), AutomaticThemeRules.Notification(theme));
        Assert.Equal("Holiday Lights", AutomaticThemeRules.Notification(new ThemeDefinition { Name = "Classic Lights" }).Title);
        Assert.Equal("Merry", AutomaticThemeRules.Notification(new ThemeDefinition { Name = "X", Saver = new ThemeSaver { Message = "\r\nMerry\r\nChristmas" } }).Title);
    }

    [Fact]
    public void ASwitchAppliesTheThemeRecordsRecentSettingsAndNotifies()
    {
        StaThread.Run(() =>
        {
            AutomaticThemeScheduler scheduler = Scheduler();
            AutomaticThemeDecision decision = scheduler.Evaluate(turnedOn: false);

            Assert.Equal(AutomaticThemeAction.Switch, decision.Action);
            AppSettings now = settings.Current;
            Assert.Equal(CalendarEntryIds.Halloween, now.Calendar.ActiveEntryId);
            Assert.True(now.Calendar.Enabled);
            Assert.True(services.ThemeService.Matches(services.Themes.Find("Halloween")!, now));
            Assert.Equal("Before Halloween (automatic)", now.RecentSettings[0].Label);
            Assert.Single(settings.History, change => change.Kind == SettingsChangeKind.AutomaticTheme);
            Assert.Equal(("Happy Halloween!", "Your lights switched to the Halloween theme. Click to undo or choose another."), Assert.Single(balloons));

            // The same day again: no boundary, no switch.
            Assert.Equal(AutomaticThemeAction.None, scheduler.Evaluate(turnedOn: false).Action);
            Assert.Single(balloons);
            Assert.Single(settings.History, change => change.Kind == SettingsChangeKind.AutomaticTheme);
        });
    }

    [Fact]
    public void NoNotificationWhenTheUserAskedNotToBeTold()
    {
        StaThread.Run(() =>
        {
            settings.Update(s => s with { Calendar = s.Calendar with { Notify = false } }, SettingsChange.Internal);
            Scheduler().Evaluate(turnedOn: false);
            Assert.Empty(balloons);
            Assert.Equal(CalendarEntryIds.Halloween, settings.Current.Calendar.ActiveEntryId);
        });
    }

    [Fact]
    public void ABoundaryWaitsForTheSettingsWindowToClose()
    {
        StaThread.Run(() =>
        {
            AutomaticThemeScheduler scheduler = Scheduler();
            window.IsOpen = true;
            Assert.Equal(AutomaticThemeAction.Wait, scheduler.Evaluate(turnedOn: false).Action);
            Assert.True(scheduler.IsWaiting);
            Assert.Null(settings.Current.Calendar.ActiveEntryId);

            window.IsOpen = false;
            Assert.Equal(AutomaticThemeAction.Switch, scheduler.Evaluate(turnedOn: false).Action);
            Assert.False(scheduler.IsWaiting);
        });
    }

    [Fact]
    public void ThanksgivingFollowsHalloweenAtMidnight()
    {
        StaThread.Run(() =>
        {
            AutomaticThemeScheduler scheduler = Scheduler();
            scheduler.Evaluate(turnedOn: false);
            time.Now = new DateTimeOffset(2026, 11, 1, 0, 0, 1, Pacific);
            Assert.Equal(AutomaticThemeAction.Switch, scheduler.Evaluate(turnedOn: false).Action);
            Assert.True(services.ThemeService.Matches(services.Themes.Find("Thanksgiving")!, settings.Current));
            Assert.Equal("Before Thanksgiving (automatic)", settings.Current.RecentSettings[0].Label);

            // One notification per day at most (PRODUCT-SPEC 3.12); this one is on another day.
            Assert.Equal(2, balloons.Count);
        });
    }

    [Fact]
    public void ADeletedShippedThemeStillShowsFromItsOriginal()
    {
        StaThread.Run(() =>
        {
            services.Themes.Delete("Halloween");
            Assert.Null(services.Themes.Find("Halloween"));
            Scheduler().Evaluate(turnedOn: false);
            Assert.Equal("Halloween", settings.Current.Themes.LastName);
        });
    }

    [Fact]
    public void TheNextMidnightRespectsTheTimeZone()
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("UTC-7", Pacific, "UTC-7", "UTC-7");
        DateTimeOffset evening = new DateTimeOffset(2026, 10, 31, 23, 30, 0, Pacific).ToUniversalTime();
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, Pacific), AutomaticThemeScheduler.NextLocalMidnight(evening, zone));
    }
}
