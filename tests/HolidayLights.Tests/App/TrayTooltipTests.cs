using HolidayLights.App.Tray;

namespace HolidayLights.Tests.App;

/// <summary>The tray tooltip of PRODUCT-SPEC 2.2 (up to 3 lines within the notification area's 127 characters).</summary>
public sealed class TrayTooltipTests
{
    private static LightsScene Scene(LayerMode requested = LayerMode.BehindIcons, EnergySaverChoice? saver = null) => LightsScene.Empty with
    {
        LightsOn = true,
        RequestedLayer = requested,
        EnergySaverInEffect = saver,
    };

    private static LightsStatus Status(params LayerMode?[] effective) => new()
    {
        Health = LightsHealth.Running,
        Displays = [.. effective.Select((mode, i) => new DisplayLayerStatus($"display-{i + 1}", mode, false))],
    };

    private static string Build(bool on = true, string theme = "Automatic: Halloween", LightsScene? scene = null, LightsStatus? status = null,
        LightsPauseState? pause = null, string? song = null) =>
        TrayTooltip.Build(new TrayTooltipState(on, theme, scene ?? Scene(), status ?? Status(LayerMode.BehindIcons), pause ?? LightsPauseState.None, song));

    [Fact]
    public void LightsOnShowTheThemeAndWhereTheyAre() =>
        Assert.Equal("Holiday Lights\nAutomatic: Halloween - behind your icons", Build());

    [Fact]
    public void LightsOffSaySo() => Assert.Equal("Holiday Lights\nLights off", Build(on: false, song: "Jingle Bells"));

    [Fact]
    public void APlayingSongAddsAThirdLine() =>
        Assert.Equal("Holiday Lights\nChristmas 1 - on top of all windows\nPlaying: Jingle Bells",
            Build(theme: "Christmas 1", scene: Scene(LayerMode.OnTop), status: Status(LayerMode.OnTop), song: "Jingle Bells"));

    [Fact]
    public void AFallbackIsTold() =>
        Assert.EndsWith("Custom Settings - in front of your icons", Build(theme: "Custom Settings", status: Status(LayerMode.InFrontOfIcons, LayerMode.BehindIcons)));

    [Fact]
    public void RestingIsTold()
    {
        var fullScreen = new LightsPauseState(PauseReasons.None, new HashSet<string> { "display-2" });
        Assert.EndsWith("resting while a full-screen app is open", Build(pause: fullScreen));
        Assert.EndsWith("resting while a full-screen app is open", Build(pause: new LightsPauseState(PauseReasons.ExclusiveFullScreen, new HashSet<string>())));
        Assert.EndsWith("resting during a presentation", Build(pause: new LightsPauseState(PauseReasons.Presentation, new HashSet<string>())));
        Assert.EndsWith("resting while Energy Saver is on", Build(scene: Scene(saver: EnergySaverChoice.TurnOffLights)));
    }

    [Fact]
    public void ABusySynthesizerIsToldOnLineThree()
    {
        TrayTooltipState state(bool on) => new(on, "Christmas 1", Scene(), Status(LayerMode.BehindIcons), LightsPauseState.None, null) { MusicWaiting = true };
        Assert.Equal("Holiday Lights\nChristmas 1 - behind your icons\nMusic is waiting for the synthesizer", TrayTooltip.Build(state(true)));
        Assert.Equal("Holiday Lights\nLights off\nMusic is waiting for the synthesizer", TrayTooltip.Build(state(false)));

        string longTheme = TrayTooltip.Build(new TrayTooltipState(true, new string('T', 120), Scene(), Status(LayerMode.BehindIcons), LightsPauseState.None, null) { MusicWaiting = true });
        Assert.True(longTheme.Length <= TrayTooltip.MaxLength, $"{longTheme.Length} characters");
    }

    [Fact]
    public void LongNamesAreShortenedToFitTheNotificationArea()
    {
        string text = Build(theme: new string('T', 63), song: new string('S', 200));
        Assert.True(text.Length <= TrayTooltip.MaxLength, $"{text.Length} characters");
        Assert.StartsWith("Holiday Lights\nTTTT", text);
        Assert.Contains(" - behind your icons", text);
        Assert.Contains("\nPlaying: SSS", text);
        Assert.EndsWith("…", text);
    }
}
