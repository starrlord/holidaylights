using HolidayLights.App.Shell;
using HolidayLights.App.Tray;
using HolidayLights.Tests.App.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.App;

/// <summary>The notifications of PRODUCT-SPEC 3.12: throttling, quiet moments and where a click leads.</summary>
public sealed class NotificationTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);

    private readonly InMemorySettingsStore settings = new();
    private readonly FakeSettingsWindow window = new();
    private readonly List<(string Title, string Text)> balloons = [];
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(-7)));
    private PauseState pause = PauseState.None;

    private NotificationService Service(bool trayShown = true) => new(
        new FakeServices { Settings = settings, SettingsWindow = window },
        (title, text) =>
        {
            balloons.Add((title, text));
            return trayShown;
        },
        () => pause,
        time);

    [Theory]
    [InlineData(NotificationKind.FirstClose, NotificationLimit.OnceEver)]
    [InlineData(NotificationKind.ThemeChanged, NotificationLimit.OncePerDay)]
    [InlineData(NotificationKind.HotKeyUnavailable, NotificationLimit.OncePerChange)]
    [InlineData(NotificationKind.MusicWaiting, NotificationLimit.OncePerSession)]
    [InlineData(NotificationKind.SettingsReset, NotificationLimit.OncePerDay)]
    [InlineData(NotificationKind.LightsRestarted, NotificationLimit.OncePerSession)]
    public void EveryKindHasItsLimit(NotificationKind kind, NotificationLimit limit) => Assert.Equal(limit, NotificationThrottle.LimitOf(kind));

    [Fact]
    public void OncePerDayIsRememberedAcrossSessions()
    {
        var onboarding = new OnboardingState();
        Assert.True(NotificationThrottle.IsAllowed(NotificationKind.ThemeChanged, onboarding, new HashSet<NotificationKind>(), Today));
        OnboardingState shown = NotificationThrottle.Record(NotificationKind.ThemeChanged, onboarding, Today);
        Assert.Equal(Today, shown.LastNotified["ThemeChanged"]);
        Assert.False(NotificationThrottle.IsAllowed(NotificationKind.ThemeChanged, shown, new HashSet<NotificationKind>(), Today));
        Assert.True(NotificationThrottle.IsAllowed(NotificationKind.ThemeChanged, shown, new HashSet<NotificationKind>(), Today.AddDays(1)));
        Assert.True(NotificationThrottle.IsAllowed(NotificationKind.SettingsReset, shown, new HashSet<NotificationKind>(), Today));
    }

    [Fact]
    public void AHotKeyInUseIsRememberedPerCombinationNotPerDay()
    {
        var location = new HotKeyBinding { Enabled = true, Key = "B" };
        var other = new HotKeyBinding { Enabled = true, Key = "H" };
        var onboarding = new OnboardingState { LastNotified = new Dictionary<string, DateOnly> { ["ThemeChanged"] = Today } };
        Assert.True(NotificationThrottle.IsAllowed(NotificationKind.HotKeyUnavailable, onboarding, new HashSet<NotificationKind> { NotificationKind.HotKeyUnavailable }, Today));
        Assert.False(NotificationThrottle.WasHotKeyReported(onboarding, HotKeyAction.SwitchLocation, location));

        OnboardingState reported = NotificationThrottle.RecordHotKeyReported(onboarding, HotKeyAction.SwitchLocation, location, Today);
        Assert.True(NotificationThrottle.WasHotKeyReported(reported, HotKeyAction.SwitchLocation, location));
        Assert.False(NotificationThrottle.WasHotKeyReported(reported, HotKeyAction.SwitchLocation, other));
        Assert.False(NotificationThrottle.WasHotKeyReported(reported, HotKeyAction.ToggleLights, location));

        OnboardingState changed = NotificationThrottle.RecordHotKeyReported(reported, HotKeyAction.SwitchLocation, other, Today.AddDays(1));
        Assert.False(NotificationThrottle.WasHotKeyReported(changed, HotKeyAction.SwitchLocation, location));
        Assert.True(NotificationThrottle.WasHotKeyReported(changed, HotKeyAction.SwitchLocation, other));
        Assert.Equal(Today, changed.LastNotified["ThemeChanged"]);

        OnboardingState forgotten = NotificationThrottle.ForgetHotKeyReported(changed, HotKeyAction.SwitchLocation);
        Assert.False(NotificationThrottle.WasHotKeyReported(forgotten, HotKeyAction.SwitchLocation, other));
        Assert.Same(forgotten, NotificationThrottle.ForgetHotKeyReported(forgotten, HotKeyAction.SwitchLocation));
    }

    [Fact]
    public void FirstCloseIsShownOnceEver()
    {
        OnboardingState shown = NotificationThrottle.Record(NotificationKind.FirstClose, new OnboardingState(), Today);
        Assert.True(shown.CloseNotified);
        Assert.False(NotificationThrottle.IsAllowed(NotificationKind.FirstClose, shown, new HashSet<NotificationKind>(), Today.AddYears(1)));
    }

    [Fact]
    public void SessionKindsPersistNothing()
    {
        var onboarding = new OnboardingState();
        Assert.Same(onboarding, NotificationThrottle.Record(NotificationKind.MusicWaiting, onboarding, Today));
        Assert.False(NotificationThrottle.IsAllowed(NotificationKind.MusicWaiting, onboarding, new HashSet<NotificationKind> { NotificationKind.MusicWaiting }, Today));
    }

    [Fact]
    public void TheServiceShowsOncePerDayAndRecordsIt()
    {
        NotificationService service = Service();
        Assert.True(service.Show(NotificationKind.SettingsReset, "Settings were reset", "Your settings file couldn't be read."));
        Assert.False(service.Show(NotificationKind.SettingsReset, "Settings were reset", "Again."));
        Assert.Single(balloons);
        Assert.Equal(Today, settings.Current.Onboarding.LastNotified["SettingsReset"]);
        Assert.Equal(SettingsChangeKind.Internal, Assert.Single(settings.History).Kind);
    }

    [Fact]
    public void NothingIsShownDuringAFullScreenAppOrAFocusSession()
    {
        NotificationService service = Service();
        pause = PauseState.None with { FullScreenActive = true };
        Assert.False(service.Show(NotificationKind.LightsRestarted, "Lights restarted", "Something went wrong."));
        pause = PauseState.None with { FocusSessionActive = true };
        Assert.False(service.Show(NotificationKind.LightsRestarted, "Lights restarted", "Something went wrong."));
        Assert.Empty(balloons);

        pause = PauseState.None;
        Assert.True(service.Show(NotificationKind.LightsRestarted, "Lights restarted", "Something went wrong."));
    }

    [Fact]
    public void WithoutATrayIconNothingIsRecorded()
    {
        NotificationService service = Service(trayShown: false);
        Assert.False(service.Show(NotificationKind.ThemeChanged, "Happy Halloween!", "Your lights switched."));
        Assert.Empty(settings.History);
    }

    [Theory]
    [InlineData(NotificationKind.FirstClose, SettingsPageId.Home)]
    [InlineData(NotificationKind.HotKeyUnavailable, SettingsPageId.General)]
    [InlineData(NotificationKind.SettingsReset, SettingsPageId.General)]
    [InlineData(NotificationKind.MusicWaiting, SettingsPageId.MusicBox)]
    public void ClickingOpensThePageOfTheKind(NotificationKind kind, SettingsPageId page)
    {
        NotificationService service = Service();
        Assert.True(service.Show(kind, "Title", "Text"));
        service.OnClicked();
        Assert.Equal(page, Assert.Single(window.Shown).Page);
    }

    [Fact]
    public void ClickingLightsRestartedOpensNothing()
    {
        NotificationService service = Service();
        service.Show(NotificationKind.LightsRestarted, "Lights restarted", "Text");
        service.OnClicked();
        Assert.Empty(window.Shown);
    }

    [Fact]
    public void ClickingAThemeSwitchOpensThemesWithItsUndo()
    {
        NotificationService service = Service();
        Assert.True(service.ShowThemeSwitched("Happy Halloween!", "Your lights switched to the Halloween theme.", "Halloween", Today));
        service.OnClicked();
        (SettingsPageId? page, SettingsRequest? request) = Assert.Single(window.Shown);
        Assert.Equal(SettingsPageId.Themes, page);
        Assert.Equal(new ThemeSwitchedRequest("Halloween", Today), request);
        Assert.Equal(("Happy Halloween!", "Your lights switched to the Halloween theme."), balloons.Single());
    }
}
