using HolidayLights.Core.Flash;
using HolidayLights.Tests.Layout;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Flash;

/// <summary>"Twinkle" (PRODUCT-SPEC 5.7 pattern 5): deterministic, about 80 % lit, never repeating, limited retriggers.</summary>
public sealed class TwinkleTests
{
    private static (LightsLayout Layout, IBulbResolver Bulbs) BigStandardFrame()
    {
        string id = BulbIds.BuiltIn("standard-bulbs");
        var arrangement = new SlotAssignment
        {
            Top = [id], Right = [id], Bottom = [id], Left = [id],
            TopLeft = id, TopRight = id, BottomLeft = id, BottomRight = id,
        };
        FakeBulbResolver bulbs = TableBulbs.Resolver;
        return (FlashTestKit.LayoutEngine.Layout([new LayoutTarget("d", new RectI(0, 0, 3840, 2088), 1.0)], arrangement, bulbs), bulbs);
    }

    // The reference PC (two identical 4K displays at 150 %, the second at x = -3840) with light bulbs on the top and bottom,
    // a 4-frame animation on the sides and light-bulb corners.
    private static readonly LayoutTarget Display1 = new("display1", new RectI(0, 0, 3840, 2088), 1.5);
    private static readonly LayoutTarget Display2 = new("display2", new RectI(-3840, 0, 0, 2088), 1.5);

    private static (LightsLayout Layout, IBulbResolver Bulbs) ReferencePc(FrameMode mode, params LayoutTarget[] targets)
    {
        var light = new FakeBulb("addon:Light", 20, 20) { Kind = BulbAnimationKind.LightBulb, Phases = 2 };
        var animation = new FakeBulb("addon:Anim", 20, 20) { Phases = 4 };
        var bulbs = new FakeBulbResolver([light, animation]);
        var arrangement = new SlotAssignment
        {
            Top = [light.Id], Bottom = [light.Id], Right = [animation.Id], Left = [animation.Id],
            TopLeft = light.Id, TopRight = light.Id, BottomLeft = light.Id, BottomRight = light.Id,
        };
        return (FlashTestKit.LayoutEngine.Layout(targets, arrangement, bulbs, mode), bulbs);
    }

    [Fact]
    public void PatternRandom_IsSplitMix64OfSeedRingIndexAndStep()
    {
        // The first output of SplitMix64 seeded with 0 (Steele, Lea and Flood).
        Assert.Equal(0xE220A8397B1DCDAFUL, PatternRandom.SplitMix64(0));
        Assert.Equal(0x0000000700000000UL ^ 99UL, PatternRandom.Key(99, 7));
        Assert.Equal((PatternRandom.SplitMix64(PatternRandom.Key(5, 3) ^ 12) >> 11) / (double)(1UL << 53), PatternRandom.Uniform(PatternRandom.Key(5, 3), 12));
        Assert.All(Enumerable.Range(0, 1000), s => Assert.InRange(PatternRandom.Uniform(17, s), 0.0, 0.9999999999999999));
    }

    [Fact]
    public void LightBulbs_FollowTheSpecifiedChainExactly()
    {
        const ulong Seed = 0xC0FFEE;
        var frame = FlashTestKit.StandardFrame();
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = Seed });
        bool[] lit = [.. frame.Layout.Placements.Select(p => PatternRandom.Uniform(PatternRandom.Key(Seed, p.RingIndex), 0) < 0.8)];
        for (int step = 0; step <= 60; step++)
        {
            if (step > 0)
            {
                for (int o = 0; o < lit.Length; o++)
                {
                    double u = PatternRandom.Uniform(PatternRandom.Key(Seed, frame.Layout.Placements[o].RingIndex), step);
                    lit[o] = lit[o] ? u >= 0.12 : u < 0.5;
                }

                sequencer.MoveTo(step, 0);
            }

            Assert.Equal(lit, sequencer.Current.ToArray().Select(s => s.Brightness == 1));
        }
    }

    [Fact]
    public void Twinkle_IsDeterministicForASeedAndDiffersBetweenSeeds()
    {
        var frame = FlashTestKit.MixedFrame();
        IFlashSequencer a = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = 42 });
        IFlashSequencer b = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = 42 });
        IFlashSequencer c = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = 43 });
        int differences = 0;
        for (int step = 0; step < 200; step++)
        {
            a.MoveTo(step, 0);
            b.MoveTo(step, 0);
            c.MoveTo(step, 0);
            Assert.True(a.Current.SequenceEqual(b.Current));
            differences += a.Current.SequenceEqual(c.Current) ? 0 : 1;
        }

        Assert.True(differences > 190);
    }

    [Fact]
    public void LightBulbs_AreAboutEightyPercentLit()
    {
        var frame = BigStandardFrame();
        long startLit = 0;
        long lit = 0;
        long samples = 0;
        int bulbs = frame.Layout.Placements.Count;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = seed });
            startLit += sequencer.Current.ToArray().Count(s => s.Brightness == 1);
            for (int step = 1; step <= 300; step++)
            {
                sequencer.MoveTo(step, 0);
                lit += sequencer.Current.ToArray().Count(s => s.Brightness == 1);
                samples += bulbs;
            }
        }

        // Start: 0.8. Steady state of the chain: 0.5 / (0.12 + 0.5) = 0.806.
        Assert.InRange(startLit / (10.0 * bulbs), 0.78, 0.82);
        Assert.InRange(lit / (double)samples, 0.796, 0.816);
    }

    [Fact]
    public void LightBulbs_GoDarkAndLightAtTheSpecifiedRates()
    {
        var frame = BigStandardFrame();
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = 7 });
        int litBefore = 0, wentDark = 0, darkBefore = 0, lightened = 0;
        BulbVisualState[] previous = FlashTestKit.Snapshot(sequencer);
        for (int step = 1; step <= 2000; step++)
        {
            sequencer.MoveTo(step, 0);
            for (int o = 0; o < previous.Length; o++)
            {
                bool was = previous[o].Brightness == 1;
                bool now = sequencer.Current[o].Brightness == 1;
                if (was)
                {
                    litBefore++;
                    wentDark += now ? 0 : 1;
                }
                else
                {
                    darkBefore++;
                    lightened += now ? 1 : 0;
                }
            }

            previous = FlashTestKit.Snapshot(sequencer);
        }

        Assert.InRange(wentDark / (double)litBefore, 0.115, 0.125);
        Assert.InRange(lightened / (double)darkBefore, 0.48, 0.52);
    }

    [Fact]
    public void AnimationBulbs_AdvanceAboutEveryOtherStep()
    {
        var frame = FlashTestKit.MixedFrame(1600, 1200);
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = 3 });
        int[] animated = [.. frame.Layout.Placements.Where(p => sequencer.GetBulb(p.Ordinal).Kind == BulbAnimationKind.Animation).Select(p => p.Ordinal)];
        Assert.NotEmpty(animated);
        int advances = 0, holds = 0, others = 0;
        BulbVisualState[] previous = FlashTestKit.Snapshot(sequencer);
        Assert.All(animated, o => Assert.Equal(0, previous[o].Frame));
        for (int step = 1; step <= 3000; step++)
        {
            sequencer.MoveTo(step, 0);
            foreach (int o in animated)
            {
                int delta = (sequencer.Current[o].Frame - previous[o].Frame + 4) % 4;
                advances += delta == 1 ? 1 : 0;
                holds += delta == 0 ? 1 : 0;
                others += delta > 1 ? 1 : 0;
            }

            previous = FlashTestKit.Snapshot(sequencer);
        }

        double total = advances + holds + others;
        Assert.InRange(advances / total, 0.48, 0.52);

        // Only the restarts every 1,024 steps can jump by more than one frame.
        Assert.True(others <= animated.Length * 3, $"{others} jumps");
    }

    [Fact]
    public void Twinkle_NeverRepeatsLikeRandomFlashing()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = 11 });
        var seen = new HashSet<string>();
        for (int step = 0; step < 500; step++)
        {
            sequencer.MoveTo(step, 0);
            Assert.True(seen.Add(string.Concat(sequencer.Current.ToArray().Select(s => s.Brightness == 1 ? '1' : '0'))), $"step {step} repeats");
        }
    }

    [Fact]
    public void DirectEvaluation_EqualsSteppingAcrossRestarts()
    {
        var frame = FlashTestKit.MixedFrame();
        var options = new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = 123456789 };
        IFlashSequencer stepping = FlashTestKit.Create(frame, options);
        var history = new Dictionary<int, BulbVisualState[]>();
        int[] probes = [0, 1, 2, 511, 1023, 1024, 1025, 2047, 2048, 3001, 4100];
        for (int step = 0; step <= probes.Max(); step++)
        {
            stepping.MoveTo(step, 0);
            if (probes.Contains(step))
            {
                history[step] = FlashTestKit.Snapshot(stepping);
            }
        }

        foreach (int step in probes.Reverse())
        {
            IFlashSequencer fresh = FlashTestKit.Create(frame, options);
            fresh.MoveTo(step, 0);
            Assert.True(history[step].AsSpan().SequenceEqual(fresh.Current), $"step {step}");
        }
    }

    [Fact]
    public void FarJumps_StayCheap()
    {
        var frame = FlashTestKit.MixedFrame(3840, 2160);
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = 5 });
        var watch = System.Diagnostics.Stopwatch.StartNew();
        sequencer.MoveTo(2_000_000_000, 0); // about 19 years at the default speed
        sequencer.MoveTo(1_999_999_000, 0);
        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < PerformanceBudget.Milliseconds(500), $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void LimitedFlashing_RelightsABulbAtMostEveryTwoSteps()
    {
        // With "Limit Flashing" the interval is 3 or more (180 ms), so two steps are at least 360 ms (>= 333 ms).
        IFlashSequencer sequencer = FlashTestKit.Create(BigStandardFrame(), new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = 9, LimitFlashing = true });
        var lastLit = new long[sequencer.Layout.Placements.Count];
        Array.Fill(lastLit, -10);
        BulbVisualState[] previous = FlashTestKit.Snapshot(sequencer);
        for (int step = 1; step <= 1000; step++)
        {
            sequencer.MoveTo(step, 0);
            for (int o = 0; o < previous.Length; o++)
            {
                if (previous[o].Brightness == 0 && sequencer.Current[o].Brightness == 1)
                {
                    Assert.True(step - lastLit[o] >= 2);
                    lastLit[o] = step;
                }
            }

            previous = FlashTestKit.Snapshot(sequencer);
        }

        Assert.True(sequencer.GetFadeProfile(180).TurnOffMilliseconds > 0);
    }

    [Fact]
    public void DisplaySeeds_KeepDisplayOneAndSeparateEveryOtherDisplay()
    {
        Assert.Equal(12345UL, DisplaySeeds.PatternSeed(12345, 0));
        Assert.Equal(777u, DisplaySeeds.ClassicRandomSeed(777, 0));
        Assert.Equal(12345UL ^ PatternRandom.SplitMix64(1), DisplaySeeds.PatternSeed(12345, 1));
        Assert.Equal(777u ^ (uint)PatternRandom.SplitMix64(2), DisplaySeeds.ClassicRandomSeed(777, 2));

        // Two rings could only draw the same u(i, s) at the same step if their seeds differed by (i XOR j) << 32 alone:
        // the seeds of any two of the first 64 rings differ in the low half and by far more than 65,536 in the high half.
        for (int a = 0; a < 64; a++)
        {
            for (int b = a + 1; b < 64; b++)
            {
                ulong difference = DisplaySeeds.PatternSeed(99, a) ^ DisplaySeeds.PatternSeed(99, b);
                Assert.True(difference >> 32 >= 1 << 16 && (uint)difference != 0, $"rings {a} and {b}");
            }
        }

        Assert.Equal(64, Enumerable.Range(0, 64).Select(d => DisplaySeeds.ClassicRandomSeed(1, d)).Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplaySeeds.PatternSeed(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplaySeeds.ClassicRandomSeed(1, -1));
    }

    [Theory]
    [InlineData(FlashPatternId.Twinkle, 0)]
    [InlineData(FlashPatternId.Combination, 160)] // Combination's Twinkle segment: steps 160-199
    public void EachDisplay_IdenticalDisplaysDoNotTwinkleInLockstep(FlashPatternId pattern, int firstStep)
    {
        // Product-owner decision 5. Display 1 (ring 0) keeps exactly the sequence it has alone; display 2 (ring 1) twinkles
        // on its own, exactly as display 2 alone with DisplaySeeds.PatternSeed(seed, 1) (what the screen saver uses).
        const ulong Seed = 0x5EED;
        var both = ReferencePc(FrameMode.EachDisplay, Display1, Display2);
        var oneAlone = ReferencePc(FrameMode.EachDisplay, Display1);
        var twoAlone = ReferencePc(FrameMode.EachDisplay, Display2);
        var options = new FlashOptions { Pattern = pattern, PatternSeed = Seed, SmoothFading = false };
        IFlashSequencer desktop = FlashTestKit.Create(both, options);
        IFlashSequencer first = FlashTestKit.Create(oneAlone, options);
        IFlashSequencer second = FlashTestKit.Create(twoAlone, options with { PatternSeed = DisplaySeeds.PatternSeed(Seed, 1) });

        IReadOnlyList<int> ring0 = both.Layout.Rings[0].Ordinals;
        IReadOnlyList<int> ring1 = both.Layout.Rings[1].Ordinals;
        int count = oneAlone.Layout.Placements.Count;
        Assert.Equal(ring0.Count, ring1.Count);
        Assert.Equal(2 * count, both.Layout.Placements.Count);
        Assert.All(ring0, o => Assert.True(o < count));

        long samePosition = 0, positions = 0, identicalSteps = 0;
        for (int step = firstStep; step < firstStep + 40; step++)
        {
            desktop.MoveTo(step, 0);
            first.MoveTo(step, 0);
            second.MoveTo(step, 0);
            Assert.True(desktop.Current[..count].SequenceEqual(first.Current), $"display 1 changed at step {step}");
            Assert.True(desktop.Current[count..].SequenceEqual(second.Current), $"display 2 differs from its per-display seed at step {step}");

            int same = Enumerable.Range(0, ring0.Count).Count(k => desktop.Current[ring0[k]] == desktop.Current[ring1[k]]);
            samePosition += same;
            positions += ring0.Count;
            identicalSteps += same == ring0.Count ? 1 : 0;
        }

        // Independent bulbs agree about 69 % of the time (light bulbs 0.806^2 + 0.194^2, animation bulbs 1/4); lockstep is 100 %.
        Assert.Equal(0, identicalSteps);
        Assert.InRange(samePosition / (double)positions, 0.3, 0.8);
    }

    [Fact]
    public void AllDisplaysTogether_TheOneWreathKeepsTheSpecifiedChain()
    {
        // One ring over both displays: ring 0, so u(i, s) uses the pattern seed itself, exactly as before.
        const ulong Seed = 0xABCDEF;
        var frame = ReferencePc(FrameMode.AllDisplaysTogether, Display1, Display2);
        Assert.Single(frame.Layout.Rings);
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.Twinkle, PatternSeed = Seed });
        int[] lights = [.. frame.Layout.Placements.Where(p => sequencer.GetBulb(p.Ordinal).Kind == BulbAnimationKind.LightBulb).Select(p => p.Ordinal)];
        bool[] lit = [.. lights.Select(o => PatternRandom.Uniform(PatternRandom.Key(Seed, frame.Layout.Placements[o].RingIndex), 0) < 0.8)];
        for (int step = 0; step <= 30; step++)
        {
            if (step > 0)
            {
                for (int n = 0; n < lights.Length; n++)
                {
                    double u = PatternRandom.Uniform(PatternRandom.Key(Seed, frame.Layout.Placements[lights[n]].RingIndex), step);
                    lit[n] = lit[n] ? u >= 0.12 : u < 0.5;
                }

                sequencer.MoveTo(step, 0);
            }

            Assert.Equal(lit, lights.Select(o => sequencer.Current[o].Brightness == 1));
        }
    }
}
