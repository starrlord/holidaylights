using HolidayLights.Core.Flash;
using HolidayLights.Tests.Layout;

namespace HolidayLights.Tests.Flash;

/// <summary>The 5.4 quirks the product owner keeps (PRODUCT-SPEC 5.6) and the light-bulb states of the classic patterns.</summary>
public sealed class ClassicPatternTests
{
    private static readonly FakeBulb TwoPhase = new("addon:Two", 20, 20) { Phases = 2 };
    private static readonly FakeBulb FourPhase = new("addon:Four", 20, 20) { Phases = 4 };
    private static readonly FakeBulb Holly = new("addon:Holly", 20, 20) { Phases = 1 };

    private static (LightsLayout Layout, IBulbResolver Bulbs) TopOnly(IEnumerable<string> ids, string? corner = null)
    {
        var bulbs = new FakeBulbResolver([TwoPhase, FourPhase, Holly]);
        var arrangement = new SlotAssignment { Top = [.. ids], TopLeft = corner, TopRight = corner };
        return (FlashTestKit.LayoutEngine.Layout([new LayoutTarget("d", new RectI(0, 0, 400, 300), 1.0)], arrangement, bulbs), bulbs);
    }

    private static int[] TopFrames(IFlashSequencer sequencer, long step)
    {
        sequencer.MoveTo(step, 0);
        return [.. sequencer.Layout.Displays[0].Strips[0].Placements.Where(p => !p.IsCorner).Select(p => sequencer.Current[p.Ordinal].Frame)];
    }

    [Fact]
    public void Alternating_AlternatesTwoPhaseBulbsAndSkipsSingleFrameBulbs()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(TopOnly([TwoPhase.Id, Holly.Id]), new FlashOptions { Pattern = FlashPatternId.Alternating });
        int[] frames = TopFrames(sequencer, 0);

        // Animated bulbs are 1st, 3rd, 5th...: k = 1, 2, 3...; odd k shows f + 1 (dark first), even k shows f.
        int[] animated = [.. frames.Where((_, i) => i % 2 == 0)];
        Assert.Equal([1, 0, 1, 0, 1, 0], animated.Take(6));
        Assert.All(frames.Where((_, i) => i % 2 == 1), f => Assert.Equal(0, f));
    }

    [Fact]
    public void Alternating_StopsAlternatingNextToAFourPhaseBulb()
    {
        // W = 4: the shift W / 2 = 2 is a whole cycle of a 2-phase bulb (5.4 quirk, kept).
        IFlashSequencer sequencer = FlashTestKit.Create(TopOnly([TwoPhase.Id, FourPhase.Id]), new FlashOptions { Pattern = FlashPatternId.Alternating });
        Assert.Equal(4, sequencer.GetBulb(sequencer.Layout.Displays[0].Strips[0].Placements[0].Ordinal).StripFrameCount);
        // Every animated bulb is on the same frame: the 2-phase bulbs (odd k) are shifted by a whole cycle.
        int[] frames = TopFrames(sequencer, 1);
        Assert.All(frames, f => Assert.Equal(1, f));
    }

    [Fact]
    public void BulbChase_RunsTowardTheStartOfTheStrip()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(TopOnly([FourPhase.Id]), new FlashOptions { Pattern = FlashPatternId.BulbChase });
        int[] now = TopFrames(sequencer, 5);
        int[] next = TopFrames(sequencer, 6);

        // One step later, bulb k shows what bulb k + 1 showed.
        Assert.Equal(now.Skip(1), next.Take(now.Length - 1));
        Assert.Equal([2, 3, 0, 1], now.Take(4)); // (f + k) mod 4 with f = 5 mod 4 = 1, k = 1, 2, 3, 4
    }

    [Theory]
    [InlineData(FlashPatternId.Alternating)]
    [InlineData(FlashPatternId.BulbChase)]
    [InlineData(FlashPatternId.RandomFlashing)]
    public void Corners_IgnoreThePattern(FlashPatternId pattern)
    {
        IFlashSequencer sequencer = FlashTestKit.Create(TopOnly([TwoPhase.Id], corner: FourPhase.Id), new FlashOptions { Pattern = pattern });
        BulbPlacement[] corners = [.. sequencer.Layout.Placements.Where(p => p.IsCorner)];
        Assert.Equal(2, corners.Length);
        for (int step = 0; step < 16; step++)
        {
            sequencer.MoveTo(step, 0);
            int w = pattern == FlashPatternId.RandomFlashing ? 8 : 4;
            Assert.All(corners, c => Assert.Equal(step % w % 4, sequencer.Current[c.Ordinal].Frame));
        }
    }

    [Fact]
    public void RandomFlashing_RepeatsEveryEightStepsAndRerollsWithAnotherSeed()
    {
        (LightsLayout Layout, IBulbResolver Bulbs) frame = FlashTestKit.StandardFrame();
        IFlashSequencer first = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.RandomFlashing, ClassicRandomSeed = 7 });
        IFlashSequencer again = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.RandomFlashing, ClassicRandomSeed = 7 });
        IFlashSequencer other = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.RandomFlashing, ClassicRandomSeed = 8 });

        var cycle = new List<BulbVisualState[]>();
        bool differs = false;
        for (int step = 0; step < 24; step++)
        {
            first.MoveTo(step, 0);
            again.MoveTo(step, 0);
            other.MoveTo(step, 0);
            Assert.Equal(first.Current.ToArray(), again.Current.ToArray());
            differs |= !first.Current.SequenceEqual(other.Current);
            if (step < 8)
            {
                cycle.Add(FlashTestKit.Snapshot(first));
            }
            else
            {
                Assert.Equal(cycle[step % 8], FlashTestKit.Snapshot(first));
            }
        }

        Assert.True(differs);
        double lit = cycle.SelectMany(s => s).Count(s => s.Brightness == 1) / (double)cycle.Sum(s => s.Length);
        Assert.InRange(lit, 0.4, 0.6);
    }

    [Fact]
    public void FlashTogether_LightBulbsAreLitOnTheirLitFrameAndGlowWithIt()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions());
        Assert.Equal(FlashPatternId.FlashTogether, sequencer.Options.Pattern);
        Assert.All(sequencer.Current.ToArray(), s => Assert.Equal(new BulbVisualState(0, 1f, 1f), s));
        sequencer.MoveTo(1, 0);
        Assert.All(sequencer.Current.ToArray(), s => Assert.Equal(new BulbVisualState(1, 0f, 0f), s));
    }

    [Fact]
    public void AnimationAndStaticBulbs_HaveFullOpacityAndNoGlow()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.MixedFrame(), new FlashOptions());
        for (int step = 0; step < 6; step++)
        {
            sequencer.MoveTo(step, 0);
            foreach (BulbPlacement p in sequencer.Layout.Placements)
            {
                FlashBulbInfo info = sequencer.GetBulb(p.Ordinal);
                BulbVisualState state = sequencer.Current[p.Ordinal];
                if (info.Kind == BulbAnimationKind.Animation)
                {
                    Assert.Equal(new BulbVisualState(step % 4, 1f, 0f), state);
                }
                else if (info.Kind == BulbAnimationKind.Static)
                {
                    Assert.Equal(new BulbVisualState(0, 1f, 0f), state);
                }
            }
        }
    }

    // An add-on light bulb whose brighter frame is the second (681 such animations in 76 bundled .bul files), a classic one
    // lit on frame 0, and a 4-frame animation, on sides and corners.
    private static (LightsLayout Layout, IBulbResolver Bulbs) LitFrameMix()
    {
        var bright = new FakeBulb("addon:Bright", 20, 20) { Kind = BulbAnimationKind.LightBulb, LitFrame = 1 };
        var classic = new FakeBulb("addon:Classic", 20, 20) { Kind = BulbAnimationKind.LightBulb, LitFrame = 0 };
        var bulbs = new FakeBulbResolver([bright, classic, FourPhase]);
        var arrangement = new SlotAssignment
        {
            Top = [bright.Id, classic.Id], Bottom = [FourPhase.Id], Right = [bright.Id], Left = [classic.Id],
            TopLeft = bright.Id, TopRight = classic.Id, BottomLeft = FourPhase.Id, BottomRight = bright.Id,
        };
        return (FlashTestKit.LayoutEngine.Layout([new LayoutTarget("d", new RectI(0, 0, 400, 300), 1.0)], arrangement, bulbs), bulbs);
    }

    [Fact]
    public void DontFlash_ShowsFrameZeroOfEveryBulbExactlyLike54()
    {
        // PRODUCT-SPEC 5.6: W = 1 and phase 0 for every side and corner bulb. A light bulb whose lit frame is the second
        // shows its darker first frame, as 5.4 did; light bulbs lit on frame 0 (every built-in) stay lit.
        var frame = LitFrameMix();
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.DontFlash });
        Assert.Contains(frame.Layout.Placements, p => sequencer.GetBulb(p.Ordinal).LitFrame == 1 && p.IsCorner);
        Assert.Contains(frame.Layout.Placements, p => sequencer.GetBulb(p.Ordinal).LitFrame == 1 && !p.IsCorner);
        for (int step = 0; step < 5; step++)
        {
            sequencer.MoveTo(step, 0);
            foreach (BulbPlacement p in frame.Layout.Placements)
            {
                FlashBulbInfo info = sequencer.GetBulb(p.Ordinal);
                BulbVisualState expected = info.Kind != BulbAnimationKind.LightBulb ? new BulbVisualState(0, 1f, 0f)
                    : info.LitFrame == 0 ? new BulbVisualState(0, 1f, 1f) : new BulbVisualState(0, 0f, 0f);
                Assert.Equal(expected, sequencer.Current[p.Ordinal]);
                Assert.Equal(1, info.StripFrameCount);
            }
        }
    }

    [Fact]
    public void StopFlashing_KeepsEveryLightBulbLitEvenWhenItsLitFrameIsTheSecond()
    {
        var frame = LitFrameMix();
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.FlashTogether, StopFlashing = true });
        foreach (BulbPlacement p in frame.Layout.Placements)
        {
            FlashBulbInfo info = sequencer.GetBulb(p.Ordinal);
            BulbVisualState expected = info.Kind == BulbAnimationKind.LightBulb ? new BulbVisualState(info.LitFrame, 1f, 1f) : new BulbVisualState(0, 1f, 0f);
            Assert.Equal(expected, sequencer.Current[p.Ordinal]);
        }
    }

    [Theory]
    [InlineData(FlashPatternId.Alternating)]
    [InlineData(FlashPatternId.BulbChase)]
    public void AllDisplaysTogether_AlternatingAndBulbChaseRunOnAcrossTheSeam(FlashPatternId pattern)
    {
        // Reference PC, one wreath: the top and bottom edges are split at x = 0. With two-phase light bulbs, neighbours
        // along an edge always differ in both patterns, also the pair at x = -48 | 0 across the bezel.
        string id = FlashTestKit.StandardBulbs;
        var arrangement = new SlotAssignment
        {
            Top = [id], Right = [id], Bottom = [id], Left = [id],
            TopLeft = id, TopRight = id, BottomLeft = id, BottomRight = id,
        };
        LayoutTarget[] targets =
        [
            new("display1", new RectI(0, 0, 3840, 2088), 1.5),
            new("display2", new RectI(-3840, 0, 0, 2088), 1.5),
        ];
        LightsLayout layout = FlashTestKit.LayoutEngine.Layout(targets, arrangement, TableBulbs.Resolver, FrameMode.AllDisplaysTogether);
        IFlashSequencer sequencer = FlashTestKit.Engine.CreateSequencer(layout, TableBulbs.Resolver, new FlashOptions { Pattern = pattern, SmoothFading = false });
        for (int step = 0; step < 4; step++)
        {
            sequencer.MoveTo(step, 0);
            foreach (CellSlot slot in new[] { CellSlot.Top, CellSlot.Bottom })
            {
                BulbPlacement[] edge = [.. layout.Placements.Where(p => p.Slot == slot).OrderBy(p => p.Bounds.Left)];
                Assert.Contains(edge, p => p.Bounds.Left == 0);
                for (int k = 1; k < edge.Length; k++)
                {
                    Assert.True(
                        sequencer.Current[edge[k - 1].Ordinal].Brightness != sequencer.Current[edge[k].Ordinal].Brightness,
                        $"{slot} step {step}: bulbs at x = {edge[k - 1].Bounds.Left} and {edge[k].Bounds.Left} share a state");
                }
            }
        }
    }

    [Fact]
    public void StopFlashing_ShowsALitStillPictureAndIgnoresStepsAndMusic()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.MixedFrame(), new FlashOptions { Pattern = FlashPatternId.DanceToMusic, StopFlashing = true });
        BulbVisualState[] still = FlashTestKit.Snapshot(sequencer);
        Assert.All(sequencer.Layout.Placements, p => Assert.Equal(sequencer.GetBulb(p.Ordinal).Kind == BulbAnimationKind.LightBulb ? 1f : 0f, still[p.Ordinal].Glow));
        Assert.All(still, s => Assert.Equal(0, s.Frame));

        sequencer.MoveTo(17, 0);
        Assert.Equal(still, FlashTestKit.Snapshot(sequencer));
        Assert.Same(MusicResponse.None, sequencer.ApplyMusicEvents([new MusicEvent(MusicEventKind.SongStarted, 0, 0, 0, 0)]));
        Assert.False(sequencer.IsDancing);
        Assert.Null(sequencer.GetWave(sequencer.Layout.Placements[4].Ordinal));
        Assert.Equal(FadeProfile.Instant, sequencer.GetFadeProfile(300));

        var samples = new BulbVisualState[still.Length];
        sequencer.Sample(FlashTestKit.Ms(5000), StepClock.Start(0, 5), samples);
        Assert.Equal(still, samples);
    }

    [Theory]
    [InlineData(FlashPatternId.FlashTogether)]
    [InlineData(FlashPatternId.Alternating)]
    [InlineData(FlashPatternId.BulbChase)]
    [InlineData(FlashPatternId.RandomFlashing)]
    public void ClassicPatterns_RepeatEveryStripFrameCountWithGlowEqualToBrightness(FlashPatternId pattern)
    {
        // The IFlashSequencer guarantee the renderer's repeating light-bulb animations rely on.
        foreach ((LightsLayout Layout, IBulbResolver Bulbs) frame in new[] { FlashTestKit.StandardFrame(), FlashTestKit.MixedFrame() })
        {
            var options = new FlashOptions { Pattern = pattern, ClassicRandomSeed = 7 };
            IFlashSequencer sequencer = FlashTestKit.Create(frame, options);
            IFlashSequencer later = FlashTestKit.Create(frame, options);
            int[] periods = [.. sequencer.Layout.Placements.Select(p => sequencer.GetBulb(p.Ordinal).StripFrameCount).Distinct()];
            for (long step = 0; step < 24; step++)
            {
                sequencer.MoveTo(step, 0);
                foreach (int period in periods)
                {
                    later.MoveTo(step + period, 0);
                    foreach (BulbPlacement p in sequencer.Layout.Placements)
                    {
                        FlashBulbInfo info = sequencer.GetBulb(p.Ordinal);
                        if (info.StripFrameCount != period)
                        {
                            continue;
                        }

                        BulbVisualState state = sequencer.Current[p.Ordinal];
                        Assert.Equal(state, later.Current[p.Ordinal]);
                        if (info.Kind == BulbAnimationKind.LightBulb)
                        {
                            Assert.Equal(state.Brightness, state.Glow);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void ClassicPatterns_IgnoreMusic()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions { Pattern = FlashPatternId.FlashTogether });
        BulbVisualState[] before = FlashTestKit.Snapshot(sequencer);
        MusicResponse response = sequencer.ApplyMusicEvents(
        [
            new MusicEvent(MusicEventKind.SongStarted, 0, 0, 0, 0),
            new MusicEvent(MusicEventKind.NoteOn, 10, 9, 36, 127),
            new MusicEvent(MusicEventKind.Beat, 20, 0, 0, 127),
            new MusicEvent(MusicEventKind.AudioBeat, 30, 0, 0, 127),
        ]);
        Assert.Same(MusicResponse.None, response);
        Assert.Equal(before, FlashTestKit.Snapshot(sequencer));
        Assert.False(sequencer.IsDancing);
    }
}
