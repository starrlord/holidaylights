using HolidayLights.App.Shell;

namespace HolidayLights.Tests.App;

/// <summary>What a later launch asks the running instance to do (PRODUCT-SPEC 6.6.2, 2.5.4, 2.3 "Which page opens").</summary>
public sealed class LaunchCommandsTests
{
    private static InstanceCommand? For(LaunchKind kind, string? argument = null, SettingsPageId? page = null, IReadOnlyList<string>? files = null) =>
        LaunchCommands.ForRunningInstance(new LaunchRequest { Kind = kind, Argument = argument, Page = page, Files = files ?? [] });

    [Fact]
    public void ASecondPlainLaunchOpensSettingsOnTheLastPage()
    {
        InstanceCommand command = For(LaunchKind.Normal)!;
        Assert.Equal(InstanceCommandKind.ShowSettings, command.Kind);
        Assert.Empty(command.Arguments);
        Assert.True(LaunchCommands.ShowsWindow(command));
    }

    [Fact]
    public void AnAutostartWhileRunningAsksNothing() => Assert.Null(For(LaunchKind.Autostart));

    [Fact]
    public void SettingsPagesTravelByName()
    {
        Assert.Equal(["themes"], For(LaunchKind.Settings, page: SettingsPageId.Themes)!.Arguments);
        Assert.Equal(["saver"], For(LaunchKind.LegacySettings)!.Arguments);
        Assert.Equal(["saver"], For(LaunchKind.ScreenSaverConfigure)!.Arguments);
    }

    [Fact]
    public void FilesAreForwarded()
    {
        InstanceCommand command = For(LaunchKind.Open, files: [@"C:\a.bul", @"C:\b.gif"])!;
        Assert.Equal(InstanceCommandKind.Open, command.Kind);
        Assert.Equal([@"C:\a.bul", @"C:\b.gif"], command.Arguments);
        Assert.True(LaunchCommands.ShowsWindow(command));
    }

    [Theory]
    [InlineData("on", InstanceCommandKind.Lights, "on")]
    [InlineData("off", InstanceCommandKind.Lights, "off")]
    [InlineData("toggle", InstanceCommandKind.ToggleLights, null)]
    public void LightsCommands(string state, InstanceCommandKind kind, string? argument)
    {
        InstanceCommand command = For(LaunchKind.Lights, state)!;
        Assert.Equal(kind, command.Kind);
        Assert.Equal(argument is null ? [] : [argument], command.Arguments);
        Assert.False(LaunchCommands.ShowsWindow(command));
    }

    [Theory]
    [InlineData(LaunchKind.ToggleLayer, InstanceCommandKind.ToggleLayer)]
    [InlineData(LaunchKind.Exit, InstanceCommandKind.Exit)]
    [InlineData(LaunchKind.Reset, InstanceCommandKind.Reset)]
    public void SimpleCommands(LaunchKind kind, InstanceCommandKind expected) => Assert.Equal(expected, For(kind)!.Kind);

    [Fact]
    public void ThemesTravelByName() => Assert.Equal(["Christmas 1"], For(LaunchKind.Theme, "Christmas 1")!.Arguments);

    [Theory]
    [InlineData(LaunchKind.ScreenSaverShow)]
    [InlineData(LaunchKind.ScreenSaverPreview)]
    [InlineData(LaunchKind.RenderTest)]
    [InlineData(LaunchKind.Install)]
    [InlineData(LaunchKind.Diagnostics)]
    public void SessionsOfTheirOwnAreNeverForwarded(LaunchKind kind) =>
        Assert.Throws<ArgumentException>(() => For(kind));

    [Fact]
    public void EveryForwardedCommandSurvivesThePipe()
    {
        foreach (LaunchKind kind in new[] { LaunchKind.Normal, LaunchKind.Settings, LaunchKind.Open, LaunchKind.ToggleLayer, LaunchKind.Lights, LaunchKind.Theme, LaunchKind.Exit, LaunchKind.Reset })
        {
            InstanceCommand command = For(kind, kind == LaunchKind.Theme ? "Halloween" : "on", SettingsPageId.Home, [@"C:\x.bul"])!;
            string line = new InstanceMessage { Command = command }.ToLine();
            Assert.True(InstanceMessage.TryParseLine(line, out InstanceMessage? parsed));
            Assert.Equal(command.Kind, parsed.Command!.Kind);
            Assert.Equal(command.Arguments, parsed.Command.Arguments);
        }
    }
}
