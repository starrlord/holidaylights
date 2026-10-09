using HolidayLights.App.Shell;

namespace HolidayLights.Tests.App;

/// <summary>The command line of PRODUCT-SPEC 6.6.3 and 6.2.1, the 5.4 forms and PO-3's test options.</summary>
public sealed class CommandLineTests
{
    private const string Exe = @"C:\Users\Test\AppData\Local\Programs\HolidayLights\HolidayLights.exe";
    private const string Scr = @"C:\Users\Test\AppData\Local\Programs\HolidayLights\Holiday Lights.scr";

    private static LaunchRequest Parse(params string[] args) => CommandLine.Parse(args, Exe);

    [Fact]
    public void NoArgumentsIsANormalStart()
    {
        LaunchRequest request = Parse();
        Assert.Equal(LaunchKind.Normal, request.Kind);
        Assert.Empty(request.UnknownArguments);
        Assert.False(request.NoSystemChanges);
        Assert.Null(request.DataRoot);
    }

    [Fact]
    public void TheScreenSaverCopyWithoutArgumentsIsWindowsSettingsButton() =>
        Assert.Equal(LaunchKind.ScreenSaverConfigure, CommandLine.Parse([], Scr).Kind);

    [Fact]
    public void TheSetupCopyWithoutArgumentsIsTheInstaller() =>
        Assert.Equal(LaunchKind.Install, CommandLine.Parse([], @"D:\dist\Holiday Lights 6.0.0\Setup.exe").Kind);

    [Theory]
    [InlineData("/s")]
    [InlineData("/S")]
    [InlineData("-s")]
    [InlineData("-S")]
    public void ScreenSaverShow(string argument)
    {
        LaunchRequest request = CommandLine.Parse([argument], Scr);
        Assert.Equal(LaunchKind.ScreenSaverShow, request.Kind);
        Assert.True(request.IsScreenSaverMode);
    }

    [Theory]
    [InlineData(new[] { "/p", "123456" }, 123456)]
    [InlineData(new[] { "/P", "42" }, 42)]
    [InlineData(new[] { "/p:777" }, 777)]
    [InlineData(new[] { "-p", "9" }, 9)]
    public void ScreenSaverPreviewTakesTheWindow(string[] args, long handle)
    {
        LaunchRequest request = CommandLine.Parse(args, Scr);
        Assert.Equal(LaunchKind.ScreenSaverPreview, request.Kind);
        Assert.Equal((nint)handle, request.WindowHandle);
        Assert.Empty(request.UnknownArguments);
    }

    [Theory]
    [InlineData(new[] { "/c" }, 0)]
    [InlineData(new[] { "/c:1234" }, 1234)]
    [InlineData(new[] { "/C", "55" }, 55)]
    public void ScreenSaverConfigure(string[] args, long handle)
    {
        LaunchRequest request = CommandLine.Parse(args, Scr);
        Assert.Equal(LaunchKind.ScreenSaverConfigure, request.Kind);
        Assert.Equal((nint)handle, request.WindowHandle);
    }

    [Fact]
    public void ThePasswordSwitchIsRecognizedToBeIgnored()
    {
        Assert.Equal(LaunchKind.ScreenSaverPassword, CommandLine.Parse(["/a", "1234"], Scr).Kind);
        Assert.Equal(LaunchKind.ScreenSaverPassword, CommandLine.Parse(["/a"], Scr).Kind);
    }

    [Fact]
    public void AutostartIsSilent() => Assert.Equal(LaunchKind.Autostart, Parse("--autostart").Kind);

    [Theory]
    [InlineData("home", SettingsPageId.Home)]
    [InlineData("bulbs", SettingsPageId.BulbFactory)]
    [InlineData("MUSIC", SettingsPageId.MusicBox)]
    [InlineData("saver", SettingsPageId.ScreenSaver)]
    [InlineData("themes", SettingsPageId.Themes)]
    [InlineData("general", SettingsPageId.General)]
    public void SettingsOnAPage(string page, SettingsPageId expected)
    {
        LaunchRequest request = Parse("--settings", page);
        Assert.Equal(LaunchKind.Settings, request.Kind);
        Assert.Equal(expected, request.Page);
    }

    [Fact]
    public void SettingsWithoutAPageOpensTheLastPage()
    {
        LaunchRequest request = Parse("--settings");
        Assert.Equal(LaunchKind.Settings, request.Kind);
        Assert.Null(request.Page);
    }

    [Fact]
    public void SettingsWithAnUnknownPageOpensTheLastPageAndReportsIt()
    {
        LaunchRequest request = Parse("--settings", "nowhere");
        Assert.Equal(LaunchKind.Settings, request.Kind);
        Assert.Null(request.Page);
        Assert.Equal(["nowhere"], request.UnknownArguments);
    }

    [Fact]
    public void LegacySettingsIsTheScreenSaverPage() => Assert.Equal(LaunchKind.LegacySettings, Parse("settings").Kind);

    [Fact]
    public void ModernOpenTakesEveryFile()
    {
        LaunchRequest request = Parse("--open", @"C:\Bulbs\Star.bul", @"C:\Bulbs\Moon.gif");
        Assert.Equal(LaunchKind.Open, request.Kind);
        Assert.Equal([@"C:\Bulbs\Star.bul", @"C:\Bulbs\Moon.gif"], request.Files);
    }

    [Fact]
    public void LegacyOpenTakesEverythingAfterOpenAsOnePath()
    {
        // 5.4 wrote the association as "<exe>" open %1 with an unquoted %1: the path arrives split at its spaces.
        LaunchRequest request = Parse("open", @"C:\My", "Bulbs\\Snow", "Man.bul");
        Assert.Equal(LaunchKind.Open, request.Kind);
        Assert.Equal([@"C:\My Bulbs\Snow Man.bul"], request.Files);
    }

    [Fact]
    public void LegacyOpenAcceptsAQuotedPath() =>
        Assert.Equal([@"C:\My Bulbs\Star.bul"], Parse("open", "\"C:\\My Bulbs\\Star.bul\"").Files);

    [Fact]
    public void FilesDroppedOnTheProgramAreOpened()
    {
        LaunchRequest request = Parse(@"C:\Downloads\Star.bul", @"C:\Downloads\Tree.gif");
        Assert.Equal(LaunchKind.Open, request.Kind);
        Assert.Equal(2, request.Files.Count);
    }

    [Fact]
    public void RelativeFilesBecomeFullPaths()
    {
        LaunchRequest request = Parse("--open", "Star.bul");
        Assert.True(Path.IsPathFullyQualified(request.Files[0]));
        Assert.Equal("Star.bul", Path.GetFileName(request.Files[0]));
    }

    [Theory]
    [InlineData(new[] { "--lights", "on" }, "on")]
    [InlineData(new[] { "--lights", "OFF" }, "off")]
    [InlineData(new[] { "--lights", "toggle" }, "toggle")]
    [InlineData(new[] { "--lights" }, "toggle")]
    public void Lights(string[] args, string state)
    {
        LaunchRequest request = Parse(args);
        Assert.Equal(LaunchKind.Lights, request.Kind);
        Assert.Equal(state, request.Argument);
    }

    [Fact]
    public void LightsWithAWrongStateIsIgnored()
    {
        LaunchRequest request = Parse("--lights", "maybe");
        Assert.Equal(LaunchKind.Normal, request.Kind);
        Assert.Equal(["--lights", "maybe"], request.UnknownArguments.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ThemeNamesMayBeQuotedOrSplit()
    {
        Assert.Equal("Christmas 1", Parse("--theme", "Christmas 1").Argument);
        Assert.Equal("Christmas 1", Parse("--theme", "Christmas", "1").Argument);
        Assert.Equal(LaunchKind.Theme, Parse("--theme", "Halloween").Kind);
        Assert.Equal(LaunchKind.Normal, Parse("--theme").Kind);
    }

    [Theory]
    [InlineData("--toggle-layer", LaunchKind.ToggleLayer)]
    [InlineData("--exit", LaunchKind.Exit)]
    [InlineData("--reset", LaunchKind.Reset)]
    [InlineData("reset", LaunchKind.Reset)]
    [InlineData("RESET", LaunchKind.Reset)]
    [InlineData("--install", LaunchKind.Install)]
    [InlineData("--uninstall", LaunchKind.Uninstall)]
    [InlineData("--EXIT", LaunchKind.Exit)]
    public void SimpleCommands(string argument, LaunchKind kind) => Assert.Equal(kind, Parse(argument).Kind);

    [Fact]
    public void QuietInstallIsForTheSetup()
    {
        LaunchRequest request = Parse("--install", "--quiet", "--data-root", @"C:\Tempoot", "--no-system-changes");
        Assert.Equal(LaunchKind.Install, request.Kind);
        Assert.True(request.Quiet);
        Assert.True(request.NoSystemChanges);
        Assert.Equal(@"C:\Tempoot", request.DataRoot);
        Assert.Empty(request.UnknownArguments);
        Assert.True(Parse("--QUIET", "--install").Quiet);
        Assert.False(Parse("--install").Quiet);
    }

    [Fact]
    public void RenderTestTakesAFolder()
    {
        LaunchRequest request = Parse("--render-test", @"C:\Temp\render");
        Assert.Equal(LaunchKind.RenderTest, request.Kind);
        Assert.Equal(@"C:\Temp\render", request.Argument);
    }

    [Fact]
    public void RenderTestWithoutAFolderUsesTheCurrentFolder() =>
        Assert.Equal(Path.GetFullPath("."), Parse("--render-test").Argument);

    [Fact]
    public void DiagnosticsTakeAnOptionalFile()
    {
        Assert.Null(Parse("--diagnostics").Argument);
        LaunchRequest request = Parse("--diagnostics", @"C:\Temp\report.txt");
        Assert.Equal(LaunchKind.Diagnostics, request.Kind);
        Assert.Equal(@"C:\Temp\report.txt", request.Argument);
    }

    [Fact]
    public void TestOptionsWorkAnywhereAndWithEveryCommand()
    {
        LaunchRequest request = Parse("--no-system-changes", "--settings", "themes", "--data-root", @"C:\Temp\root");
        Assert.Equal(LaunchKind.Settings, request.Kind);
        Assert.Equal(SettingsPageId.Themes, request.Page);
        Assert.True(request.NoSystemChanges);
        Assert.Equal(@"C:\Temp\root", request.DataRoot);
        Assert.Empty(request.UnknownArguments);

        LaunchRequest saver = CommandLine.Parse(["--data-root", @"C:\Temp\root", "/s"], Scr);
        Assert.Equal(LaunchKind.ScreenSaverShow, saver.Kind);
        Assert.Equal(@"C:\Temp\root", saver.DataRoot);
    }

    [Fact]
    public void ADataRootWithoutAFolderIsReported()
    {
        LaunchRequest request = Parse("--data-root");
        Assert.Null(request.DataRoot);
        Assert.Equal(["--data-root"], request.UnknownArguments);
    }

    [Fact]
    public void UnknownArgumentsGiveANormalStartAndAreReported()
    {
        LaunchRequest request = Parse("--sparkle", "now");
        Assert.Equal(LaunchKind.Normal, request.Kind);
        Assert.Contains("--sparkle", request.UnknownArguments);

        LaunchRequest words = Parse("please", "start");
        Assert.Equal(LaunchKind.Normal, words.Kind);
        Assert.Equal(["please", "start"], words.UnknownArguments);
    }

    [Fact]
    public void ExtraArgumentsAfterACommandAreReported()
    {
        LaunchRequest request = Parse("--exit", "--autostart");
        Assert.Equal(LaunchKind.Exit, request.Kind);
        Assert.Equal(["--autostart"], request.UnknownArguments);
    }
}
