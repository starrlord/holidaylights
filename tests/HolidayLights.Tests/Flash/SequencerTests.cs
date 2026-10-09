using HolidayLights.Core.Flash;
using HolidayLights.Tests.Layout;

namespace HolidayLights.Tests.Flash;

/// <summary>The sequencer's per-bulb facts, ring indices and argument handling.</summary>
public sealed class SequencerTests
{
    [Fact]
    public void GetBulb_DescribesKindsFramesAndRingIndices()
    {
        var frame = FlashTestKit.MixedFrame();
        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions());
        Assert.Same(frame.Layout, sequencer.Layout);
        int lights = 0;
        int animations = 0;
        foreach (int ordinal in frame.Layout.Rings[0].Ordinals)
        {
            FlashBulbInfo info = sequencer.GetBulb(ordinal);
            BulbPlacement placement = frame.Layout.Placements[ordinal];
            Assert.Equal(ordinal, info.Ordinal);
            switch (info.Kind)
            {
                case BulbAnimationKind.LightBulb:
                    Assert.Equal((2, 0, lights++, -1, 2), (info.FrameCount, info.LitFrame, info.LightIndex, info.AnimationIndex, info.StripFrameCount));
                    break;
                case BulbAnimationKind.Animation:
                    Assert.Equal((4, animations++, -1, -1, 4), (info.FrameCount, info.AnimationIndex, info.LightIndex, info.MusicGroup, info.StripFrameCount));
                    break;
                default:
                    Assert.True(placement.IsCorner);
                    Assert.Equal((1, -1, -1, -1), (info.FrameCount, info.LightIndex, info.AnimationIndex, info.MusicGroup));
                    break;
            }
        }

        Assert.Equal(76, lights);
        Assert.Equal(56, animations);
    }

    [Theory]
    [InlineData(FlashPatternId.DontFlash, 1)]
    [InlineData(FlashPatternId.RandomFlashing, 8)]
    [InlineData(FlashPatternId.BulbChase, 4)]
    [InlineData(FlashPatternId.Twinkle, 4)]
    public void StripFrameCount_FollowsThePattern(FlashPatternId pattern, int frames)
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.MixedFrame(), new FlashOptions { Pattern = pattern });
        int side = sequencer.Layout.Displays[0].Strips.Single(s => s.Side == Side.Right).Placements[0].Ordinal;
        Assert.Equal(frames, sequencer.GetBulb(side).StripFrameCount);
    }

    [Fact]
    public void RingIndices_RestartOnEveryDisplay()
    {
        string id = FlashTestKit.StandardBulbs;
        var arrangement = new SlotAssignment { Top = [id], Bottom = [id], Right = [id], Left = [id] };
        FakeBulbResolver bulbs = TableBulbs.Resolver;
        LightsLayout layout = FlashTestKit.LayoutEngine.Layout(
            [new LayoutTarget("a", new RectI(0, 0, 960, 600), 1.0), new LayoutTarget("b", new RectI(960, 0, 1920, 600), 1.0)], arrangement, bulbs);
        IFlashSequencer sequencer = FlashTestKit.Engine.CreateSequencer(layout, bulbs, new FlashOptions { Pattern = FlashPatternId.ChaseAround });

        Assert.Equal(2, layout.Rings.Count);
        foreach (LightsRing ring in layout.Rings)
        {
            Assert.Equal(Enumerable.Range(0, ring.Ordinals.Count), ring.Ordinals.Select(o => sequencer.GetBulb(o).LightIndex));
            Assert.Equal((ring.Ordinals.Count + 2) / 3, ring.Ordinals.Count(o => sequencer.Current[o].Brightness == 1));
        }
    }

    [Fact]
    public void AddOnLightBulbWithASecondLitFrame_IsDarkOnFrameZero()
    {
        var bright = new FakeBulb("addon:Bright", 20, 20) { Kind = BulbAnimationKind.LightBulb, LitFrame = 1 };
        var bulbs = new FakeBulbResolver([bright]);
        LightsLayout layout = FlashTestKit.LayoutEngine.Layout([new LayoutTarget("d", new RectI(0, 0, 200, 200), 1.0)], new SlotAssignment { Top = [bright.Id] }, bulbs);
        IFlashSequencer sequencer = FlashTestKit.Engine.CreateSequencer(layout, bulbs, new FlashOptions());

        Assert.All(sequencer.Current.ToArray(), s => Assert.Equal(new BulbVisualState(0, 0f, 0f), s));
        sequencer.MoveTo(1, 0);
        Assert.All(sequencer.Current.ToArray(), s => Assert.Equal(new BulbVisualState(1, 1f, 1f), s));
    }

    [Fact]
    public void BulbsThatNoLongerResolve_StayStill()
    {
        var frame = FlashTestKit.StandardFrame();
        IFlashSequencer sequencer = FlashTestKit.Engine.CreateSequencer(frame.Layout, new FakeBulbResolver([]), new FlashOptions());
        sequencer.MoveTo(3, 0);
        Assert.All(sequencer.Current.ToArray(), s => Assert.Equal(new BulbVisualState(0, 1f, 0f), s));
        Assert.All(frame.Layout.Placements, p => Assert.Equal(BulbAnimationKind.Static, sequencer.GetBulb(p.Ordinal).Kind));
    }

    [Fact]
    public void MoveTo_NegativeStepsShowStepZero()
    {
        IFlashSequencer sequencer = FlashTestKit.Create(FlashTestKit.StandardFrame(), new FlashOptions());
        sequencer.MoveTo(5, 0);
        sequencer.MoveTo(-3, 0);
        Assert.Equal(0, sequencer.Step);
        Assert.All(sequencer.Current.ToArray(), s => Assert.Equal(1f, s.Brightness));
    }

    [Fact]
    public void EmptyLayout_HasNoStates()
    {
        IFlashSequencer sequencer = FlashTestKit.Engine.CreateSequencer(LightsLayout.Empty, new FakeBulbResolver([]), new FlashOptions { Pattern = FlashPatternId.Twinkle });
        sequencer.MoveTo(10, 0);
        Assert.Equal(0, sequencer.Current.Length);
        sequencer.Sample(FlashTestKit.Ms(1000), StepClock.Start(0, 5), Span<BulbVisualState>.Empty);
    }

    [Fact]
    public void Arguments_AreValidated()
    {
        var frame = FlashTestKit.StandardFrame();
        Assert.Throws<ArgumentNullException>(() => FlashTestKit.Engine.CreateSequencer(null!, frame.Bulbs, new FlashOptions()));
        Assert.Throws<ArgumentNullException>(() => FlashTestKit.Engine.CreateSequencer(frame.Layout, null!, new FlashOptions()));
        Assert.Throws<ArgumentNullException>(() => FlashTestKit.Engine.CreateSequencer(frame.Layout, frame.Bulbs, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlashTestKit.Engine.CreateSequencer(frame.Layout, frame.Bulbs, new FlashOptions { Pattern = (FlashPatternId)42 }));

        IFlashSequencer sequencer = FlashTestKit.Create(frame, new FlashOptions());
        int count = frame.Layout.Placements.Count;
        Assert.Throws<ArgumentOutOfRangeException>(() => sequencer.GetBulb(count));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequencer.GetBulb(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequencer.GetWave(count));
    }
}
