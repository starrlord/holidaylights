using HolidayLights.App.Install;
using HolidayLights.App.Shell;

namespace HolidayLights.Tests.App;

/// <summary>Which version is newer, and how the setup words installing over an installation.</summary>
public sealed class InstallKindTests
{
    [Theory]
    [InlineData("6.0.0", "6.0.1", -1)]
    [InlineData("6.0.1", "6.0.1", 0)]
    [InlineData("6.1.0", "6.0.9", 1)]
    [InlineData("6.10.0", "6.9.0", 1)]
    [InlineData("6.0", "6.0.0", 0)]
    [InlineData("6.0.1+3f2a9c1", "6.0.1", 0)]
    [InlineData("6.1.0-beta.1", "6.1.0", -1)]
    [InlineData("6.1.0-beta.2", "6.1.0-beta.10", -1)]
    [InlineData("6.1.0-alpha", "6.1.0-beta", -1)]
    [InlineData("6.1.0-beta", "6.1.0-beta.1", -1)]
    [InlineData("6.1.0-1", "6.1.0-alpha", -1)]
    [InlineData("6.1.0-beta.1", "6.0.9", 1)]
    [InlineData("unknown", "6.0.0", -1)]
    public void VersionsAreOrderedAsSemanticVersions(string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(InstallKinds.Compare(left, right)));
        Assert.Equal(-expected, Math.Sign(InstallKinds.Compare(right, left)));
    }

    [Theory]
    [InlineData(null, "6.0.2", InstallKind.New)]
    [InlineData("6.0.0", "6.0.2", InstallKind.Update)]
    [InlineData("6.0.2", "6.0.2", InstallKind.Reinstall)]
    [InlineData("6.1.0", "6.0.2", InstallKind.Downgrade)]
    [InlineData("", "6.0.2", InstallKind.Update)]
    public void TheKindComesFromTheInstalledVersion(string? installed, string setup, InstallKind expected) =>
        Assert.Equal(expected, InstallKinds.For(installed, setup));

    [Fact]
    public void TheSetupWindowSaysWhatInstallingDoes()
    {
        string version = VersionInfo.ProgramVersion;
        Assert.Equal(("Install Holiday Lights", "_Install"), Pick(SetupWindow.InstallTexts(InstallKind.New, null, @"C:\Programs\HolidayLights")));
        Assert.Contains(@"C:\Programs\HolidayLights", SetupWindow.InstallTexts(InstallKind.New, null, @"C:\Programs\HolidayLights").Intro);

        var update = SetupWindow.InstallTexts(InstallKind.Update, "6.0.0", "");
        Assert.Equal(("Update Holiday Lights", "_Update"), Pick(update));
        Assert.Contains($"Holiday Lights 6.0.0 is installed. Setup updates it to version {version}.", update.Intro);
        Assert.Contains("settings, themes, bulbs, songs and pictures are kept", update.Intro);

        Assert.Equal(("Reinstall Holiday Lights", "_Reinstall"), Pick(SetupWindow.InstallTexts(InstallKind.Reinstall, version, "")));

        var older = SetupWindow.InstallTexts(InstallKind.Downgrade, "9.0.0", "");
        Assert.Equal(("Install an Older Version?", "_Install Older Version"), Pick(older));
        Assert.Contains($"Holiday Lights 9.0.0 is installed, which is newer than this setup ({version}).", older.Intro);
    }

    private static (string Heading, string Button) Pick((string Heading, string Intro, string Button) texts) => (texts.Heading, texts.Button);
}
