using HolidayLights.Core.Flash;

namespace HolidayLights.Tests.Flash;

/// <summary>Slow Glow, Chase Around the Screen, Waves and Combination (PRODUCT-SPEC 5.7, 5.8).</summary>
public sealed class NewPatternTests
{
    [Fact]
    public void SlowGlow_BreathesEveryLightBulbOverSixteenSteps()
    {
        var frame = FlashTestKit.MixedFrame();
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.SlowGlow });
        int light = FlashTestKit.LightBulbsInRingOrder(sequencer)[0];
        BrightnessWave? wave = sequencer.GetWave(light);
        Assert.Equal(new BrightnessWave(16, 0), wave);

        // 4.8 s per breath at the default speed: dark at 0, fully lit at 2.4 s, dark again at 4.8 s.
        StepClock clock = StepClock.Start(0, 5);
        var states = new BulbVisualState[frame.Layout.Placements.Count];
        sequencer.Sample(FlashTestKit.Ms(2400), clock, states);
        Assert.Equal(1f, states[light].Brightness, 6);
        Assert.Equal(1f, states[light].Glow, 6);
        sequencer.Sample(FlashTestKit.Ms(1200), clock, states);
        Assert.Equal(0.5f, states[light].Brightness, 6);
        sequencer.Sample(FlashTestKit.Ms(4800), clock, states);
        Assert.Equal(0f, states[light].Brightness, 6);

        // Animation bulbs step like Flash Together; static bulbs have no wave.
        int animated = frame.Layout.Placements.First(p => sequencer.GetBulb(p.Ordinal).Kind == BulbAnimationKind.Animation).Ordinal;
        Assert.Null(sequencer.GetWave(animated));
        Assert.Equal(16 % 4, states[animated].Frame);
        Assert.Equal(0.5f, (float)wave!.Value.Evaluate(4), 6);
        sequencer.MoveTo(4, 0);
        Assert.Equal(0.5f, sequencer.Current[light].Brightness, 6);
    }

    [Fact]
    public void SlowGlow_WithoutSmoothFadingIsLitForEightOfSixteenSteps()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.MixedFrame(), new FlashOptions { Pattern = FlashPatternId.SlowGlow, SmoothFading = false });
        int light = FlashTestKit.LightBulbsInRingOrder(sequencer)[0];
        Assert.Null(sequencer.GetWave(light));
        var lit = new List<int>();
        for (int step = 0; step < 32; step++)
        {
            sequencer.MoveTo(step, 0);
            float brightness = sequencer.Current[light].Brightness;
            Assert.True(brightness is 0f or 1f);
            if (brightness == 1f)
            {
                lit.Add(step % 16);
            }
        }

        Assert.Equal([4, 5, 6, 7, 8, 9, 10, 11, 4, 5, 6, 7, 8, 9, 10, 11], lit);
    }

    [Fact]
    public void ChaseAround_LightsOneInThreeAndMovesClockwise()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions { Pattern = FlashPatternId.ChaseAround });
        int[] ring = FlashTestKit.LightBulbsInRingOrder(sequencer);
        Assert.Equal(180, ring.Length);
        bool[]? before = null;
        for (int step = 0; step < 9; step++)
        {
            sequencer.MoveTo(step, 0);
            bool[] lit = [.. ring.Select(o => sequencer.Current[o].Brightness == 1)];
            Assert.Equal(60, lit.Count(l => l));
            Assert.All(Enumerable.Range(0, ring.Length), q => Assert.Equal((((q - step) % 3) + 3) % 3 == 0, lit[q]));
            if (before is not null)
            {
                Assert.Equal(before[..^1], lit[1..]);
            }

            before = lit;
        }

        Assert.Equal(new FadeProfile(90, 150), sequencer.GetFadeProfile(300));
        Assert.Null(sequencer.GetWave(ring[0]));
    }

    [Fact]
    public void ChaseAround_AnimationMotionTravelsClockwise()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.MixedFrame(), new FlashOptions { Pattern = FlashPatternId.ChaseAround });
        int[] animated = [.. sequencer.Layout.Rings[0].Ordinals.Where(o => sequencer.GetBulb(o).Kind == BulbAnimationKind.Animation)];
        sequencer.MoveTo(10, 0);
        Assert.All(animated, o => Assert.Equal(((10 - sequencer.GetBulb(o).AnimationIndex) % 4 + 4) % 4, sequencer.Current[o].Frame));
        int[] now = [.. animated.Select(o => sequencer.Current[o].Frame)];
        sequencer.MoveTo(11, 0);
        int[] next = [.. animated.Select(o => sequencer.Current[o].Frame)];
        Assert.Equal(now[..^1], next[1..]);
    }

    [Fact]
    public void Waves_OffsetEachLightBulbBySixteenthsOfAPeriod()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions { Pattern = FlashPatternId.Waves });
        int[] ring = FlashTestKit.LightBulbsInRingOrder(sequencer);
        for (int q = 0; q < 40; q++)
        {
            Assert.Equal(new BrightnessWave(16, q % 16 / 16.0), sequencer.GetWave(ring[q]));
        }

        // The brightest bulb moves clockwise: at position x the peak is where x / 16 - q / 16 = 1/2, i.e. q = x - 8.
        var states = new BulbVisualState[ring.Length];
        StepClock clock = StepClock.Start(0, 5);
        for (int step = 8; step < 24; step++)
        {
            sequencer.Sample(clock.TimestampOfStep(step), clock, states);
            int brightest = Enumerable.Range(0, 16).MaxBy(q => states[ring[q]].Brightness);
            Assert.Equal((step - 8) % 16, brightest);
        }
    }

    [Fact]
    public void Combination_PlaysEachPatternForFortyStepsFromItsStart()
    {
        var frame = FlashTestKit.MixedFrame();
        var options = new FlashOptions { Pattern = FlashPatternId.Combination, PatternSeed = 21, ClassicRandomSeed = 4 };
        IFlashSequencer combination = FlashTestKit.Create(frame, options);
        FlashPatternId[] order =
        [
            FlashPatternId.FlashTogether, FlashPatternId.Alternating, FlashPatternId.BulbChase, FlashPatternId.RandomFlashing,
            FlashPatternId.Twinkle, FlashPatternId.SlowGlow, FlashPatternId.ChaseAround, FlashPatternId.Waves,
        ];
        for (int segment = 0; segment < 10; segment++)
        {
            FlashPatternId pattern = order[segment % order.Length];
            IFlashSequencer alone = FlashTestKit.Create(frame, options with { Pattern = pattern });
            for (int local = 0; local < 40; local++)
            {
                combination.MoveTo((segment * 40) + local, 0);
                alone.MoveTo(local, 0);
                if (pattern is not (FlashPatternId.SlowGlow or FlashPatternId.Waves))
                {
                    Assert.True(alone.Current.SequenceEqual(combination.Current), $"{pattern} at local step {local}");
                }
            }

            Assert.Equal(alone.GetFadeProfile(300), combination.GetFadeProfile(300));
        }
    }

    [Fact]
    public void Combination_ContinuousSegmentsRestartTheirWave()
    {
        var frame = FlashTestKit.StandardFrame();
        IFlashSequencer combination = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.Combination });
        int light = FlashTestKit.LightBulbsInRingOrder(combination)[3];

        // Slow Glow is the sixth pattern: steps 200..239. Its wave starts dark at step 200.
        combination.MoveTo(200, 0);
        BrightnessWave wave = combination.GetWave(light)!.Value;
        Assert.Equal(0, wave.Evaluate(200), 9);
        Assert.Equal(1, wave.Evaluate(208), 9);

        // Waves (steps 280..319): bulb q = 3 peaks 8 steps after it starts plus 3.
        combination.MoveTo(280, 0);
        BrightnessWave waves = combination.GetWave(light)!.Value;
        Assert.Equal(1, waves.Evaluate(280 + 8 + 3), 9);

        combination.MoveTo(320, 0);
        Assert.Null(combination.GetWave(light));
        Assert.Equal(new FadeProfile(72, 120), combination.GetFadeProfile(300));
    }
}
