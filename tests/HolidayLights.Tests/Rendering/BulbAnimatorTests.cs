using System.Diagnostics;
using HolidayLights.Rendering.Animation;
using HolidayLights.Rendering.Composition;
using HolidayLights.Tests.Rendering.Fakes;

namespace HolidayLights.Tests.Rendering;

public sealed class BulbAnimatorTests
{
    private const long T0 = 5_000_000_000;
    private const string StaticCorners = "builtin:test-static";
    private static readonly WaveTiming Timing300 = new(T0, 0, 300);

    private static long At(double milliseconds) => T0 + (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000);

    private static (BulbAnimator Animator, FakeSequencer Sequencer, LightsLayout Layout) Create(
        FlashPatternId pattern, bool smooth = true, string? corner = "builtin:test-animated", int stripFrames = 2)
    {
        LightsLayout layout = SyntheticScene.Layout(corner);
        var sequencer = new FakeSequencer(layout, SyntheticScene.Resolver, SyntheticScene.Options(pattern, smooth), stripFrames);
        return (new BulbAnimator(sequencer, 0.55f), sequencer, layout);
    }

    private static IEnumerable<int> All(LightsLayout layout) => Enumerable.Range(0, layout.Placements.Count);

    private static IEnumerable<int> LightOrdinals(LightsLayout layout) => layout.Placements.Where(p => !p.IsCorner).Select(p => p.Ordinal);

    private static IEnumerable<int> CornerOrdinals(LightsLayout layout) => layout.Placements.Where(p => p.IsCorner).Select(p => p.Ordinal);

    /// <summary>Shows the start state and starts the pattern at step 0, as the engine does.</summary>
    private static RecordingVisualTarget Start(BulbAnimator animator, LightsLayout layout)
    {
        var target = new RecordingVisualTarget();
        animator.ApplyFullState(All(layout), T0, Timing300, target);
        animator.Step(0, T0, Timing300, target);
        return target;
    }

    [Fact]
    public void FlashTogether_BindsOneSharedRepeatingAnimationOfThePeriodOnce()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether, corner: StaticCorners);
        Assert.Equal(LightOrdinals(layout).Count(), animator.PeriodicLightBulbs);
        Assert.True(animator.NeedsSteps);

        RecordingVisualTarget target = Start(animator, layout);
        int[] lights = [.. LightOrdinals(layout)];
        OpacityCurve period = target.Lit[lights[0]];
        Assert.All(lights, o => Assert.Same(period, target.Lit[o]));
        Assert.True(period.Repeats);
        Assert.Equal(0.6, period.RepeatSeconds, 9);
        Assert.False(animator.NeedsSteps);

        // Lit in even steps; a 120 ms fade out at odd boundaries, a 72 ms fade in at even ones; the same every 600 ms.
        foreach (double cycle in new[] { 0.0, 600, 60_000 })
        {
            Assert.Equal(1, period.Evaluate(At(cycle + 200)), 6);
            Assert.Equal(0.5, period.Evaluate(At(cycle + 360)), 6);
            Assert.Equal(0, period.Evaluate(At(cycle + 500)), 6);
            Assert.Equal(0.5, period.Evaluate(At(cycle + 636)), 6);
            Assert.Equal(1, period.Evaluate(At(cycle + 700)), 6);
        }

        Assert.Equal(0.55 * 0.5, target.Glow[lights[0]].Evaluate(At(360)), 5);

        target.Clear();
        animator.Step(1, At(300), Timing300, target);
        animator.Step(2, At(600), Timing300, target);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void WithoutSmoothFading_ThePeriodSwitchesInstantly()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether, smooth: false);
        RecordingVisualTarget target = Start(animator, layout);

        Assert.All(LightOrdinals(layout), o =>
        {
            OpacityCurve curve = target.Lit[o];
            Assert.Equal(1, curve.Evaluate(At(299)), 6);
            Assert.Equal(0, curve.Evaluate(At(301)), 6);
            Assert.Equal(0, curve.Evaluate(At(599)), 6);
            Assert.Equal(1, curve.Evaluate(At(601)), 6);
            Assert.Equal(0, curve.Evaluate(At(30_301)), 6);
        });
    }

    [Fact]
    public void ClassicPatternWithoutARepeatingPeriod_FadesAtEveryStepWithOneSharedCurvePerDirection()
    {
        // The strips claim one frame, but the brightness alternates: the samples do not repeat, so steps drive the bulbs.
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether, stripFrames: 1);
        Assert.Equal(0, animator.PeriodicLightBulbs);
        var target = new RecordingVisualTarget();
        animator.ApplyFullState(All(layout), T0, Timing300, target);
        Assert.All(LightOrdinals(layout), o => Assert.Equal(1f, target.Lit[o].EndValue));

        target.Clear();
        animator.Step(1, At(300), Timing300, target);

        int[] lights = [.. LightOrdinals(layout)];
        OpacityCurve off = target.Lit[lights[0]];
        Assert.All(lights, o => Assert.Same(off, target.Lit[o]));
        Assert.Equal(0.120, off.TerminalOffsetSeconds, 9);
        Assert.Equal(1.0, off.Evaluate(At(300)), 6);
        Assert.Equal(0.0, off.Evaluate(At(420)), 6);
        Assert.Equal(0.55 * 0.5, target.Glow[lights[0]].Evaluate(At(360)), 5);

        target.Clear();
        animator.Step(2, At(600), Timing300, target);
        OpacityCurve on = target.Lit[lights[0]];
        Assert.Equal(0.072, on.TerminalOffsetSeconds, 9);
        Assert.Equal(1.0, on.Evaluate(At(672)), 6);
    }

    [Fact]
    public void LearningThePeriods_LeavesTheSequencerAtItsStep()
    {
        LightsLayout layout = SyntheticScene.Layout();
        var sequencer = new FakeSequencer(layout, SyntheticScene.Resolver, SyntheticScene.Options(FlashPatternId.Alternating));
        sequencer.MoveTo(5, T0);
        BulbVisualState[] before = sequencer.Current.ToArray();

        var animator = new BulbAnimator(sequencer, 0.55f);

        Assert.Equal(5, sequencer.Step);
        Assert.Equal(before, sequencer.Current.ToArray());
        Assert.Equal(LightOrdinals(layout).Count(), animator.PeriodicLightBulbs);
    }

    [Fact]
    public void ClassicPattern_SpeedChangeReanchorsThePeriodAtTheBoundary()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether, corner: StaticCorners);
        RecordingVisualTarget target = Start(animator, layout);
        int light = LightOrdinals(layout).First();
        OpacityCurve slow = target.Lit[light];

        // From step 3 (at 900 ms) a step lasts 60 ms, and the fades shrink to 24 ms (off) and 14.4 ms (on).
        animator.ChangeSpeed(new WaveTiming(At(900), 3, 60), target);
        OpacityCurve fast = target.Lit[light];

        Assert.NotSame(slow, fast);
        Assert.Equal(1, fast.Evaluate(At(899)), 6);
        Assert.Equal(0.5, fast.Evaluate(At(912)), 6);
        Assert.Equal(0, fast.Evaluate(At(950)), 6);
        Assert.Equal(0.5, fast.Evaluate(At(967.2)), 6);
        Assert.Equal(1, fast.Evaluate(At(900 + (60 * 101) + 40)), 6);
        Assert.All(LightOrdinals(layout), o => Assert.Same(fast, target.Lit[o]));
    }

    [Fact]
    public void ApplyFullState_OnceThePatternRuns_JoinsThePeriodInPhase()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether, corner: StaticCorners);
        RecordingVisualTarget target = Start(animator, layout);
        target.Clear();

        // A display shown again during step 3 (900-1200 ms, unlit) shows that step at once, then the period goes on.
        animator.ApplyFullState(LightOrdinals(layout), At(1_000), Timing300, target);
        OpacityCurve curve = target.Lit[LightOrdinals(layout).First()];

        Assert.True(curve.Repeats);
        Assert.Equal(0, curve.Evaluate(At(1_000)), 6);
        Assert.Equal(0.5, curve.Evaluate(At(1_236)), 6);
        Assert.Equal(1, curve.Evaluate(At(1_400)), 6);
        Assert.False(animator.NeedsSteps);
    }

    [Fact]
    public void RefreshRepeating_CreatesTheSamePictureAgain()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether, corner: StaticCorners);
        RecordingVisualTarget target = Start(animator, layout);
        int light = LightOrdinals(layout).First();
        OpacityCurve original = target.Lit[light];
        target.Clear();

        // Half an hour later, 40 ms into the fade-out of step 6001.
        animator.RefreshRepeating(At(1_800_340), Timing300, target);

        OpacityCurve refreshed = target.Lit[light];
        Assert.NotSame(original, refreshed);
        Assert.All(LightOrdinals(layout), o => Assert.Same(refreshed, target.Lit[o]));
        foreach (double at in new[] { 1_800_340.0, 1_800_400, 1_800_650, 1_801_000, 1_900_123 })
        {
            Assert.Equal(original.Evaluate(At(at)), refreshed.Evaluate(At(at)), 5);
        }
    }

    [Fact]
    public void AnimationBulbs_ShowTheirFrameOnlyWhenItChanges()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether);
        var target = new RecordingVisualTarget();
        animator.ApplyFullState(All(layout), T0, Timing300, target);
        Assert.All(CornerOrdinals(layout), o => Assert.Equal(0, target.Frames[o]));

        target.Clear();
        animator.Step(1, At(300), Timing300, target);
        Assert.All(CornerOrdinals(layout), o => Assert.Equal(1, target.Frames[o]));
        Assert.DoesNotContain(target.Calls, c => c.StartsWith("frame", StringComparison.Ordinal) && !CornerOrdinals(layout).Any(o => c == $"frame {o} 1"));
    }

    [Fact]
    public void StaticBulbsNeverChange()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether, corner: StaticCorners);
        var target = new RecordingVisualTarget();
        animator.ApplyFullState(All(layout), T0, Timing300, target);
        target.Clear();

        for (int step = 1; step < 6; step++)
        {
            animator.Step(step, At(300 * step), Timing300, target);
        }

        Assert.DoesNotContain(target.Calls, c => c.StartsWith("frame", StringComparison.Ordinal));
    }

    [Fact]
    public void SlowGlow_BindsOneSharedWaveOnceAndReanchorsItOnASpeedChange()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.SlowGlow);
        var target = new RecordingVisualTarget();
        animator.ApplyFullState(All(layout), T0, Timing300, target);
        int[] lights = [.. LightOrdinals(layout)];
        OpacityCurve wave = target.Lit[lights[0]];
        Assert.True(wave.Repeats);
        Assert.All(lights, o => Assert.Same(wave, target.Lit[o]));
        Assert.Equal(0, animator.PeriodicLightBulbs);

        target.Clear();
        animator.Step(1, At(300), Timing300, target);
        animator.Step(2, At(600), Timing300, target);
        Assert.DoesNotContain(target.Calls, c => c.StartsWith("lit", StringComparison.Ordinal));

        var faster = new WaveTiming(At(900), 3, 60);
        animator.ChangeSpeed(faster, target);
        OpacityCurve rebound = target.Lit[lights[0]];
        Assert.NotSame(wave, rebound);
        Assert.InRange(rebound.Evaluate(At(900)) - wave.Evaluate(At(900)), -0.01, 0.01);
        Assert.InRange(rebound.Evaluate(At(900 + 60 * 8)) - new BrightnessWave(16, 0).Evaluate(11), -0.01, 0.01);
    }

    [Fact]
    public void Dance_GivesEachPitchClassItsOwnEnvelope()
    {
        (BulbAnimator animator, FakeSequencer sequencer, LightsLayout layout) = Create(FlashPatternId.DanceToMusic, corner: StaticCorners);
        var target = new RecordingVisualTarget();
        animator.ApplyFullState(All(layout), T0, Timing300, target);
        Assert.True(target.Lit[LightOrdinals(layout).First()].Repeats);

        target.Clear();
        animator.ApplyMusic([new MusicEvent(MusicEventKind.NoteOn, At(1_040), 0, Note: 64, Velocity: 127)], At(1_000), Timing300, target);

        int[] group4 = [.. layout.Placements.Where(p => sequencer.GetBulb(p.Ordinal).MusicGroup == 4).Select(p => p.Ordinal)];
        Assert.NotEmpty(group4);
        OpacityCurve envelope = target.Lit[group4[0]];
        Assert.All(group4, o => Assert.Same(envelope, target.Lit[o]));
        Assert.InRange(envelope.Evaluate(At(1_070)) - 1.0, -0.01, 0.01);
        Assert.InRange(envelope.Evaluate(At(1_070 + 250)) - Math.Exp(-1), -0.01, 0.01);

        // The other light bulbs left the idle wave (mode change) but got no envelope of their own.
        int other = LightOrdinals(layout).First(o => sequencer.GetBulb(o).MusicGroup != 4);
        Assert.False(target.Lit.TryGetValue(other, out OpacityCurve? otherCurve) && ReferenceEquals(otherCurve, envelope));
        Assert.True(sequencer.IsDancing);
    }

    [Fact]
    public void PowerUp_LightsEachBulbWhenTheWaveReachesIt()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether);
        PowerUpPlan plan = PowerUpPlan.Create(SceneTransition.FirstRunPowerUp, reducedMotion: false, layout);
        var target = new RecordingVisualTarget();
        animator.ApplyPowerUp(plan, T0, delayMilliseconds: 0, All(layout), target);

        foreach (int ordinal in LightOrdinals(layout))
        {
            double fullAt = plan.FullBrightnessAt(ordinal);
            Assert.Equal(0, target.Lit[ordinal].Evaluate(At(fullAt - 121)), 6);
            Assert.Equal(1, target.Lit[ordinal].Evaluate(At(fullAt)), 6);
            Assert.Equal(0.55, target.Glow[ordinal].Evaluate(At(fullAt + 1)), 5);
        }

        foreach (int ordinal in CornerOrdinals(layout))
        {
            Assert.Equal(0.4, target.Opacity[ordinal].Evaluate(T0), 6);
            Assert.Equal(1, target.Opacity[ordinal].Evaluate(At(plan.FullBrightnessAt(ordinal))), 6);
            Assert.Equal(0, target.Frames[ordinal]);
        }
    }

    [Fact]
    public void DuringThePowerUp_ADisplayShownAgainShowsTheSequencerStateNotThePeriod()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether, corner: StaticCorners);
        Start(animator, layout);
        PowerUpPlan plan = PowerUpPlan.Create(SceneTransition.ShortPowerUp, reducedMotion: false, layout);
        var target = new RecordingVisualTarget();
        animator.ApplyPowerUp(plan, At(1_000), delayMilliseconds: 0, All(layout), target);

        animator.ApplyFullState(LightOrdinals(layout), At(1_100), Timing300, target);

        Assert.All(LightOrdinals(layout), o => Assert.True(target.Lit[o].IsConstant));
        Assert.True(animator.NeedsSteps);
    }

    [Fact]
    public void AfterThePowerUp_StepZeroStartsEachClassFromLit()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.Alternating, corner: StaticCorners);
        var target = new RecordingVisualTarget();
        animator.AssumeAllLit();
        animator.Step(0, T0, Timing300, target);

        // Alternating: two classes (every other bulb of the ring), both starting from lit; the class unlit at step 0 fades out.
        OpacityCurve[] curves = [.. LightOrdinals(layout).Select(o => target.Lit[o]).Distinct()];
        Assert.Equal(2, curves.Length);
        Assert.All(curves, curve =>
        {
            Assert.True(curve.Repeats);
            Assert.Equal(1, curve.Evaluate(T0), 6);
        });
        Assert.Equal(new[] { 0.0, 1.0 }, curves.Select(c => Math.Round(c.Evaluate(At(200)), 6)).Order());
        Assert.Equal(new[] { 0.0, 1.0 }, curves.Select(c => Math.Round(c.Evaluate(At(500)), 6)).Order());
        Assert.False(animator.NeedsSteps);
    }

    [Fact]
    public void SeededVisuals_FadeFromWhatTheyShow()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.DontFlash);
        var target = new RecordingVisualTarget();
        animator.SeedShown(_ => new BulbVisualState(1, 0f, 0f));
        animator.Step(0, T0, Timing300, target);

        int light = LightOrdinals(layout).First();
        Assert.Equal(0, target.Lit[light].Evaluate(T0), 6);
        Assert.Equal(1, target.Lit[light].Evaluate(At(500)), 6);
        Assert.False(target.Lit[light].IsConstant);
    }

    [Fact]
    public void SeededVisuals_GetTheirOwnCurveEvenWhenTheirStateStaysTheSame()
    {
        // The visuals may still run the previous pattern's repeating animation: taking them over must replace it, also for
        // bulbs whose new state equals the value they happened to show (the live check of Twinkle after Random Flashing).
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.DontFlash, corner: StaticCorners);
        var target = new RecordingVisualTarget();
        animator.SeedShown(_ => new BulbVisualState(0, 1f, 1f));
        animator.Step(0, T0, Timing300, target);

        Assert.All(LightOrdinals(layout), o =>
        {
            Assert.True(target.Lit[o].IsConstant);
            Assert.Equal(1f, target.Lit[o].EndValue);
            Assert.Equal(0.55f, target.Glow[o].EndValue, 5);
        });

        target.Clear();
        animator.Step(1, At(300), Timing300, target);
        Assert.Empty(target.Calls);
    }

    [Fact]
    public void NeedsSteps_IsFalseWhenNothingChangesAtSteps()
    {
        Assert.False(Create(FlashPatternId.DontFlash).Animator.NeedsSteps);
        Assert.True(Create(FlashPatternId.FlashTogether).Animator.NeedsSteps);
        Assert.True(Create(FlashPatternId.ChaseAround, corner: StaticCorners).Animator.NeedsSteps);

        // Light bulbs of a classic pattern need steps only until their period is bound.
        (BulbAnimator flashing, _, LightsLayout layout) = Create(FlashPatternId.FlashTogether, corner: StaticCorners);
        Assert.True(flashing.NeedsSteps);
        Start(flashing, layout);
        Assert.False(flashing.NeedsSteps);
        flashing.AssumeAllLit();
        Assert.True(flashing.NeedsSteps);

        LightsLayout still = SyntheticScene.Layout(corner: StaticCorners, side: StaticCorners);
        var sequencer = new FakeSequencer(still, SyntheticScene.Resolver, SyntheticScene.Options(FlashPatternId.FlashTogether));
        Assert.False(new BulbAnimator(sequencer, 0.55f).NeedsSteps);

        var stopped = new FakeSequencer(SyntheticScene.Layout(), SyntheticScene.Resolver, SyntheticScene.Options(FlashPatternId.FlashTogether) with { StopFlashing = true });
        Assert.False(new BulbAnimator(stopped, 0.55f).NeedsSteps);
    }

    [Fact]
    public void GlowIntensityChange_ReappliesTheGlow()
    {
        (BulbAnimator animator, _, LightsLayout layout) = Create(FlashPatternId.DontFlash);
        var target = new RecordingVisualTarget();
        animator.ApplyFullState(All(layout), T0, Timing300, target);
        int light = LightOrdinals(layout).First();
        Assert.Equal(0.55f, target.Glow[light].EndValue, 5);

        animator.SetGlowIntensity(1f, T0, Timing300, target);
        Assert.Equal(1f, target.Glow[light].EndValue, 5);
    }
}
