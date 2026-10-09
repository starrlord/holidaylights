using System.Diagnostics;
using HolidayLights.Audio.Events;
using HolidayLights.Rendering;
using HolidayLights.Rendering.Interop;
using HolidayLights.Tests.Rendering.RealCore;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>
/// Live robustness checks with the real lights (PRODUCT-SPEC 5.12, 5.13; runs only with <c>HOLIDAYLIGHTS_LIVE_TESTS=1</c>): a
/// layer window destroyed from outside (as when Explorer, its parent or owner, exits) comes back within 3 s; <c>TaskbarCreated</c>
/// keeps the lights; a lost graphics device is recreated within 1 s and a second loss within a minute switches to software
/// drawing, both still pixel-exact; a display that no monitor shows any more stays hidden; lights off and pauses stop the
/// commits; one resting display hides alone. Nothing in Windows is changed: only the presenter's own windows are touched.
/// </summary>
[Trait("Category", "Live")]
[Collection(nameof(LiveDesktopCollection))]
public sealed class LiveRecoveryProbe : IClassFixture<RealLights>
{
    private readonly RealLights lights;
    private readonly ITestOutputHelper output;
    private long revision;

    public LiveRecoveryProbe(RealLights lights, ITestOutputHelper output)
    {
        this.lights = lights;
        this.output = output;
    }

    [LiveFact]
    public async Task RecoversFromDestroyedWindowsDeviceLossAndVanishedDisplays()
    {
        DesktopCapture.UsePhysicalPixels();
        IReadOnlyList<DisplayInfo> displays = DesktopCapture.Displays();
        ThemeDefinition christmas = lights.Theme("Christmas 1");
        var log = new RecordingLog();
        using LightsPresenter presenter = LiveSupport.CreatePresenter(lights, new MusicEventHub(), log);
        try
        {
            presenter.Start();
            Apply(presenter, lights.Scene(christmas, displays, LayerMode.BehindIcons));
            Assert.True(LiveSupport.WaitFor(() => LiveSupport.AllShown(presenter, displays.Count, LayerMode.BehindIcons), TimeSpan.FromSeconds(10)), "The lights did not start.");

            // 1. A layer window is destroyed from outside (as when Explorer, its parent, exits).
            nint victim = (nint)presenter.Diagnostics.Layers[0].Window;
            var watch = Stopwatch.StartNew();
            Assert.True(TestWindows.PostMessageW(victim, TestWindows.CloseMessage, 0, 0));
            Assert.True(
                LiveSupport.WaitFor(() => LiveSupport.AllShown(presenter, displays.Count, LayerMode.BehindIcons) && presenter.Diagnostics.Layers.All(l => (nint)l.Window != victim && User32.IsWindowVisible((nint)l.Window)), TimeSpan.FromSeconds(3)),
                "The destroyed layer was not re-created within 3 s: " + string.Join(" | ", log.Entries.Select(e => e.Message)));
            output.WriteLine($"Destroyed layer re-created in the requested mode after {watch.ElapsedMilliseconds} ms.");

            // 2. TaskbarCreated (Explorer restarted, or a DPI change): the lights stay in the requested mode.
            nint watcher = await presenter.GetWatcherWindowAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotEqual(0, watcher);
            Assert.True(TestWindows.PostMessageW(watcher, User32.RegisterWindowMessageW("TaskbarCreated"), 0, 0));
            Thread.Sleep(800);
            Assert.True(LiveSupport.AllShown(presenter, displays.Count, LayerMode.BehindIcons), "TaskbarCreated lost the lights.");

            // 3. The graphics device is lost (driver update, TDR): everything is re-created within 1 s, still exact.
            LightsScene still = Apply(presenter, lights.Scene(christmas, displays, LayerMode.OnTop, r => r with { Pattern = FlashPatternId.DontFlash }));
            Assert.True(LiveSupport.WaitFor(() => presenter.Diagnostics.SceneRevision == still.Revision && LiveSupport.AllShown(presenter, displays.Count, LayerMode.OnTop), TimeSpan.FromSeconds(5)));
            watch.Restart();
            presenter.SimulateDeviceLoss();
            Assert.True(
                LiveSupport.WaitFor(() => presenter.Diagnostics.DeviceLosses == 1 && LiveSupport.AllShown(presenter, displays.Count, LayerMode.OnTop), TimeSpan.FromSeconds(1)),
                "The lights did not come back within 1 s after a device loss.");
            output.WriteLine($"Device loss recovered after {watch.ElapsedMilliseconds} ms; health {presenter.Status.Health}, adapter {presenter.Diagnostics.Adapter}.");
            Assert.Equal(LightsHealth.Running, presenter.Status.Health);
            CheckExact(still, "after the device loss");

            // 4. A second loss within a minute: software drawing (WARP), reported as such, still exact.
            presenter.SimulateDeviceLoss();
            Assert.True(
                LiveSupport.WaitFor(() => presenter.Diagnostics.DeviceLosses == 2 && presenter.Status.Health == LightsHealth.SoftwareRendering && LiveSupport.AllShown(presenter, displays.Count, LayerMode.OnTop), TimeSpan.FromSeconds(2)),
                $"Two losses did not switch to software drawing: {LiveSupport.Describe(presenter.Status)}");
            Assert.True(presenter.Diagnostics.SoftwareRendering);
            output.WriteLine($"Second device loss: software drawing on {presenter.Diagnostics.Adapter}.");
            CheckExact(still, "with software drawing");

            // 4b. The Lights thread fails: it is restarted with the latest scene (no animation) and reports the restart.
            watch.Restart();
            presenter.SimulateCrash();
            Assert.True(
                LiveSupport.WaitFor(() => presenter.Status.RestartCount == 1 && LiveSupport.AllShown(presenter, displays.Count, LayerMode.OnTop), TimeSpan.FromSeconds(3)),
                $"The Lights thread did not come back: {LiveSupport.Describe(presenter.Status)}");
            output.WriteLine($"Lights thread restarted after {watch.ElapsedMilliseconds} ms (restart count {presenter.Status.RestartCount}).");
            CheckExact(still, "after the Lights thread restarted");

            // 5. A display no monitor shows any more (unplugged before the scene builder caught up): its layer stays hidden.
            DisplayInfo gone = displays[0] with { DeviceId = "unplugged", DeviceName = @"\\.\DISPLAY99", Number = 9, Bounds = RectI.FromXYWH(20_000, 0, 1920, 1080), WorkArea = RectI.FromXYWH(20_000, 0, 1920, 1032), Dpi = 96 };
            LightsScene withGone = Apply(presenter, lights.Scene(christmas, [.. displays, gone], LayerMode.OnTop, r => r with { Pattern = FlashPatternId.DontFlash }));
            Assert.True(LiveSupport.WaitFor(
                () => presenter.Diagnostics.SceneRevision == withGone.Revision && presenter.Status.Displays.Count == displays.Count + 1
                    && presenter.Status.Displays.Single(d => d.DisplayId == "unplugged").Effective is null
                    && presenter.Status.Displays.Where(d => d.DisplayId != "unplugged").All(d => d.Effective == LayerMode.OnTop),
                TimeSpan.FromSeconds(5)), $"A display without a monitor was shown: {LiveSupport.Describe(presenter.Status)}");
            output.WriteLine("A display that no monitor shows stays hidden; the others keep their lights.");

            // 6. Lights off and a global pause: no commits while hidden, no clock.
            Apply(presenter, lights.Scene(christmas, displays, LayerMode.BehindIcons, r => r with { LightsOn = false }));
            Assert.True(LiveSupport.WaitFor(() => presenter.Status.Displays.All(d => d.Effective is null), TimeSpan.FromSeconds(2)), "Lights off did not hide the layers.");
            AssertNoCommits(presenter, "with the lights off");

            Apply(presenter, lights.Scene(christmas, displays, LayerMode.BehindIcons));
            Assert.True(LiveSupport.WaitFor(() => LiveSupport.AllShown(presenter, displays.Count, LayerMode.BehindIcons), TimeSpan.FromSeconds(2)), "The lights did not come back on.");
            presenter.SetPaused(new LightsPauseState(PauseReasons.SessionLocked, new HashSet<string>()));
            Assert.True(LiveSupport.WaitFor(() => presenter.Status.Paused == PauseReasons.SessionLocked && presenter.Status.Displays.All(d => d.Resting && d.Effective is null), TimeSpan.FromSeconds(2)));
            AssertNoCommits(presenter, "while the session is locked");
            Assert.False(presenter.Diagnostics.ClockRunning);
            presenter.SetPaused(LightsPauseState.None);
            Assert.True(LiveSupport.WaitFor(() => LiveSupport.AllShown(presenter, displays.Count, LayerMode.BehindIcons), TimeSpan.FromSeconds(2)), "Resuming did not show the lights.");

            // 7. One display rests (a full-screen app there): only that display hides; the clock keeps running.
            string resting = displays[^1].DeviceId;
            presenter.SetPaused(new LightsPauseState(PauseReasons.None, new HashSet<string> { resting }));
            Assert.True(LiveSupport.WaitFor(() => presenter.Status.Displays.Single(d => d.DisplayId == resting) is { Resting: true, Effective: null }, TimeSpan.FromSeconds(2)));
            Thread.Sleep(2_500);
            Assert.True(displays.Count == 1 || presenter.Diagnostics.ClockRunning, "The clock stopped although another display still shows lights.");
            presenter.SetPaused(LightsPauseState.None);
        }
        finally
        {
            await presenter.ShutdownAsync(fadeOut: false).WaitAsync(TimeSpan.FromSeconds(5));
        }

        LiveSupport.WriteLog(log, output);
        Assert.Equal(0, LiveSupport.CountLayerWindows());

        // The only error is the simulated failure of the Lights thread.
        Assert.Equal(["The Lights thread failed."], log.Entries.Where(e => e.Level == AppLogLevel.Error).Select(e => e.Message));
    }

    private LightsScene Apply(LightsPresenter presenter, LightsScene scene)
    {
        scene = scene with { Revision = ++revision };
        presenter.Apply(scene, SceneTransition.None);
        return scene;
    }

    /// <summary>Every bulb on top of all windows shows its Core sprite at its Core position.</summary>
    private void CheckExact(LightsScene scene, string when)
    {
        Thread.Sleep(400);
        RectI screen = scene.Displays.Select(d => d.Display.Bounds).Aggregate((a, b) => a.Union(b));
        ScreenShot shot = ScreenShot.Take(screen);
        IFlashSequencer sequencer = lights.Flash.CreateSequencer(scene.Layout, lights.Catalog, scene.Flash);
        int exact = 0;
        foreach (BulbPlacement placement in scene.Layout.Placements)
        {
            int frame = ScreenExpectations.ShownFrame(sequencer.GetBulb(placement.Ordinal), sequencer.Current[placement.Ordinal]);
            (PremultipliedImage image, PointI position) = ScreenExpectations.Sprite(lights, scene, placement, frame);
            (int opaque, int matching) = ScreenExpectations.Compare(shot, image, position);
            exact += matching >= opaque * 0.98 ? 1 : 0;
        }

        output.WriteLine($"{exact} of {scene.Layout.Placements.Count} bulbs exact {when}.");
        Assert.True(exact >= scene.Layout.Placements.Count * 0.995, $"Only {exact} of {scene.Layout.Placements.Count} bulbs are exact {when}.");
    }

    private void AssertNoCommits(LightsPresenter presenter, string when)
    {
        // Let the hiding settle (fades end, windows hide), then compare two fresh snapshots taken 4 s apart.
        Thread.Sleep(600);
        LightsDiagnostics first = presenter.GetDiagnosticsAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Thread.Sleep(4_000);
        LightsDiagnostics second = presenter.GetDiagnosticsAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Assert.Equal(first.Commits, second.Commits);
        Assert.Equal(first.Steps, second.Steps);
        output.WriteLine($"No commits and no steps {when} ({(second.Uptime - first.Uptime).TotalSeconds:F1} s).");
    }
}
