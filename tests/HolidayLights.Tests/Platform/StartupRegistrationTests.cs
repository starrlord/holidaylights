using HolidayLights.Platform.Integration;
using HolidayLights.Tests.Shared;
using Microsoft.Win32;

namespace HolidayLights.Tests.Platform;

/// <summary>Runs against <see cref="TestRegistryRoot"/>, never the real Run key.</summary>
public sealed class StartupRegistrationTests
{
    private static readonly DataPaths Paths = DataPaths.ForDataRoot(@"C:\HolidayLightsTestData", @"C:\Users\Test\AppData\Local\Programs\HolidayLights");

    [Fact]
    public void Enable_WritesTheQuotedProgramWithAutostart()
    {
        using var root = new TestRegistryRoot();
        var startup = new StartupRegistration(Paths, new AppRuntimeOptions(), new RecordingLog(), root.Registry);
        Assert.False(startup.IsEnabled);

        startup.Enable();

        Assert.Equal(
            "\"C:\\Users\\Test\\AppData\\Local\\Programs\\HolidayLights\\HolidayLights.exe\" --autostart",
            root.Get(StartupRegistration.RunKey, "Holiday Lights"));
        Assert.Equal(RegistryValueKind.String, root.KindOf(StartupRegistration.RunKey, "Holiday Lights"));
        Assert.True(startup.IsEnabled);
    }

    [Fact]
    public void Disable_RemovesOnlyOurValue()
    {
        using var root = new TestRegistryRoot();
        root.Set(StartupRegistration.RunKey, "OneDrive", "\"C:\\Program Files\\Microsoft OneDrive\\OneDrive.exe\" /background");
        var startup = new StartupRegistration(Paths, new AppRuntimeOptions(), new RecordingLog(), root.Registry);
        startup.Enable();

        startup.Disable();

        Assert.False(startup.IsEnabled);
        Assert.Null(root.Get(StartupRegistration.RunKey, "Holiday Lights"));
        Assert.NotNull(root.Get(StartupRegistration.RunKey, "OneDrive"));
        startup.Disable();
    }

    [Theory]
    [InlineData("\"C:\\Old Place\\HolidayLights.exe\" --autostart", false)]
    [InlineData("\"c:\\users\\test\\appdata\\local\\programs\\holidaylights\\HOLIDAYLIGHTS.EXE\" --autostart", true)]
    [InlineData("C:\\Users\\Test\\AppData\\Local\\Programs\\HolidayLights\\HolidayLights.exe", true)]
    [InlineData("", false)]
    public void IsEnabled_OnlyForThisInstallation(string value, bool expected)
    {
        using var root = new TestRegistryRoot();
        root.Set(StartupRegistration.RunKey, "Holiday Lights", value);

        Assert.Equal(expected, new StartupRegistration(Paths, new AppRuntimeOptions(), new RecordingLog(), root.Registry).IsEnabled);
    }

    [Theory]
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 3, 0, 0, 0, 0x10, 0x2A, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 6, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 7 }, true)]
    [InlineData(new byte[0], false)]
    public void IsDisabledByWindows_ReadsTheFirstByteOfStartupApproved(byte[] state, bool expected)
    {
        using var root = new TestRegistryRoot();
        root.Set(StartupRegistration.ApprovedKey, "Holiday Lights", state, RegistryValueKind.Binary);
        var startup = new StartupRegistration(Paths, new AppRuntimeOptions(), new RecordingLog(), root.Registry);

        Assert.Equal(expected, startup.IsDisabledByWindows);
        startup.Enable();
        startup.Disable();
        Assert.Equal(state, root.Get(StartupRegistration.ApprovedKey, "Holiday Lights"));
    }

    [Fact]
    public void IsDisabledByWindows_FalseWithoutAnEntry()
    {
        using var root = new TestRegistryRoot();

        Assert.False(new StartupRegistration(Paths, new AppRuntimeOptions(), new RecordingLog(), root.Registry).IsDisabledByWindows);
    }

    [Fact]
    public void NoSystemChanges_SkipsAndLogsEveryWrite()
    {
        using var root = new TestRegistryRoot();
        var log = new RecordingLog();
        var startup = new StartupRegistration(Paths, new AppRuntimeOptions { AllowSystemChanges = false }, log, root.Registry);

        startup.Enable();
        Assert.False(root.Exists(StartupRegistration.RunKey));

        root.Set(StartupRegistration.RunKey, "Holiday Lights", startup.Command);
        startup.Disable();
        Assert.NotNull(root.Get(StartupRegistration.RunKey, "Holiday Lights"));
        Assert.Equal(2, log.Entries.Count(e => e.Message.Contains("System changes are turned off", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheRealRunKey_CanBeReadWithoutChangingIt()
    {
        var startup = new StartupRegistration(DataPaths.FromEnvironment(), new AppRuntimeOptions { AllowSystemChanges = false }, new RecordingLog());

        // The test output folder is never the installed program.
        Assert.False(startup.IsEnabled);
        _ = startup.IsDisabledByWindows;
    }
}
