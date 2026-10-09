using System.Diagnostics;
using HolidayLights.Rendering.Animation;
using HolidayLights.Rendering.Composition;
using HolidayLights.Tests.Rendering.Fakes;

namespace HolidayLights.Tests.Rendering.RealCore;

/// <summary>
/// The desktop shows what the previews show (PRODUCT-SPEC 5.5.2, 5.6-5.10): with the real flash engine, the opacity curves
/// and frames the animator gives the DirectComposition visuals, evaluated at many moments, equal the states that
/// <see cref="IFlashSequencer.Sample"/> (the CPU previews' path) computes for the same layout, options and clock. The engine's
/// own rule decides which steps are processed (only while <see cref="BulbAnimator.NeedsSteps"/>), so the repeating
/// animations of the classic patterns must stand on their own.
/// </summary>
public sealed class PatternEquivalenceTests : IClassFixture<RealLights>
{
    private const long T0 = 10_000_000_000;
    private const float GlowIntensity = 0.55f;
    private const double BrightnessTolerance = 0.02;

    /// <summary>Moments within each step, in fractions of the step: right after the boundary, during the fades, settled.</summary>
    private static readonly double[] Offsets = [0.02, 0.15, 0.3, 0.5, 0.75, 0.97];

    private readonly RealLights lights;

    public PatternEquivalenceTests(RealLights lights) => this.lights = lights;

    public static TheoryData<FlashPatternId, bool> EveryPattern()
    {
        var data = new TheoryData<FlashPatternId, bool>();
        foreach (FlashPatternId pattern in Enum.GetValues<FlashPatternId>())
        {
            data.Add(pattern, true);
            data.Add(pattern, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPattern))]
    public void TheDesktopShowsWhatThePreviewsShow(FlashPatternId pattern, bool smoothFading)
    {
        // Halloween has light bulbs (Ghosts, Autumn Leaves corners), animations (Jack-O-Lanterns, The Grim Reaper) and statics.
        LightsScene scene = lights.Scene(lights.Theme("Halloween"), ReferencePc.Displays, LayerMode.BehindIcons, r => r with
        {
            Pattern = pattern,
            SmoothFading = smoothFading,
        });
        int steps = pattern == FlashPatternId.Combination ? 330 : 48;

        Mismatches mismatches = Run(scene, StepClock.Start(T0, scene.Interval), steps, speedChange: null);

        mismatches.AssertNone();
    }

    [Theory]
    [InlineData(FlashPatternId.FlashTogether)]
    [InlineData(FlashPatternId.BulbChase)]
    [InlineData(FlashPatternId.RandomFlashing)]
    [InlineData(FlashPatternId.Twinkle)]
    [InlineData(FlashPatternId.SlowGlow)]
    [InlineData(FlashPatternId.ChaseAround)]
    public void ASpeedChangeKeepsTheDesktopAndThePreviewsTogether(FlashPatternId pattern)
    {
        LightsScene scene = lights.Scene(lights.Theme("Christmas 1"), ReferencePc.Displays, LayerMode.BehindIcons, r => r with { Pattern = pattern });

        Mismatches mismatches = Run(scene, StepClock.Start(T0, scene.Interval), steps: 60, speedChange: (AtStep: 23, Interval: 2));

        mismatches.AssertNone();
    }

    [Theory]
    [InlineData(FlashPatternId.FlashTogether)]
    [InlineData(FlashPatternId.Twinkle)]
    [InlineData(FlashPatternId.ChaseAround)]
    [InlineData(FlashPatternId.SlowGlow)]
    public void LimitFlashing_ForcesTheSameFadesOnTheDesktopAsInThePreviews(FlashPatternId pattern)
    {
        // "Limit Flashing to 3 Flashes per Second" (PRODUCT-SPEC 5.5.3): interval at least 3 and Smooth Fading forced on.
        LightsScene scene = lights.Scene(lights.Theme("Halloween"), ReferencePc.Displays, LayerMode.BehindIcons, r => r with
        {
            Pattern = pattern,
            SmoothFading = false,
            LimitFlashing = true,
            Interval = 1,
        });
        Assert.Equal(3, scene.Interval);

        Mismatches mismatches = Run(scene, StepClock.Start(T0, scene.Interval), steps: 60, speedChange: null);

        mismatches.AssertNone();
    }

    [Fact]
    public void StopFlashing_ShowsEveryBulbLitWithoutChanges()
    {
        LightsScene scene = lights.Scene(lights.Theme("Halloween"), ReferencePc.Displays, LayerMode.BehindIcons, r => r with { StopFlashing = true });

        Mismatches mismatches = Run(scene, StepClock.Start(T0, scene.Interval), steps: 12, speedChange: null);

        mismatches.AssertNone();
        IFlashSequencer sequencer = lights.Flash.CreateSequencer(scene.Layout, lights.Catalog, scene.Flash);
        Assert.False(new BulbAnimator(sequencer, GlowIntensity).NeedsSteps);
    }

    [Fact]
    public void FlashTogether_OnTheDefaultArrangement_NeedsNoWorkPerStep()
    {
        // Christmas 1 (the 5.4 default look): Standard Bulbs are light bulbs, Snow Family and Jolly Holly are static.
        LightsScene scene = lights.Scene(lights.Theme("Christmas 1"), ReferencePc.Displays, LayerMode.BehindIcons);
        IFlashSequencer sequencer = lights.Flash.CreateSequencer(scene.Layout, lights.Catalog, scene.Flash);
        int lightBulbs = Enumerable.Range(0, scene.Layout.Placements.Count).Count(o => sequencer.GetBulb(o).Kind == BulbAnimationKind.LightBulb);

        var animator = new BulbAnimator(sequencer, GlowIntensity);
        var target = new RecordingVisualTarget();
        WaveTiming timing = WaveTiming.At(StepClock.Start(T0, scene.Interval), T0);
        animator.ApplyFullState(Enumerable.Range(0, scene.Layout.Placements.Count), T0, timing, target);
        animator.Step(0, T0, timing, target);

        Assert.True(lightBulbs > 100);
        Assert.Equal(lightBulbs, animator.PeriodicLightBulbs);
        Assert.False(animator.NeedsSteps);
    }

    [Fact]
    public void DanceToTheMusic_FollowsTheSameNotesAsThePreviews()
    {
        LightsScene scene = lights.Scene(lights.Theme("Holiday Party"), ReferencePc.Displays, LayerMode.BehindIcons);
        Assert.Equal(FlashPatternId.DanceToMusic, scene.Flash.Pattern);
        StepClock clock = StepClock.Start(T0, scene.Interval);
        IFlashSequencer desktop = lights.Flash.CreateSequencer(scene.Layout, lights.Catalog, scene.Flash);
        IFlashSequencer preview = lights.Flash.CreateSequencer(scene.Layout, lights.Catalog, scene.Flash);
        var animator = new BulbAnimator(desktop, GlowIntensity);
        var target = new RecordingVisualTarget();
        WaveTiming timing = WaveTiming.At(clock, T0);
        int count = scene.Layout.Placements.Count;
        animator.ApplyFullState(Enumerable.Range(0, count), T0, timing, target);
        animator.Step(0, T0, timing, target);

        // A song: a start, a bass drum (every group), a melody running through the pitch classes, a cymbal (the corners),
        // tempo-map beats; then the song stops and the lights glow slowly again. Events carry the moment they are heard
        // (40 ms after they are read), and the Lights thread reads them in 30 ms batches.
        var song = new List<MusicEvent> { new(MusicEventKind.SongStarted, At(1_000), 0, 0, 0), new(MusicEventKind.NoteOn, At(1_100), 9, 36, 120) };
        for (int i = 0; i < 24; i++)
        {
            song.Add(new MusicEvent(MusicEventKind.NoteOn, At(1_300 + (i * 110)), 0, (byte)(60 + i), (byte)(70 + i)));
            if (i % 4 == 0)
            {
                song.Add(new MusicEvent(MusicEventKind.Beat, At(1_300 + (i * 110)), 0, 0, 127));
            }
        }

        song.Add(new MusicEvent(MusicEventKind.NoteOn, At(4_000), 9, 49, 100));
        long stopped = At(6_000);
        song.Add(new MusicEvent(MusicEventKind.SongStopped, stopped, 0, 0, 0));
        song.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        var mismatches = new Mismatches(scene.Flash.Pattern, smooth: true);
        var states = new BulbVisualState[count];
        long next = T0;
        long end = At(9_000);
        int read = 0;
        long lastStep = 0;
        while (next < end)
        {
            // The engine: steps at their boundaries while needed, music every 30 ms (events become readable 40 ms early).
            long step = clock.StepAt(next);
            if (step > lastStep && animator.NeedsSteps)
            {
                animator.Step(step, clock.TimestampOfStep(step), timing, target);
            }

            lastStep = Math.Max(lastStep, step);
            long readable = next + Ms(40);
            int from = read;
            while (read < song.Count && song[read].Timestamp <= readable)
            {
                read++;
            }

            if (read > from)
            {
                ReadOnlySpan<MusicEvent> batch = song.ToArray().AsSpan(from, read - from);
                animator.ApplyMusic(batch, next, timing, target);
                preview.ApplyMusicEvents(batch);
            }

            foreach (double offset in new[] { 3.0, 17.0 })
            {
                // Between reading an event and hearing it, the previews hold the level the group will have when it is heard,
                // while the desktop moves there linearly from the level it shows: compare once every read event is heard.
                // When the song stops, the desktop eases into the idle Slow Glow over 600 ms where the previews jump.
                long at = next + Ms(offset);
                if ((read > 0 && song[read - 1].Timestamp > at) || (at >= stopped && at < stopped + Ms(600)))
                {
                    continue;
                }

                preview.Sample(at, clock, states);
                mismatches.Compare(target, states, at, GlowIntensity, Enumerable.Range(0, count), desktop, tolerance: 0.03);
            }

            next += Ms(DanceEnvelope.BatchMilliseconds);
        }

        mismatches.AssertNone();
    }

    private static long Ms(double milliseconds) => (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000);

    /// <summary>
    /// Combination (PRODUCT-SPEC 5.7) switches pattern every 40 steps. Where a segment of Slow Glow (the 6th) or Waves (the 8th)
    /// begins, the desktop eases each light bulb from what it shows into the wave over 600 ms, where the previews jump; in the
    /// first step after such a segment the desktop fades from the wave's value at the boundary, the previews from its value
    /// at the start of the previous step. Outside these moments both show the same.
    /// </summary>
    private static bool InWaveLeadIn(FlashPatternId pattern, StepClock clock, long at)
    {
        const int StepsPerSegment = 40;
        const int Segments = 8;
        if (pattern != FlashPatternId.Combination)
        {
            return false;
        }

        long step = clock.StepAt(at);
        long segmentStart = step / StepsPerSegment * StepsPerSegment;
        long segment = step / StepsPerSegment % Segments;
        bool intoWave = segment is 5 or 7 && at - clock.TimestampOfStep(segmentStart) < Ms(600);
        bool outOfWave = segment is 6 or 0 && segmentStart > 0 && step == segmentStart;
        return intoWave || outOfWave;
    }

    private static long At(double milliseconds) => T0 + Ms(milliseconds);

    /// <summary>
    /// Runs the engine's step rule against a second sequencer that samples like a preview, optionally with a speed change
    /// that the engine applies at the boundary where it takes effect.
    /// </summary>
    private Mismatches Run(LightsScene scene, StepClock clock, int steps, (int AtStep, int Interval)? speedChange)
    {
        IFlashSequencer desktop = lights.Flash.CreateSequencer(scene.Layout, lights.Catalog, scene.Flash);
        IFlashSequencer preview = lights.Flash.CreateSequencer(scene.Layout, lights.Catalog, scene.Flash);
        var animator = new BulbAnimator(desktop, GlowIntensity);
        var target = new RecordingVisualTarget();
        int count = scene.Layout.Placements.Count;
        int[] all = [.. Enumerable.Range(0, count)];
        WaveTiming timing = WaveTiming.At(clock, T0);
        animator.ApplyFullState(all, T0, timing, target);
        animator.Step(0, T0, timing, target);

        var mismatches = new Mismatches(scene.Flash.Pattern, scene.Flash.SmoothFading);
        var states = new BulbVisualState[count];
        for (long step = 0; step < steps; step++)
        {
            if (speedChange is { } change && step == change.AtStep - 1)
            {
                // Requested in the middle of the previous step; effective at this boundary.
                clock = clock.WithInterval(change.Interval, (clock.TimestampOfStep(step) + clock.TimestampOfStep(step + 1)) / 2);
            }

            long boundary = clock.TimestampOfStep(step);
            if (speedChange is { } effective && step == effective.AtStep)
            {
                timing = WaveTiming.At(clock, boundary);
                animator.ChangeSpeed(timing, target);
            }

            if (step > 0 && animator.NeedsSteps)
            {
                animator.Step(step, boundary, timing, target);
            }

            long length = clock.TimestampOfStep(step + 1) - boundary;
            foreach (double offset in Offsets)
            {
                long at = boundary + (long)(length * offset);
                if (InWaveLeadIn(scene.Flash.Pattern, clock, at))
                {
                    continue;
                }

                preview.Sample(at, clock, states);
                mismatches.Compare(target, states, at, GlowIntensity, all, desktop, BrightnessTolerance);
            }
        }

        return mismatches;
    }

    /// <summary>Collects the differences between the visuals and the sampled states.</summary>
    private sealed class Mismatches(FlashPatternId pattern, bool smooth)
    {
        private readonly List<string> found = [];
        private int compared;

        public void Compare(RecordingVisualTarget target, BulbVisualState[] states, long at, float glowIntensity, IEnumerable<int> ordinals, IFlashSequencer facts, double tolerance)
        {
            foreach (int ordinal in ordinals)
            {
                compared++;
                FlashBulbInfo bulb = facts.GetBulb(ordinal);
                BulbVisualState expected = states[ordinal];
                if (bulb.Kind != BulbAnimationKind.LightBulb)
                {
                    int shown = target.Frames.TryGetValue(ordinal, out int frame) ? frame : -1;
                    if (shown != expected.Frame)
                    {
                        Add($"t={Time(at)} ordinal {ordinal} ({bulb.Kind}): frame {shown}, previews {expected.Frame}");
                    }

                    continue;
                }

                double lit = target.Lit[ordinal].Evaluate(at);
                double glow = target.Glow[ordinal].Evaluate(at);
                if (Math.Abs(lit - expected.Brightness) > tolerance || Math.Abs(glow - (expected.Glow * glowIntensity)) > tolerance)
                {
                    Add($"t={Time(at)} ordinal {ordinal}: lit {lit:F3} glow {glow:F3}, previews {expected.Brightness:F3} / {expected.Glow * glowIntensity:F3}");
                }
            }
        }

        public void AssertNone() =>
            Assert.True(
                found.Count == 0,
                $"{FlashPatterns.DisplayName(pattern)} (smooth {smooth}): {found.Count} of {compared} comparisons differ, e.g.{Environment.NewLine}"
                + string.Join(Environment.NewLine, found.Take(12)));

        private static string Time(long at) => $"{(at - T0) * 1000.0 / Stopwatch.Frequency:F1} ms";

        private void Add(string message) => found.Add(message);
    }
}
