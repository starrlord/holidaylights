using System.Diagnostics;
using HolidayLights.Audio.Events;
using HolidayLights.Rendering;
using HolidayLights.Tests.Rendering.RealCore;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>
/// The performance budget of PRODUCT-SPEC 5.14 on the real desktop (runs only with <c>HOLIDAYLIGHTS_LIVE_TESTS=1</c>): the
/// Lights thread's CPU while the default theme animates on both displays, per processed step ("frame") and as a share of one
/// core, while the lights stand still, while they rest (no commits at all), and while Dance to the Music follows notes; the
/// time to the first committed frame with a cold and a warm sprite cache. The process CPU of the whole test host is reported
/// next to it as an upper bound.
/// </summary>
[Trait("Category", "Live")]
[Collection(nameof(LiveDesktopCollection))]
public sealed class LivePerformanceProbe
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper output;

    public LivePerformanceProbe(ITestOutputHelper output) => this.output = output;

    [LiveFact]
    public async Task StaysWithinTheBudget()
    {
        DesktopCapture.UsePhysicalPixels();
        IReadOnlyList<DisplayInfo> displays = DesktopCapture.Displays();
        output.WriteLine($"Displays {(DisplayPower.IsDisplayOn() ? "on" : "off")}: {string.Join("; ", displays.Select(d => d.Describe()))}.");
        var hub = new MusicEventHub();
        var log = new RecordingLog();

        // Cold: a new data root, so no sprite is cached on disk or in memory yet (the first launch after install).
        using (var cold = new RealLights())
        {
            await MeasureFirstFrame(cold, hub, displays, "cold");
            await MeasureFirstFrame(cold, hub, displays, "warm");
        }

        using var lights = new RealLights();
        using LightsPresenter presenter = LiveSupport.CreatePresenter(lights, hub, log);
        try
        {
            presenter.Start();
            ThemeDefinition halloween = lights.Theme("Halloween");

            Measurement animating = Measure(presenter, lights.Scene(halloween, displays, LayerMode.BehindIcons) with { Revision = 1 }, displays, "Halloween (the default theme in October), Flash Together, 300 ms");
            Assert.True(animating.CoreShare <= 0.005, $"The default theme used {animating.CoreShare:P3} of a core (budget 0.5 %).");

            Measurement classic = Measure(presenter, lights.Scene(lights.Theme("Christmas 1"), displays, LayerMode.BehindIcons) with { Revision = 2 }, displays, "Christmas 1 (the 5.4 default), Flash Together, 300 ms");
            Assert.Equal(0, classic.Steps);
            Assert.True(classic.CoreShare <= 0.005);

            Measurement chase = Measure(presenter, lights.Scene(halloween, displays, LayerMode.BehindIcons, r => r with { Pattern = FlashPatternId.ChaseAround }) with { Revision = 3 }, displays, "Halloween, Chase Around the Screen, 300 ms");
            Assert.True(chase.CoreShare <= 0.005);

            Measurement still = Measure(presenter, lights.Scene(halloween, displays, LayerMode.BehindIcons, r => r with { Pattern = FlashPatternId.DontFlash }) with { Revision = 4 }, displays, "Halloween, Don't Flash");
            Assert.Equal(0, still.Steps);
            Assert.True(still.Commits <= 1, "A still picture kept committing.");

            presenter.SetPaused(new LightsPauseState(PauseReasons.SessionLocked, new HashSet<string>()));
            Assert.True(LiveSupport.WaitFor(() => presenter.Status.Displays.All(d => d.Resting && d.Effective is null), TimeSpan.FromSeconds(2)));
            Measurement resting = Measure(presenter, null, displays, "everything resting (session locked)");
            Assert.Equal(0, resting.Commits);
            Assert.Equal(0, resting.Steps);
            presenter.SetPaused(LightsPauseState.None);

            Measurement dance = Measure(presenter, lights.Scene(lights.Theme("Holiday Party"), displays, LayerMode.BehindIcons) with { Revision = 5 }, displays, "Holiday Party, Dance to the Music with notes every 120 ms", hub);
            Assert.True(dance.CoreShare <= 0.02, $"Dance used {dance.CoreShare:P3} of a core (budget 2 %).");
        }
        finally
        {
            await presenter.ShutdownAsync(fadeOut: false).WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.DoesNotContain(log.Entries, e => e.Level == AppLogLevel.Error);
    }

    /// <summary>Time from <see cref="LightsPresenter.Start"/> to the first committed frame (goal G1: 2 s warm, 6 s cold).</summary>
    private async Task MeasureFirstFrame(RealLights lights, MusicEventHub hub, IReadOnlyList<DisplayInfo> displays, string cache)
    {
        var log = new RecordingLog();
        long started = Stopwatch.GetTimestamp();
        using LightsPresenter presenter = LiveSupport.CreatePresenter(lights, hub, log);
        presenter.Start();
        presenter.Apply(lights.Scene(lights.Theme("Halloween"), displays, LayerMode.BehindIcons), SceneTransition.FirstRunPowerUp);
        Assert.True(LiveSupport.WaitFor(() => presenter.Status.FirstFrameTimestamp is not null, TimeSpan.FromSeconds(10)), "No frame was committed.");
        double milliseconds = (presenter.Status.FirstFrameTimestamp!.Value - started) * 1000.0 / Stopwatch.Frequency;
        output.WriteLine($"First committed frame with a {cache} sprite cache: {milliseconds:F0} ms after Start (preparation {presenter.Diagnostics.LastPreparationMilliseconds:F0} ms).");
        Assert.True(milliseconds <= (cache == "cold" ? 6_000 : 2_000), $"The first frame took {milliseconds:F0} ms with a {cache} cache.");
        await presenter.ShutdownAsync(fadeOut: false).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private Measurement Measure(LightsPresenter presenter, LightsScene? scene, IReadOnlyList<DisplayInfo> displays, string label, MusicEventHub? music = null)
    {
        if (scene is not null)
        {
            presenter.Apply(scene, SceneTransition.None);
            Assert.True(LiveSupport.WaitFor(() => presenter.Diagnostics.SceneRevision == scene.Revision && LiveSupport.AllShown(presenter, displays.Count, LayerMode.BehindIcons), TimeSpan.FromSeconds(20)));
        }

        // Let fades settle, then take fresh diagnostics at both ends of the window.
        Thread.Sleep(1_000);
        using var process = Process.GetCurrentProcess();
        TimeSpan processBefore = process.TotalProcessorTime;
        LightsDiagnostics start = presenter.GetDiagnosticsAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        var watch = Stopwatch.StartNew();
        int notes = 0;
        if (music is not null)
        {
            music.Publish(new MusicEvent(MusicEventKind.SongStarted, Stopwatch.GetTimestamp(), 0, 0, 0));
        }

        while (watch.Elapsed < Window)
        {
            if (music is not null)
            {
                long heard = Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 25);
                music.Publish(new MusicEvent(MusicEventKind.NoteOn, heard, (byte)(notes % 3 == 0 ? 9 : 0), (byte)(notes % 3 == 0 ? 36 : 60 + (notes % 12)), 100));
                if (notes % 4 == 0)
                {
                    music.Publish(new MusicEvent(MusicEventKind.Beat, heard, 0, 0, 127));
                }

                notes++;
                Thread.Sleep(120);
            }
            else
            {
                Thread.Sleep(200);
            }
        }

        LightsDiagnostics end = presenter.GetDiagnosticsAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        process.Refresh();
        double wall = (end.Uptime - start.Uptime).TotalMilliseconds;
        double busy = (end.BusyTime - start.BusyTime).TotalMilliseconds;
        double cpu = (end.ThreadCpuTime - start.ThreadCpuTime).TotalMilliseconds;
        double megacycles = (end.ThreadCycles - start.ThreadCycles) / 1e6;
        long steps = end.Steps - start.Steps;
        long commits = end.Commits - start.Commits;
        double processShare = (process.TotalProcessorTime - processBefore).TotalMilliseconds / Math.Max(1, watch.Elapsed.TotalMilliseconds);
        var measurement = new Measurement(steps, commits, busy / Math.Max(1, wall));
        output.WriteLine(
            $"{label}: over {wall / 1000:F1} s the Lights thread was awake {busy:F1} ms = {100 * measurement.CoreShare:F3} % of one core "
            + $"({megacycles:F1} M cycles, scheduler CPU {cpu:F0} ms); {steps} steps processed"
            + (steps > 0 ? $" ({busy / steps:F3} ms and {megacycles / steps:F2} M cycles per step)" : string.Empty)
            + $", {commits} commits{(commits > 0 ? $" ({busy / commits:F3} ms per commit)" : string.Empty)}{(notes > 0 ? $", {notes} notes" : string.Empty)}; "
            + $"{end.PeriodicLightBulbs} light bulbs on repeating animations, {end.Visuals} visuals, {end.Surfaces} surfaces, max step latency {end.MaxStepLatencyMilliseconds:F2} ms; "
            + $"whole test process {100 * processShare:F2} % of one core.");
        return measurement;
    }

    private sealed record Measurement(long Steps, long Commits, double CoreShare);
}
