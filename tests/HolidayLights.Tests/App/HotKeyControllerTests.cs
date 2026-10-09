using HolidayLights.App.Shell;
using HolidayLights.Tests.App.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.App;

/// <summary>The hot keys (PRODUCT-SPEC 6.6.4, 3.12, D16; acceptance 7.5 #17) and the commands of later launches (6.6.2, 6.6.3).</summary>
public sealed class HotKeyControllerTests
{
    private readonly InMemorySettingsStore settings = new();
    private readonly FakeHotKeyService hotKeys = new();
    private readonly FakeNotifications notifications = new();
    private readonly FakeSettingsWindow window = new();
    private readonly FakeAppShell shell = new();
    private readonly List<PillRequest> pills = [];

    private FakeServices Services(AppSessionKind session = AppSessionKind.Normal) => new()
    {
        Settings = settings,
        Notifications = notifications,
        SettingsWindow = window,
        AppShell = shell,
        Options = new AppRuntimeOptions { Session = session },
    };

    private HotKeyController Controller(AppSessionKind session = AppSessionKind.Normal) => new(Services(session), hotKeys, pills.Add);

    [Fact]
    public void OnlyTheLocationHotKeyIsRegisteredByDefault()
    {
        using HotKeyController controller = Controller();
        controller.Start();
        Assert.Equal(new HotKeyBinding { Enabled = true, Key = "B" }, hotKeys.Registered[HotKeyAction.SwitchLocation]);
        Assert.False(hotKeys.Registered.ContainsKey(HotKeyAction.ToggleLights));
        Assert.Equal(HotKeyRegistration.Registered, controller.Status.Single(s => s.Action == HotKeyAction.SwitchLocation).Registration);
        Assert.Null(controller.Status.Single(s => s.Action == HotKeyAction.ToggleLights).Registration);
    }

    [Fact]
    public void NothingIsRegisteredOutsideTheNormalApp()
    {
        using HotKeyController controller = Controller(AppSessionKind.SettingsOnly);
        controller.Start();
        Assert.Empty(hotKeys.Registered);
    }

    [Fact]
    public void ChangingTheSettingsRegistersAgain()
    {
        using HotKeyController controller = Controller();
        controller.Start();
        int changes = 0;
        controller.StatusChanged += (_, _) => changes++;
        settings.Update(s => s with { HotKeys = s.HotKeys with { Lights = s.HotKeys.Lights with { Enabled = true } } }, SettingsChange.Edit("Turn on the lights hot key"));
        Assert.True(hotKeys.Registered.ContainsKey(HotKeyAction.ToggleLights));
        settings.Update(s => s with { HotKeys = s.HotKeys with { Location = s.HotKeys.Location with { Enabled = false } } }, SettingsChange.Edit("Turn off the location hot key"));
        Assert.False(hotKeys.Registered.ContainsKey(HotKeyAction.SwitchLocation));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void AKeyInUseIsReportedOncePerChange()
    {
        hotKeys.Outcomes[HotKeyAction.SwitchLocation] = HotKeyRegistration.InUse;
        using HotKeyController controller = Controller();
        controller.Start();
        Assert.Equal(HotKeyRegistration.InUse, controller.Status[0].Registration);
        (NotificationKind kind, string title, string text) = Assert.Single(notifications.Shown);
        Assert.Equal(NotificationKind.HotKeyUnavailable, kind);
        Assert.Equal("Hot key not available", title);
        Assert.Equal("Ctrl+Alt+Shift+B is used by another program. Choose another one in General.", text);

        hotKeys.ChangeLayouts();
        Assert.Single(notifications.Shown);

        settings.Update(s => s with { HotKeys = s.HotKeys with { Location = s.HotKeys.Location with { Key = "H" } } }, SettingsChange.Edit("Change the hot key"));
        Assert.Equal(2, notifications.Shown.Count);
        Assert.StartsWith("Ctrl+Alt+Shift+H", notifications.Shown[1].Text);

        // A second combination in use the same day is told too (the lights hot key).
        hotKeys.Outcomes[HotKeyAction.ToggleLights] = HotKeyRegistration.InUse;
        settings.Update(s => s with { HotKeys = s.HotKeys with { Lights = s.HotKeys.Lights with { Enabled = true } } }, SettingsChange.Edit("Turn on the lights hot key"));
        Assert.Equal(3, notifications.Shown.Count);
        Assert.StartsWith("Ctrl+Alt+Shift+L", notifications.Shown[2].Text);
    }

    [Fact]
    public void AKeyInUseIsNotReportedAgainAtTheNextStartOrTheNextDay()
    {
        hotKeys.Outcomes[HotKeyAction.SwitchLocation] = HotKeyRegistration.InUse;
        using (HotKeyController first = Controller())
        {
            first.Start();
        }

        Assert.Single(notifications.Shown);

        // The next start (a new controller, the same settings file), on any later day: nothing changed, nothing to tell.
        using (HotKeyController next = Controller())
        {
            next.Start();
        }

        Assert.Single(notifications.Shown);

        // The other program let go of the combination, then took it again: that is a change.
        hotKeys.Outcomes[HotKeyAction.SwitchLocation] = HotKeyRegistration.Registered;
        using (HotKeyController freed = Controller())
        {
            freed.Start();
        }

        hotKeys.Outcomes[HotKeyAction.SwitchLocation] = HotKeyRegistration.InUse;
        using (HotKeyController again = Controller())
        {
            again.Start();
        }

        Assert.Equal(2, notifications.Shown.Count);
    }

    [Fact]
    public void TheLocationHotKeySwitchesWithAPillAndAHintThreeTimes()
    {
        using HotKeyController controller = Controller();
        controller.Start();
        for (int i = 0; i < 4; i++)
        {
            hotKeys.Press(HotKeyAction.SwitchLocation);
        }

        Assert.Equal(BulbDrawing.Desktop, settings.Current.Lights.Drawing);
        Assert.Equal(
            [PillGlyph.OnTop, PillGlyph.OnDesktop, PillGlyph.OnTop, PillGlyph.OnDesktop],
            pills.Select(p => p.Glyph));
        Assert.Equal("Bulbs on top of all windows", pills[0].Text);
        Assert.Equal("Bulbs on the desktop", pills[1].Text);
        Assert.Equal("Press Ctrl+Alt+Shift+B again to put them back.", pills[0].SecondLine);
        Assert.Equal(TimeSpan.FromSeconds(4), pills[0].SecondLineDuration);
        Assert.NotNull(pills[2].SecondLine);
        Assert.Null(pills[3].SecondLine);
        Assert.Equal(3, settings.Current.Onboarding.LocationHotKeyHints);
        Assert.Contains(settings.History, c => c.Kind == SettingsChangeKind.Edit && c.Description == "Draw the bulbs on top of all windows");
    }

    [Fact]
    public void TheLightsHotKeyTogglesShowLights()
    {
        settings.Update(s => s with { HotKeys = s.HotKeys with { Lights = s.HotKeys.Lights with { Enabled = true } } }, SettingsChange.Internal);
        using HotKeyController controller = Controller();
        controller.Start();
        hotKeys.Press(HotKeyAction.ToggleLights);
        Assert.False(settings.Current.Lights.On);
        Assert.Equal(new PillRequest(PillGlyph.LightsOff, "Lights off"), pills[^1]);
        hotKeys.Press(HotKeyAction.ToggleLights);
        Assert.True(settings.Current.Lights.On);
        Assert.Equal(new PillRequest(PillGlyph.LightsOn, "Lights on"), pills[^1]);
    }

    [Fact]
    public void DisposingUnregistersEverything()
    {
        HotKeyController controller = Controller();
        controller.Start();
        controller.Dispose();
        Assert.Empty(hotKeys.Registered);
        Assert.True(hotKeys.Disposed);
    }

    [Fact]
    public void LaterLaunchesOpenSettingsPagesAndFiles()
    {
        using HotKeyController controller = Controller();
        var router = new InstanceCommandRouter(Services(), controller, TimeProvider.System);

        Assert.True(router.Execute(InstanceCommand.ShowSettings()).Accepted);
        Assert.True(router.Execute(InstanceCommand.ShowSettings(SettingsPageId.ScreenSaver)).Accepted);
        Assert.True(router.Execute(InstanceCommand.Open([@"C:\Star.bul"])).Accepted);
        Assert.True(router.Execute(InstanceCommand.Simple(InstanceCommandKind.Reset)).Accepted);

        Assert.Null(window.Shown[0].Page);
        Assert.Null(window.Shown[0].Request);
        Assert.Equal(SettingsPageId.ScreenSaver, window.Shown[1].Page);
        Assert.Equal(SettingsPageId.BulbFactory, window.Shown[2].Page);
        Assert.Equal([@"C:\Star.bul"], Assert.IsType<OpenFilesRequest>(window.Shown[2].Request).Paths);
        Assert.Equal(SettingsPageId.General, window.Shown[3].Page);
        Assert.IsType<ResetRequest>(window.Shown[3].Request);
    }

    [Fact]
    public void LaterLaunchesSwitchTheLights()
    {
        using HotKeyController controller = Controller();
        var router = new InstanceCommandRouter(Services(), controller, TimeProvider.System);
        Assert.True(router.Execute(InstanceCommand.Lights(false)).Accepted);
        Assert.False(settings.Current.Lights.On);
        Assert.True(router.Execute(InstanceCommand.Simple(InstanceCommandKind.ToggleLights)).Accepted);
        Assert.True(settings.Current.Lights.On);
        Assert.True(router.Execute(InstanceCommand.Simple(InstanceCommandKind.ToggleLayer)).Accepted);
        Assert.Equal(BulbDrawing.OnTop, settings.Current.Lights.Drawing);
        Assert.Equal(3, pills.Count);
    }

    [Fact]
    public async Task ExitAnswersFirstThenExits()
    {
        using HotKeyController controller = Controller();
        var router = new InstanceCommandRouter(Services(), controller, TimeProvider.System);
        CommandOutcome outcome = router.Execute(InstanceCommand.Simple(InstanceCommandKind.Exit));
        Assert.True(outcome.Accepted);
        Assert.Equal(0, shell.Exits);
        await outcome.AfterReply!();
        Assert.Equal(1, shell.Exits);
    }

    [Fact]
    public void IncompleteOrForeignCommandsAreRefused()
    {
        using HotKeyController controller = Controller();
        var router = new InstanceCommandRouter(Services(), controller, TimeProvider.System);
        Assert.False(router.Execute(new InstanceCommand { Kind = InstanceCommandKind.Open }).Accepted);
        Assert.False(router.Execute(new InstanceCommand { Kind = InstanceCommandKind.Lights, Arguments = ["dim"] }).Accepted);
        Assert.False(router.Execute(InstanceCommand.Simple(InstanceCommandKind.SaverStarted)).Accepted);
        Assert.Empty(window.Shown);
    }
}
