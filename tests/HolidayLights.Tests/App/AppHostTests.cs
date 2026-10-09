using System.Windows.Media.Imaging;
using HolidayLights.App.Shell;
using HolidayLights.Audio;
using HolidayLights.Tests.App.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.App;

/// <summary>
/// The composition root with the real engines in windowless sessions: <c>--render-test</c> on the reference PC (two
/// 3840 x 2160 displays at 150 %), <c>--diagnostics</c>, and services that are only created when a session uses them.
/// </summary>
public sealed class AppHostTests : IDisposable
{
    private readonly TempDataRoot root = new();
    private readonly RecordingLog log = new();

    public void Dispose() => root.Dispose();

    [Fact]
    public void AHostLeavesTheServicesOfXamlControlsAlone() => StaThread.Run(() =>
    {
        IAppServices? before = AppServicesHost.IsAvailable ? AppServicesHost.Current : null;
        AppHost host = Host(AppSessionKind.RenderTest);
        try
        {
            Assert.Same(before, AppServicesHost.IsAvailable ? AppServicesHost.Current : null);
        }
        finally
        {
            host.ShutDownNow();
        }
    });

    private AppHost Host(AppSessionKind session, TestComponents? components = null) => new(
        root.Paths,
        new AppRuntimeOptions { Session = session, AllowSystemChanges = false },
        log,
        ownsLog: false,
        components ?? new TestComponents(),
        TimeProvider.System,
        instance: null);

    [Fact]
    public void RenderTestDrawsEveryDisplayOfTheCurrentScene() => StaThread.Run(() =>
    {
        AppHost host = Host(AppSessionKind.RenderTest);
        string folder = Path.Combine(root.Root, "render");
        try
        {
            Assert.Equal(0, RenderTest.Run(host, folder));
        }
        finally
        {
            host.ShutDownNow();
        }

        foreach (string file in new[] { "display-1.png", "display-2.png" })
        {
            BitmapFrame frame = BitmapFrame.Create(new Uri(Path.Combine(folder, file)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert.Equal((3840, 2160), (frame.PixelWidth, frame.PixelHeight));
        }

        string description = File.ReadAllText(Path.Combine(folder, "render-test.txt"));
        Assert.Contains("Lights on, BehindIcons, EachDisplay, FlashTogether, interval 5", description);
        Assert.Contains("Display 2: bounds -3840,0,0,2160, work area -3840,0,0,2088, 144 DPI, lights", description);
        Assert.Contains("  scale 1.5", description);
        Assert.False(File.Exists(root.Paths.SettingsFile), "A render test never writes the settings.");
    }, TimeSpan.FromMinutes(3));

    [Fact]
    public void DiagnosticsReportWhatTheyFindAndWhatIsMissing() => StaThread.Run(() =>
    {
        AppHost host = Host(AppSessionKind.RenderTest, new TestComponents { FailPictures = true });
        string report;
        try
        {
            report = DiagnosticsReport.Build(host);
        }
        finally
        {
            host.ShutDownNow();
        }

        Assert.Contains($"Version: Holiday Lights - Modern Edition {VersionInfo.ProgramVersion}, Windows build 26300", report);
        Assert.Contains("Bundled content: 1501 bulbs, 46 songs, 11 pictures", report);
        Assert.Contains("Display 2: 3840 x 2160 at 150 %, position -3840, 0", report);
        Assert.Contains("Pictures: unavailable (InvalidOperationException: no pictures here)", report);
        Assert.Contains("Scene: ", report);
        Assert.Contains("Hot keys: SwitchLocation Ctrl+Alt+Shift+B on (Ok)", report);
    }, TimeSpan.FromMinutes(3));

    [Fact]
    public void ServicesAreCreatedOnlyWhenUsed() => StaThread.Run(() =>
    {
        var components = new TestComponents();
        AppHost host = Host(AppSessionKind.RenderTest, components);
        try
        {
            Assert.Empty(components.Created);
            _ = host.Lights.Scene;
            Assert.Contains("displays", components.Created);
            Assert.DoesNotContain("pictures", components.Created);
            Assert.DoesNotContain("music", components.Created);
            Assert.Same(host.Settings, host.Settings);
        }
        finally
        {
            host.ShutDownNow();
        }
    });

    [Fact]
    public void AFirstStartThatFailsWritesNoSettingsFile() => StaThread.Run(() =>
    {
        AppHost host = Host(AppSessionKind.Normal, new TestComponents { FailPictures = true });
        Task start;
        try
        {
            start = host.StartAsync(new LaunchRequest());
        }
        finally
        {
            host.ShutDownNow();
        }

        Assert.True(start.IsFaulted);
        Assert.Equal(SettingsLoadOutcome.Created, host.Settings.LoadOutcome);
        Assert.False(File.Exists(root.Paths.SettingsFile), "The next start must be a first run again (newcomer settings or the 5.4 import).");
        Assert.Contains(log.Entries, e => e.Level == AppLogLevel.Warning && e.Message.Contains("first start did not finish", StringComparison.Ordinal));
    }, TimeSpan.FromMinutes(1));

    /// <summary>
    /// The screen saver's "Settings" button (<c>/c</c>) before Holiday Lights ever started: the first run happens then, so
    /// the settings this session writes are the newcomer settings (or the 5.4 import), never plain defaults that would skip
    /// the first run; the Welcome card still waits for the first normal start (r1 review).
    /// </summary>
    [Fact]
    public void ASettingsOnlySessionOnAFreshProfileRunsTheFirstRun() => StaThread.Run(() =>
    {
        var components = new TestComponents();
        AppHost host = Host(AppSessionKind.SettingsOnly, components);
        try
        {
            host.StartAsync(new LaunchRequest { Kind = LaunchKind.ScreenSaverConfigure }).GetAwaiter().GetResult();
            Assert.Equal(SettingsPageId.ScreenSaver, Assert.Single(components.Window.Shown).Page);
            Assert.Equal(1, components.LegacyChecks);
        }
        finally
        {
            host.ShutDownNow();
        }

        Assert.True(File.Exists(root.Paths.SettingsFile));
        AppHost next = Host(AppSessionKind.Normal, new TestComponents());
        try
        {
            Assert.Equal(SettingsLoadOutcome.Loaded, next.Settings.LoadOutcome);
            AppSettings saved = next.Settings.Current;
            Assert.False(saved.Onboarding.WelcomeShown, "The Welcome card still comes at the first normal start.");
            Assert.NotEqual(new AppSettings().Themes.LastName, saved.Themes.LastName);
            Assert.True(saved.Startup.Auto, "The newcomer settings were applied.");
        }
        finally
        {
            next.ShutDownNow();
        }
    }, TimeSpan.FromMinutes(1));

    /// <summary>A settings-only session plays no music of its own, but what the Music Box plays follows Volume, Mute and MIDI Output (r1 review).</summary>
    [Fact]
    public void ASettingsOnlySessionAppliesTheUsersVolumeMuteAndOutput() => StaThread.Run(() =>
    {
        AppHost host = Host(AppSessionKind.SettingsOnly);
        try
        {
            host.Settings.Update(s => s with { Music = s.Music with { Enabled = true, Volume = 25, Muted = true, MidiDevice = "Synth B" } }, SettingsChange.Internal);
            var music = (FakeMusicDirector)host.Music;
            MusicPolicy policy = Assert.Single(music.Policies);
            Assert.False(policy.Enabled, "No song starts by itself.");
            Assert.Equal((25, true, "Synth B"), (policy.Volume, policy.Muted, policy.MidiDevice));

            host.Settings.Update(s => s with { Music = s.Music with { Volume = 80, Muted = false } }, SettingsChange.Edit("Volume"));
            Assert.Equal(2, music.Policies.Count);
            Assert.Equal((80, false), (music.Policies[^1].Volume, music.Policies[^1].Muted));
        }
        finally
        {
            host.ShutDownNow();
        }
    });

    /// <summary>The real components, with the displays and Windows facts of the reference PC and no system changes.</summary>
    private sealed class TestComponents : AppComponentFactory
    {
        public bool FailPictures { get; init; }

        public FakeSettingsWindow Window { get; } = new();

        /// <summary>How often the first run looked for Holiday Lights 5.4 (never present here).</summary>
        public int LegacyChecks { get; private set; }

        public override ISettingsWindowService CreateSettingsWindow(AppHost host) => Window;

        public override ILegacyImporter CreateLegacyImporter(AppHost host) => new NoLegacyInstall(() => LegacyChecks++);

        public List<string> Created { get; } = [];

        public override IDisplayService CreateDisplays(AppHost host)
        {
            Created.Add("displays");
            return new FakeDisplayService();
        }

        public override ISystemInfo CreateSystemInfo(AppHost host) => new FakeSystemInfo();

        public override IHotKeyService CreateHotKeyService(AppHost host) => new FakeHotKeyService();

        public override IPictureLibrary CreatePictures(AppHost host)
        {
            Created.Add("pictures");
            return FailPictures ? throw new InvalidOperationException("no pictures here") : base.CreatePictures(host);
        }

        public override HolidayLights.Audio.IMusicDirector CreateMusic(AppHost host)
        {
            Created.Add("music");
            return new FakeMusicDirector();
        }

        public override IHoldingFolder CreateHoldingFolder(AppHost host) => new TestHoldingFolder(host.Paths);
    }

    /// <summary>A PC without Holiday Lights 5.4 (the tests never read the real 5.4 registry key).</summary>
    private sealed class NoLegacyInstall(Action checkedForIt) : ILegacyImporter
    {
        public bool IsLegacyInstallPresent()
        {
            checkedForIt();
            return false;
        }

        public LegacyImportPreview? Analyze() => null;

        public LegacyImportResult Import(LegacyImportPreview preview, AppSettings current, LegacyImportMode mode) =>
            throw new InvalidOperationException("Holiday Lights 5.4 is not present.");
    }
}
