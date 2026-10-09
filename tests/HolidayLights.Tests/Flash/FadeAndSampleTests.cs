using HolidayLights.Core.Flash;

namespace HolidayLights.Tests.Flash;

/// <summary>Smooth fading (PRODUCT-SPEC 5.8), the flash limit's forced fading (5.5.3) and CPU sampling for previews.</summary>
public sealed class FadeAndSampleTests
{
    [Theory]
    [InlineData(FlashPatternId.FlashTogether, 300, 72, 120)]
    [InlineData(FlashPatternId.Alternating, 60, 14.4, 24)]
    [InlineData(FlashPatternId.RandomFlashing, 540, 90, 150)]
    [InlineData(FlashPatternId.Twinkle, 300, 144, 240)]
    [InlineData(FlashPatternId.ChaseAround, 300, 90, 150)]
    [InlineData(FlashPatternId.SlowGlow, 300, 0, 0)]
    [InlineData(FlashPatternId.DanceToMusic, 300, 0, 0)]
    public void FadeProfile_FollowsThePattern(FlashPatternId pattern, double period, double on, double off)
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions { Pattern = pattern });
        FadeProfile profile = sequencer.GetFadeProfile(period);
        Assert.Equal(on, profile.TurnOnMilliseconds, 9);
        Assert.Equal(off, profile.TurnOffMilliseconds, 9);
    }

    [Fact]
    public void FadeProfile_IsInstantWithoutSmoothFadingUnlessFlashingIsLimited()
    {
        var frame = FlashTestKit.StandardFrame();
        Assert.Equal(FadeProfile.Instant, FlashTestKit.Create(frame, new FlashOptions { SmoothFading = false }).GetFadeProfile(300));
        FadeProfile limited = FlashTestKit.Create(frame, new FlashOptions { SmoothFading = false, LimitFlashing = true }).GetFadeProfile(180);
        Assert.Equal(new FadeProfile(0.6 * 72, 72), limited);
    }

    [Fact]
    public void Sample_RampsLightBulbsLinearlyWithoutMovingAnyStep()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions());
        StepClock clock = StepClock.Start(1_000_000, 5);
        var states = new BulbVisualState[sequencer.Layout.Placements.Count];

        // Step 1 begins at 300 ms: lit to dark over 120 ms.
        sequencer.Sample(1_000_000 + FlashTestKit.Ms(300 + 60), clock, states);
        Assert.Equal(1, sequencer.Step);
        Assert.All(states, s => Assert.Equal(0.5f, s.Brightness, 3));
        Assert.All(states, s => Assert.Equal(s.Brightness, s.Glow));
        sequencer.Sample(1_000_000 + FlashTestKit.Ms(300 + 130), clock, states);
        Assert.All(states, s => Assert.Equal(0f, s.Brightness));

        // Step 2 begins at 600 ms: dark to lit over 72 ms.
        sequencer.Sample(1_000_000 + FlashTestKit.Ms(600 + 36), clock, states);
        Assert.Equal(2, sequencer.Step);
        Assert.All(states, s => Assert.Equal(0.5f, s.Brightness, 3));
        sequencer.Sample(1_000_000 + FlashTestKit.Ms(600 + 72), clock, states);
        Assert.All(states, s => Assert.Equal(1f, s.Brightness));
    }

    [Fact]
    public void Sample_SwitchesInstantlyWithoutSmoothFading()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions { SmoothFading = false });
        StepClock clock = StepClock.Start(0, 5);
        var states = new BulbVisualState[sequencer.Layout.Placements.Count];
        sequencer.Sample(FlashTestKit.Ms(301), clock, states);
        Assert.All(states, s => Assert.Equal(new BulbVisualState(1, 0f, 0f), s));
    }

    [Fact]
    public void Sample_BeforeTheClockStartsShowsStepZeroAndRejectsShortBuffers()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions());
        var states = new BulbVisualState[sequencer.Layout.Placements.Count];
        sequencer.Sample(0, StepClock.Start(FlashTestKit.Ms(1000), 5), states);
        Assert.Equal(0, sequencer.Step);
        Assert.All(states, s => Assert.Equal(1f, s.Brightness));
        Assert.Throws<ArgumentException>(() => sequencer.Sample(0, StepClock.Start(0, 5), new BulbVisualState[3]));
        Assert.Throws<ArgumentNullException>(() => sequencer.Sample(0, null!, states));
    }

    [Fact]
    public void Sample_UsesThePeriodOfTheStepAfterASpeedChange()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions());
        StepClock clock = StepClock.Start(0, 5).WithInterval(1, FlashTestKit.Ms(100)); // 60 ms steps from step 1 (300 ms)
        var states = new BulbVisualState[sequencer.Layout.Placements.Count];

        // Step 1 at the fastest speed: d = 24 ms, so 12 ms into the step a turning-off bulb is half lit.
        sequencer.Sample(FlashTestKit.Ms(300 + 12), clock, states);
        Assert.Equal(1, sequencer.Step);
        Assert.All(states, s => Assert.Equal(0.5f, s.Brightness, 3));
    }

    [Fact]
    public void MoveTo_JumpsAndReturnsWithTheSameStatesAsStepping()
    {
        foreach (FlashPatternId pattern in Enum.GetValues<FlashPatternId>())
        {
            var frame = FlashTestKit.MixedFrame();
            var options = new FlashOptions { Pattern = pattern, PatternSeed = 99, ClassicRandomSeed = 5 };
            IFlashSequencer stepping = FlashTestKit.Create(frame, options);
            var history = new List<BulbVisualState[]>();
            for (int step = 0; step <= 400; step++)
            {
                stepping.MoveTo(step, FlashTestKit.Ms(step * 300));
                history.Add(FlashTestKit.Snapshot(stepping));
            }

            IFlashSequencer jumping = FlashTestKit.Create(frame, options);
            foreach (int step in new[] { 399, 17, 0, 250, 251, 3, 400, 120 })
            {
                jumping.MoveTo(step, FlashTestKit.Ms(step * 300));
                Assert.True(history[step].AsSpan().SequenceEqual(jumping.Current), $"{pattern} at step {step}");
            }
        }
    }
}
