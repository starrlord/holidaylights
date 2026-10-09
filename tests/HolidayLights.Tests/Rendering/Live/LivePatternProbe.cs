using System.Diagnostics;
using HolidayLights.Audio.Events;
using HolidayLights.Rendering;
using HolidayLights.Tests.Rendering.RealCore;
using HolidayLights.Tests.Shared;
using Xunit.Abstractions;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>
/// Live check that every flash pattern really plays on the screen as the Core says (runs with <c>HOLIDAYLIGHTS_LIVE_TESTS=1</c>
/// and the displays on): the presenter draws Halloween on top of all windows on every display with the real Core; captures
/// of the top and bottom strips at many moments are compared, bulb by bulb, with an independent flash sequencer sampled on
/// the presenter's published clock. A light bulb must show the brightness the sequencer gives (the fit of
/// <c>unlit + b (lit - unlit)</c>), an animation bulb its frame, within the 50 ms around each capture (DWM presents a frame or
/// two later). Dance to the Music is played with notes published into the real music event hub.
/// </summary>
[Trait("Category", "Live")]
[Collection(nameof(LiveDesktopCollection))]
public sealed class LivePatternProbe : IClassFixture<RealLights>
{
    private const double LightTolerance = 0.12;
    private const double RequiredShare = 0.97;
    private static readonly long Before = Ms(45);
    private static readonly long After = Ms(5);

    private readonly RealLights lights;
    private readonly ITestOutputHelper output;
    private long revision;

    public LivePatternProbe(RealLights lights, ITestOutputHelper output)
    {
        this.lights = lights;
        this.output = output;
    }

    [LiveDisplayFact]
    public async Task EveryPatternPlaysOnScreenAsTheSequencerSays()
    {
        DesktopCapture.UsePhysicalPixels();
        IReadOnlyList<DisplayInfo> displays = DesktopCapture.Displays();
        var hub = new MusicEventHub();
        var log = new RecordingLog();
        var failures = new List<string>();
        using LightsPresenter presenter = LiveSupport.CreatePresenter(lights, hub, log);
        try
        {
            presenter.Start();
            ThemeDefinition halloween = lights.Theme("Halloween");
            foreach (FlashPatternId pattern in Enum.GetValues<FlashPatternId>().Where(p => p != FlashPatternId.DontFlash))
            {
                // Combination plays each pattern for 40 steps: at the fastest speed all eight pass in 19 s.
                bool combination = pattern == FlashPatternId.Combination;
                LightsScene scene = Show(presenter, lights.Scene(halloween, displays, LayerMode.OnTop, r => r with { Pattern = pattern, Interval = combination ? 1 : 5 }), displays);
                Verify(presenter, scene, displays, captures: combination ? 32 : 14, spacing: combination ? Ms(605) : Ms(173), music: null, failures);
            }

            LightsScene dance = Show(presenter, lights.Scene(lights.Theme("Holiday Party"), displays, LayerMode.OnTop), displays);
            Verify(presenter, dance, displays, captures: 24, spacing: Ms(97), music: new Song(hub), failures);
        }
        finally
        {
            await presenter.ShutdownAsync(fadeOut: false).WaitAsync(TimeSpan.FromSeconds(5));
        }

        LiveSupport.WriteLog(log, output);
        Assert.DoesNotContain(log.Entries, e => e.Level == AppLogLevel.Error);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static long Ms(double milliseconds) => (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000);

    private LightsScene Show(LightsPresenter presenter, LightsScene scene, IReadOnlyList<DisplayInfo> displays)
    {
        scene = scene with { Revision = ++revision };
        presenter.Apply(scene, SceneTransition.None);
        Assert.True(
            LiveSupport.WaitFor(() => presenter.Diagnostics.SceneRevision == scene.Revision && LiveSupport.AllShown(presenter, displays.Count, LayerMode.OnTop), TimeSpan.FromSeconds(20)),
            $"{FlashPatterns.DisplayName(scene.Flash.Pattern)} was not shown: {LiveSupport.Describe(presenter.Status)}");

        // Light bulbs join a continuous pattern over a 600 ms lead-in from what they showed before (the previews jump).
        Thread.Sleep(700);
        return scene;
    }

    /// <summary>Captures the top and bottom strips repeatedly and compares every bulb on them with the sequencer.</summary>
    private void Verify(LightsPresenter presenter, LightsScene scene, IReadOnlyList<DisplayInfo> displays, int captures, long spacing, Song? music, List<string> failures)
    {
        string name = FlashPatterns.DisplayName(scene.Flash.Pattern) + (music is null ? string.Empty : " with music");
        IFlashSequencer sequencer = lights.Flash.CreateSequencer(scene.Layout, lights.Catalog, scene.Flash);
        Sampled[] bulbs = [.. scene.Layout.Placements.Where(p => p.Slot is CellSlot.Top or CellSlot.Bottom || p.IsCorner).Select(p => Prepare(scene, sequencer, p))];
        RectI top = Band(bulbs.Where(b => b.Placement.Bounds.Top == bulbs.Min(o => o.Placement.Bounds.Top)));
        RectI bottom = Band(bulbs.Where(b => b.Placement.Bounds.Bottom == bulbs.Max(o => o.Placement.Bounds.Bottom)));
        var states = new BulbVisualState[scene.Layout.Placements.Count];
        LightsDiagnostics start = presenter.Diagnostics;
        int compared = 0;
        int matched = 0;
        var misses = new List<string>();
        long next = Stopwatch.GetTimestamp() + Ms(50);
        for (int capture = 0; capture < captures; capture++)
        {
            music?.Play(capture, sequencer, presenter.Clock);
            while (Stopwatch.GetTimestamp() < next)
            {
                Thread.Sleep(1);
            }

            long t0 = Stopwatch.GetTimestamp();
            var shots = new[] { ScreenShot.Take(top), ScreenShot.Take(bottom) };
            long t1 = Stopwatch.GetTimestamp();
            next = t1 + spacing;
            StepClock clock = presenter.Clock;

            // What the sequencer allows in the window the capture may show (DWM presents a frame or two after it composes).
            var range = new (float Min, float Max, HashSet<int> Frames)[bulbs.Length];
            for (int i = 0; i < range.Length; i++)
            {
                range[i] = (float.MaxValue, float.MinValue, []);
            }

            for (long at = t0 - Before; at <= t1 + After; at += Ms(5))
            {
                sequencer.Sample(at, clock, states);
                for (int i = 0; i < bulbs.Length; i++)
                {
                    BulbVisualState state = states[bulbs[i].Placement.Ordinal];
                    range[i] = (Math.Min(range[i].Min, state.Brightness), Math.Max(range[i].Max, state.Brightness), range[i].Frames);
                    range[i].Frames.Add(state.Frame);
                }
            }

            for (int i = 0; i < bulbs.Length; i++)
            {
                Sampled bulb = bulbs[i];
                ScreenShot shot = shots[0].Contains(bulb.Placement.Bounds.Left, bulb.Placement.Bounds.Top) ? shots[0] : shots[1];
                compared++;
                string? miss = bulb.Info.Kind == BulbAnimationKind.LightBulb
                    ? CheckLight(shot, bulb, range[i].Min, range[i].Max)
                    : CheckFrame(shot, bulb, range[i].Frames);
                if (miss is null)
                {
                    matched++;
                }
                else if (misses.Count < 8)
                {
                    misses.Add($"capture {capture} step {clock.StepAt(t0)} #{bulb.Placement.Ordinal} {bulb.Placement.BulbId}: {miss}");
                }
            }
        }

        LightsDiagnostics end = presenter.Diagnostics;
        double share = (double)matched / Math.Max(1, compared);
        output.WriteLine(
            $"{name} at {scene.Interval * 60} ms: {matched} of {compared} bulb states on screen as the sequencer says ({share:P2}); "
            + $"{end.Steps - start.Steps} steps processed, {end.Commits - start.Commits} commits, {end.PeriodicLightBulbs} light bulbs on repeating animations."
            + (misses.Count > 0 ? " Differences: " + string.Join("; ", misses) : string.Empty));
        if (share < RequiredShare)
        {
            failures.Add($"{name}: only {share:P2} of the bulb states match.");
        }
    }

    private static string? CheckLight(ScreenShot shot, Sampled bulb, float min, float max)
    {
        double seen = ScreenExpectations.FitBrightness(shot, bulb.Frames[bulb.Info.LitFrame].Image, bulb.Frames[1 - bulb.Info.LitFrame].Image, bulb.Frames[0].Position);
        return seen >= min - LightTolerance && seen <= max + LightTolerance ? null : $"brightness {seen:F2}, sequencer {min:F2}-{max:F2}";
    }

    private static string? CheckFrame(ScreenShot shot, Sampled bulb, HashSet<int> allowed)
    {
        (int best, double difference) = ScreenExpectations.BestFrame(shot, bulb.Frames);
        if (allowed.Contains(best))
        {
            return null;
        }

        // Frames with the same pixels are interchangeable.
        foreach (int frame in allowed)
        {
            (_, double other) = ScreenExpectations.BestFrame(shot, [bulb.Frames[frame]]);
            if (other <= difference + 0.5)
            {
                return null;
            }
        }

        return $"frame {best}, sequencer {string.Join("/", allowed)}";
    }

    private Sampled Prepare(LightsScene scene, IFlashSequencer sequencer, BulbPlacement placement)
    {
        FlashBulbInfo info = sequencer.GetBulb(placement.Ordinal);
        var frames = new List<(PremultipliedImage Image, PointI Position)>();
        for (int frame = 0; frame < Math.Max(1, info.FrameCount); frame++)
        {
            frames.Add(ScreenExpectations.Sprite(lights, scene, placement, frame));
        }

        return new Sampled(placement, info, frames);
    }

    private static RectI Band(IEnumerable<Sampled> bulbs) => bulbs.Select(b => b.Placement.Bounds).Aggregate((a, b) => a.Union(b));

    /// <summary>One bulb on a sampled strip with every frame's sprite.</summary>
    private sealed record Sampled(BulbPlacement Placement, FlashBulbInfo Info, IReadOnlyList<(PremultipliedImage Image, PointI Position)> Frames);

    /// <summary>
    /// A short song published into the music hub, as the music engine does (events stamped with the moment they are heard,
    /// 40 ms after publishing): a start, then per capture a bass drum, melody notes in two pitch classes, a cymbal and a
    /// tempo beat. The sequencer of the test gets the same events.
    /// </summary>
    private sealed class Song(MusicEventHub hub)
    {
        private static readonly long Latency = Ms(40);

        public void Play(int capture, IFlashSequencer sequencer, StepClock clock)
        {
            if (capture == 0)
            {
                // The song starts the animation bulbs from the step the lights show when the presenter reads it (within 30 ms):
                // start early in a step so that the presenter and this test agree on that step.
                while (clock.PositionAt(Stopwatch.GetTimestamp()) % 1 is < 0.15 or > 0.45)
                {
                    Thread.Sleep(1);
                }
            }

            long heard = Stopwatch.GetTimestamp() + Latency;
            var events = new List<MusicEvent>();
            if (capture == 0)
            {
                events.Add(new MusicEvent(MusicEventKind.SongStarted, heard, 0, 0, 0));
            }

            switch (capture % 4)
            {
                case 0:
                    events.Add(new MusicEvent(MusicEventKind.NoteOn, heard, 9, 36, 110));
                    events.Add(new MusicEvent(MusicEventKind.Beat, heard, 0, 0, 127));
                    break;
                case 1:
                    events.Add(new MusicEvent(MusicEventKind.NoteOn, heard, 0, (byte)(60 + (capture % 12)), 100));
                    events.Add(new MusicEvent(MusicEventKind.NoteOn, heard, 1, (byte)(67 + (capture % 12)), 90));
                    break;
                case 2:
                    events.Add(new MusicEvent(MusicEventKind.NoteOn, heard, 9, 49, 120));
                    break;
                default:
                    events.Add(new MusicEvent(MusicEventKind.Beat, heard, 0, 0, 127));
                    break;
            }

            // The presenter's sequencer is at the step it showed last; the test's does the same before reading the batch.
            sequencer.MoveTo(Math.Max(0, clock.StepAt(Stopwatch.GetTimestamp())), Stopwatch.GetTimestamp());
            foreach (MusicEvent e in events)
            {
                hub.Publish(e);
            }

            sequencer.ApplyMusicEvents(events.ToArray());
        }
    }
}
