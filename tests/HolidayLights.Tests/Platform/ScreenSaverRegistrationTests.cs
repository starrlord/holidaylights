using System.Globalization;
using HolidayLights.Platform.Integration;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Platform;

/// <summary>
/// Runs against <see cref="TestRegistryRoot"/>; <c>SystemParametersInfo</c> is replaced by registry writes to that root,
/// so the real screen saver is never touched.
/// </summary>
public sealed class ScreenSaverRegistrationTests : IDisposable
{
    private const string Desktop = ScreenSaverRegistration.DesktopKey;
    private const string Policy = ScreenSaverRegistration.PolicyKey;

    private readonly TestRegistryRoot root = new();
    private readonly TempDataRoot data = new();
    private readonly DataPaths paths;
    private readonly FakeShell shell = new();
    private readonly RegistryParameters parameters;

    public ScreenSaverRegistrationTests()
    {
        string install = Path.Combine(data.Root, "Programs", "HolidayLights");
        Directory.CreateDirectory(install);
        paths = DataPaths.ForDataRoot(data.Root, install);
        File.WriteAllText(paths.InstalledExePath, "stand-in for the program");
        parameters = new RegistryParameters(root);
    }

    public void Dispose()
    {
        try
        {
            root.Dispose();
        }
        finally
        {
            TestFolders.Delete(data);
        }
    }

    [Fact]
    public void NoScreenSaver_IsNotOurs()
    {
        ScreenSaverStatus status = Create().GetStatus();

        Assert.Equal(ScreenSaverState.NotOurs, status.State);
        Assert.Null(status.ScrnsaveExe);
        Assert.Equal(600, status.TimeoutSeconds);
        Assert.False(status.PolicyManaged);
    }

    [Fact]
    public void AnotherSaver_IsNotOurs()
    {
        root.Set(Desktop, "SCRNSAVE.EXE", @"C:\WINDOWS\system32\Mystify.scr");
        root.Set(Desktop, "ScreenSaveTimeOut", "300");

        ScreenSaverStatus status = Create().GetStatus();

        Assert.Equal(ScreenSaverState.NotOurs, status.State);
        Assert.Equal(@"C:\WINDOWS\system32\Mystify.scr", status.ScrnsaveExe);
        Assert.Equal(300, status.TimeoutSeconds);
    }

    [Theory]
    [InlineData("1", ScreenSaverState.Ours)]
    [InlineData(null, ScreenSaverState.Ours)]
    [InlineData("0", ScreenSaverState.OursButTurnedOff)]
    public void OurScreenSaver_IsOursUnlessTurnedOff(string? active, ScreenSaverState expected)
    {
        root.Set(Desktop, "SCRNSAVE.EXE", paths.InstalledScreenSaverPath.ToUpperInvariant());
        if (active is not null)
        {
            root.Set(Desktop, "ScreenSaveActive", active);
        }

        Assert.Equal(expected, Create().GetStatus().State);
    }

    [Theory]
    [InlineData(@"C:\WINDOWS\system32\HOLIDA~1.SCR")]
    [InlineData(@"C:\Windows\System32\Holiday Lights.scr")]
    [InlineData(@"D:\Gone\holiday lights.SCR")]
    public void TheMissingHolidayLights54Saver_IsLegacy(string saver)
    {
        root.Set(Desktop, "SCRNSAVE.EXE", saver);

        Assert.Equal(ScreenSaverState.Legacy54, Create().GetStatus().State);
    }

    [Fact]
    public void TheHolidayLights54SaverFile_IsRecognizedByItsStringResource()
    {
        // A stand-in with the 5.4 saver's string resources (the original program is not part of the repository).
        string original = Path.Combine(data.Root, "HolidayLightsSaver.scr");
        StringResourceImage.Write(original, StringResourceImage.HolidayLights54Saver);
        string copy = Path.Combine(data.Root, "Some Saver.scr");
        File.Copy(original, copy);
        root.Set(Desktop, "SCRNSAVE.EXE", copy);

        Assert.Equal(ScreenSaverState.Legacy54, Create().GetStatus().State);
        Assert.Equal("TigerTechHolidayLights", LegacyScreenSaver.ReadStringResource(original, 2));
        Assert.Equal("Holiday Lights", LegacyScreenSaver.ReadStringResource(original, 1));
    }

    [Fact]
    public void AnExistingFileNamedLike54ButWithoutItsSignature_IsNotLegacy()
    {
        string impostor = Path.Combine(data.Root, "Holiday Lights.scr");
        File.WriteAllText(impostor, "not a program");
        root.Set(Desktop, "SCRNSAVE.EXE", impostor);

        Assert.Equal(ScreenSaverState.NotOurs, Create().GetStatus().State);
        Assert.Null(LegacyScreenSaver.ReadStringResource(impostor, 2));
    }

    [Fact]
    public void PolicyValues_OverrideAndAreReported()
    {
        root.Set(Desktop, "SCRNSAVE.EXE", paths.InstalledScreenSaverPath);
        root.Set(Policy, "SCRNSAVE.EXE", @"C:\WINDOWS\system32\scrnsave.scr");
        root.Set(Policy, "ScreenSaveTimeOut", "900");

        ScreenSaverStatus status = Create().GetStatus();

        Assert.True(status.PolicyManaged);
        Assert.Equal(ScreenSaverState.NotOurs, status.State);
        Assert.Equal(900, status.TimeoutSeconds);
    }

    [Fact]
    public void Use_RemembersThePreviousValuesAndPointsAtTheFullLongPath()
    {
        root.Set(Desktop, "SCRNSAVE.EXE", @"C:\WINDOWS\system32\HOLIDA~1.SCR");
        root.Set(Desktop, "ScreenSaveActive", "1");
        root.Set(Desktop, "ScreenSaveTimeOut", "1200");
        ScreenSaverRegistration registration = Create();

        ScreenSaverPrevious previous = registration.Use();

        Assert.Equal(@"C:\WINDOWS\system32\HOLIDA~1.SCR", previous.ScrnsaveExe);
        Assert.True(previous.Active);
        Assert.Equal(1200, previous.TimeoutSeconds);
        Assert.Equal(paths.InstalledScreenSaverPath, root.Get(Desktop, "SCRNSAVE.EXE"));
        Assert.Equal("1", root.Get(Desktop, "ScreenSaveActive"));
        Assert.Equal("1200", root.Get(Desktop, "ScreenSaveTimeOut"));
        Assert.Equal(["active=1"], parameters.Calls);
        Assert.Equal(ScreenSaverState.Ours, registration.GetStatus().State);
    }

    [Fact]
    public void Use_InstallsTheScreenSaverCopyOfTheProgram()
    {
        ScreenSaverRegistration registration = Create();

        registration.Use();

        Assert.Equal(File.ReadAllText(paths.InstalledExePath), File.ReadAllText(paths.InstalledScreenSaverPath));
        Assert.False(File.Exists(paths.InstalledScreenSaverPath + ".new"));
    }

    [Fact]
    public void InstallScreenSaverFile_RefreshesAnOutdatedCopyOnly()
    {
        ScreenSaverRegistration registration = Create();
        Assert.True(registration.InstallScreenSaverFile());
        DateTime installed = File.GetLastWriteTimeUtc(paths.InstalledScreenSaverPath);

        Assert.True(registration.InstallScreenSaverFile());
        Assert.Equal(installed, File.GetLastWriteTimeUtc(paths.InstalledScreenSaverPath));

        File.WriteAllText(paths.InstalledExePath, "a newer program");
        Assert.True(registration.InstallScreenSaverFile());
        Assert.Equal("a newer program", File.ReadAllText(paths.InstalledScreenSaverPath));
    }

    [Fact]
    public void Use_WithoutATimeoutSetsTenMinutes()
    {
        ScreenSaverRegistration registration = Create();

        ScreenSaverPrevious previous = registration.Use();

        Assert.Null(previous.ScrnsaveExe);
        Assert.Equal(600, previous.TimeoutSeconds);
        Assert.Equal("600", root.Get(Desktop, "ScreenSaveTimeOut"));
        Assert.Equal(["timeout=600", "active=1"], parameters.Calls);
    }

    [Fact]
    public void StopUsing_RestoresTheRememberedSaver()
    {
        root.Set(Desktop, "SCRNSAVE.EXE", @"C:\WINDOWS\system32\Bubbles.scr");
        root.Set(Desktop, "ScreenSaveActive", "0");
        root.Set(Desktop, "ScreenSaveTimeOut", "300");
        ScreenSaverRegistration registration = Create();
        ScreenSaverPrevious previous = registration.Use();
        registration.SetTimeout(TimeSpan.FromMinutes(20));

        registration.StopUsing(previous);

        Assert.Equal(@"C:\WINDOWS\system32\Bubbles.scr", root.Get(Desktop, "SCRNSAVE.EXE"));
        Assert.Equal("0", root.Get(Desktop, "ScreenSaveActive"));
        Assert.Equal("300", root.Get(Desktop, "ScreenSaveTimeOut"));
        Assert.Equal(ScreenSaverState.NotOurs, registration.GetStatus().State);
    }

    [Fact]
    public void StopUsing_NeverRestoresTheBroken54Saver()
    {
        root.Set(Desktop, "SCRNSAVE.EXE", @"C:\WINDOWS\system32\HOLIDA~1.SCR");
        ScreenSaverRegistration registration = Create();
        ScreenSaverPrevious previous = registration.Use();

        registration.StopUsing(previous);

        Assert.Null(root.Get(Desktop, "SCRNSAVE.EXE"));
        Assert.Equal(ScreenSaverState.NotOurs, registration.GetStatus().State);
    }

    [Fact]
    public void StopUsing_WithNothingRememberedChoosesNone()
    {
        ScreenSaverRegistration registration = Create();
        registration.Use();

        registration.StopUsing(null);

        Assert.Null(root.Get(Desktop, "SCRNSAVE.EXE"));
        Assert.Equal("1", root.Get(Desktop, "ScreenSaveActive"));
    }

    [Fact]
    public void StopUsing_LeavesASaverTheUserPickedLater()
    {
        ScreenSaverRegistration registration = Create();
        ScreenSaverPrevious previous = registration.Use();
        root.Set(Desktop, "SCRNSAVE.EXE", @"C:\WINDOWS\system32\Ribbons.scr");

        registration.StopUsing(previous);

        Assert.Equal(@"C:\WINDOWS\system32\Ribbons.scr", root.Get(Desktop, "SCRNSAVE.EXE"));
    }

    [Fact]
    public void SetTimeoutAndTurnOn_WriteTheSwitches()
    {
        root.Set(Desktop, "SCRNSAVE.EXE", paths.InstalledScreenSaverPath);
        root.Set(Desktop, "ScreenSaveActive", "0");
        ScreenSaverRegistration registration = Create();
        Assert.Equal(ScreenSaverState.OursButTurnedOff, registration.GetStatus().State);

        registration.TurnOn();
        registration.SetTimeout(TimeSpan.FromMinutes(45));

        ScreenSaverStatus status = registration.GetStatus();
        Assert.Equal(ScreenSaverState.Ours, status.State);
        Assert.Equal(2700, status.TimeoutSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => registration.SetTimeout(TimeSpan.Zero));
    }

    [Fact]
    public void OpenWindowsSettings_OpensTheScreenSaverControlPanel()
    {
        Create().OpenWindowsSettings();

        Assert.Equal(["desk.cpl,,@screensaver"], shell.ControlPanels);
    }

    [Fact]
    public void NoSystemChanges_ChangesNothingButStillAnswers()
    {
        root.Set(Desktop, "SCRNSAVE.EXE", @"C:\WINDOWS\system32\Bubbles.scr");
        var registration = new ScreenSaverRegistration(
            paths, shell, new AppRuntimeOptions { AllowSystemChanges = false }, new RecordingLog(), root.Registry, parameters);

        ScreenSaverPrevious previous = registration.Use();
        registration.TurnOn();
        registration.SetTimeout(TimeSpan.FromMinutes(5));

        Assert.Equal(@"C:\WINDOWS\system32\Bubbles.scr", previous.ScrnsaveExe);
        Assert.Equal(@"C:\WINDOWS\system32\Bubbles.scr", root.Get(Desktop, "SCRNSAVE.EXE"));
        Assert.Empty(parameters.Calls);
        Assert.False(File.Exists(paths.InstalledScreenSaverPath));
    }

    [Fact]
    public void TheRealScreenSaver_CanBeReadWithoutChangingIt()
    {
        var registration = new ScreenSaverRegistration(paths, shell, new AppRuntimeOptions { AllowSystemChanges = false }, new RecordingLog());

        ScreenSaverStatus status = registration.GetStatus();

        Assert.True(Enum.IsDefined(status.State));
        Assert.True(status.TimeoutSeconds > 0);
        if (status.ScrnsaveExe is { } saver &&
            Path.GetFileName(saver).Equals("HOLIDA~1.SCR", StringComparison.OrdinalIgnoreCase) && !File.Exists(saver))
        {
            // The commissioning PC: 5.4's broken registration.
            Assert.Equal(ScreenSaverState.Legacy54, status.State);
        }
    }

    private ScreenSaverRegistration Create() =>
        new(paths, shell, new AppRuntimeOptions(), new RecordingLog(), root.Registry, parameters);

    /// <summary>Stores the two switches where <c>SystemParametersInfo(SPIF_UPDATEINIFILE)</c> would.</summary>
    private sealed class RegistryParameters(TestRegistryRoot root) : IDesktopParameters
    {
        public List<string> Calls { get; } = [];

        public void SetScreenSaveActive(bool active)
        {
            Calls.Add(active ? "active=1" : "active=0");
            root.Set(Desktop, "ScreenSaveActive", active ? "1" : "0");
        }

        public void SetScreenSaveTimeout(int seconds)
        {
            Calls.Add($"timeout={seconds}");
            root.Set(Desktop, "ScreenSaveTimeOut", seconds.ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class FakeShell : IShellOperations
    {
        public List<string> ControlPanels { get; } = [];

        public void OpenFolder(string path) => throw new NotSupportedException();

        public void ShowInFolder(string filePath) => throw new NotSupportedException();

        public void OpenSettingsUri(string uri) => throw new NotSupportedException();

        public void OpenProjectHomePage() => throw new NotSupportedException();

        public void OpenControlPanel(string arguments) => ControlPanels.Add(arguments);

        public bool MoveToRecycleBin(string path) => throw new NotSupportedException();

        public string? ResolveShortcut(string shortcutPath) => throw new NotSupportedException();
    }
}
