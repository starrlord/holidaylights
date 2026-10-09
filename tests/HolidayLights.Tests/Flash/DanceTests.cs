using HolidayLights.Core.Flash;
using HolidayLights.Tests.Layout;

namespace HolidayLights.Tests.Flash;

/// <summary>"Dance to the Music" (PRODUCT-SPEC 5.7 pattern 8, 5.10, acceptance 7.5 #16).</summary>
public sealed class DanceTests
{
    private static readonly long T0 = FlashTestKit.Ms(10_000);

    private static (LightsLayout Layout, IBulbResolver Bulbs) DanceFrame()
    {
        var light = new FakeBulb("addon:Light", 20, 20) { Kind = BulbAnimationKind.LightBulb };
        var animation = new FakeBulb("addon:Anim", 20, 20) { Phases = 4 };
        var bulbs = new FakeBulbResolver([light, animation]);
        var arrangement = new SlotAssignment
        {
            Top = [light.Id], Bottom = [light.Id], Right = [animation.Id], Left = [animation.Id],
            TopLeft = light.Id, TopRight = light.Id, BottomLeft = light.Id, BottomRight = light.Id,
        };
        return (FlashTestKit.LayoutEngine.Layout([new LayoutTarget("d", new RectI(0, 0, 800, 600), 1.0)], arrangement, bulbs), bulbs);
    }

    private static IFlashSequencer Dance(bool smoothFading = true, bool limitFlashing = false) =>
        FlashTestKit.Create(DanceFrame(), new FlashOptions { Pattern = FlashPatternId.DanceToMusic, SmoothFading = smoothFading, LimitFlashing = limitFlashing });

    private static MusicEvent Start(long at) => new(MusicEventKind.SongStarted, at, 0, 0, 0);

    private static MusicEvent Note(long at, byte note, byte velocity, byte channel = 0) => new(MusicEventKind.NoteOn, at, channel, note, velocity);

    private static MusicEvent Beat(long at) => new(MusicEventKind.Beat, at, 0, 0, 127);

    private static MusicEvent AudioBeat(long at) => new(MusicEventKind.AudioBeat, at, 0, 0, 127);

    private static DanceGroupUpdate Group(MusicResponse response, int group) => Assert.Single(response.Groups, g => g.Group == group);

    private static int SideLightOfGroup(IFlashSequencer sequencer, int group) =>
        sequencer.Layout.Placements.First(p => !p.IsCorner && sequencer.GetBulb(p.Ordinal).MusicGroup == group).Ordinal;

    [Fact]
    public void Groups_ArePitchClassesOfTheRingAndTheCorners()
    {
        IFlashSequencer sequencer = Dance();
        foreach (BulbPlacement p in sequencer.Layout.Placements)
        {
            FlashBulbInfo info = sequencer.GetBulb(p.Ordinal);
            int expected = info.Kind != BulbAnimationKind.LightBulb ? -1 : p.IsCorner ? DanceEnvelope.CornerGroup : info.LightIndex % 12;
            Assert.Equal(expected, info.MusicGroup);
        }

        Assert.Equal(13, sequencer.Layout.Placements.Select(p => sequencer.GetBulb(p.Ordinal).MusicGroup).Where(g => g >= 0).Distinct().Count());
    }

    [Fact]
    public void WithoutMusic_DanceIsExactlySlowGlow()
    {
        var frame = DanceFrame();
        IFlashSequencer dance = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.DanceToMusic });
        IFlashSequencer glow = FlashTestKit.Create(frame, new FlashOptions { Pattern = FlashPatternId.SlowGlow });
        StepClock clock = StepClock.Start(0, 3);
        var a = new BulbVisualState[frame.Layout.Placements.Count];
        var b = new BulbVisualState[a.Length];
        for (int step = 0; step < 40; step++)
        {
            dance.MoveTo(step, FlashTestKit.Ms(step * 180));
            glow.MoveTo(step, FlashTestKit.Ms(step * 180));
            Assert.True(glow.Current.SequenceEqual(dance.Current));
            dance.Sample(FlashTestKit.Ms((step * 180) + 77), clock, a);
            glow.Sample(FlashTestKit.Ms((step * 180) + 77), clock, b);
            Assert.Equal(b, a);
        }

        Assert.False(dance.IsDancing);
        Assert.All(frame.Layout.Placements, p => Assert.Equal(glow.GetWave(p.Ordinal), dance.GetWave(p.Ordinal)));
        Assert.Equal(FadeProfile.Instant, dance.GetFadeProfile(300));
    }

    [Fact]
    public void SongStart_SwitchesToTheMusicFromTheCurrentGlow()
    {
        IFlashSequencer sequencer = Dance();
        sequencer.MoveTo(8, T0);
        MusicResponse response = sequencer.ApplyMusicEvents([Start(T0)]);

        Assert.True(response.ModeChanged);
        Assert.True(sequencer.IsDancing);
        Assert.Equal(13, response.Groups.Count);
        double glow = new BrightnessWave(16, 0).Evaluate(8.5);
        Assert.All(response.Groups, g => Assert.Equal((float)glow, g.StartBrightness, 5));
        Assert.All(response.Groups, g => Assert.Equal(g.StartBrightness, g.PeakBrightness));
        Assert.All(response.Groups, g => Assert.Equal(T0, g.Timestamp));
        Assert.Null(sequencer.GetWave(SideLightOfGroup(sequencer, 0)));
    }

    [Fact]
    public void MelodyNotes_LightTheirPitchClassAndDecay()
    {
        IFlashSequencer sequencer = Dance();
        sequencer.ApplyMusicEvents([Start(T0)]);
        long t = T0 + FlashTestKit.Ms(500);
        MusicResponse response = sequencer.ApplyMusicEvents([Note(t, 61, 127), Note(t, 62, 64)]);

        DanceGroupUpdate cSharp = Group(response, 1);
        Assert.Equal(1f, cSharp.PeakBrightness, 6);
        Assert.Equal(t, cSharp.Timestamp);
        Assert.Equal((float)(0.4 + (0.6 * 64 / 127.0)), Group(response, 2).PeakBrightness, 6);
        Assert.Equal(2, response.Groups.Count);
        Assert.False(response.ModeChanged);

        // The light follows the envelope: up over 30 ms, then down by e every 250 ms.
        int bulb = SideLightOfGroup(sequencer, 1);
        StepClock clock = StepClock.Start(T0, 5);
        var states = new BulbVisualState[sequencer.Layout.Placements.Count];
        sequencer.Sample(t + FlashTestKit.Ms(30), clock, states);
        Assert.Equal(1f, states[bulb].Brightness, 4);
        Assert.Equal(states[bulb].Brightness, states[bulb].Glow);
        sequencer.Sample(t + FlashTestKit.Ms(280), clock, states);
        Assert.Equal((float)Math.Exp(-1), states[bulb].Brightness, 4);
    }

    [Fact]
    public void Drums_BassDrumLightsEveryGroupAndOtherDrumsTheCorners()
    {
        IFlashSequencer sequencer = Dance();
        sequencer.ApplyMusicEvents([Start(T0)]);
        long t = T0 + FlashTestKit.Ms(2000);
        MusicResponse bass = sequencer.ApplyMusicEvents([Note(t, 36, 100, channel: 9)]);
        Assert.Equal(13, bass.Groups.Count);
        Assert.All(bass.Groups, g => Assert.Equal(100 / 127f, g.PeakBrightness, 5));

        long later = t + FlashTestKit.Ms(2000);
        MusicResponse hat = sequencer.ApplyMusicEvents([Note(later, 42, 127, channel: 9)]);
        DanceGroupUpdate corners = Assert.Single(hat.Groups);
        Assert.Equal(DanceEnvelope.CornerGroup, corners.Group);
        Assert.Equal(1f, corners.PeakBrightness, 6);
    }

    [Fact]
    public void Raises_OnlyStartAnEnvelopeAboveTheCurrentLevel()
    {
        IFlashSequencer sequencer = Dance();
        sequencer.ApplyMusicEvents([Start(T0)]);
        long t = T0 + FlashTestKit.Ms(3000);
        Assert.Single(sequencer.ApplyMusicEvents([Note(t, 60, 127)]).Groups);

        // During the attack the level is the coming peak: a softer note does nothing.
        Assert.Empty(sequencer.ApplyMusicEvents([Note(t + FlashTestKit.Ms(20), 72, 64)]).Groups);

        // After a second of decay (B = e^-3.88) a soft note starts a new envelope from the decayed value.
        long late = t + FlashTestKit.Ms(1000);
        DanceGroupUpdate update = Group(sequencer.ApplyMusicEvents([Note(late, 48, 10)]), 0);
        Assert.Equal((float)Math.Exp(-(1000 - 30) / 250.0), update.StartBrightness, 5);
        Assert.Equal((float)(0.4 + (0.6 * 10 / 127.0)), update.PeakBrightness, 5);
    }

    [Fact]
    public void EventsInOneBatch_AreCoalescedPerGroup()
    {
        IFlashSequencer sequencer = Dance();
        sequencer.ApplyMusicEvents([Start(T0)]);
        long t = T0 + FlashTestKit.Ms(3000);
        MusicResponse response = sequencer.ApplyMusicEvents([Note(t, 60, 20), Note(t + FlashTestKit.Ms(10), 72, 127), Note(t + FlashTestKit.Ms(20), 48, 60)]);
        DanceGroupUpdate group = Assert.Single(response.Groups);
        Assert.Equal(1f, group.PeakBrightness, 6);
        Assert.Equal(t + FlashTestKit.Ms(10), group.Timestamp);
    }

    [Fact]
    public void LimitFlashing_RetriggersAGroupAtMostEveryThirdOfASecond()
    {
        IFlashSequencer sequencer = Dance(limitFlashing: true);
        sequencer.ApplyMusicEvents([Start(T0)]);
        long t = T0 + FlashTestKit.Ms(3000);
        Assert.Single(sequencer.ApplyMusicEvents([Note(t, 60, 60)]).Groups);
        Assert.Empty(sequencer.ApplyMusicEvents([Note(t + FlashTestKit.Ms(200), 60, 127)]).Groups);
        Assert.Empty(sequencer.ApplyMusicEvents([Note(t + FlashTestKit.Ms(330), 60, 127)]).Groups);
        Assert.Single(sequencer.ApplyMusicEvents([Note(t + FlashTestKit.Ms(340), 60, 127)]).Groups);

        IFlashSequencer unlimited = Dance();
        unlimited.ApplyMusicEvents([Start(T0)]);
        unlimited.ApplyMusicEvents([Note(t, 60, 60)]);
        Assert.Single(unlimited.ApplyMusicEvents([Note(t + FlashTestKit.Ms(200), 60, 127)]).Groups);
    }

    [Fact]
    public void AudioBeats_LightEveryGroupAndStepTheFrames()
    {
        IFlashSequencer sequencer = Dance();
        sequencer.ApplyMusicEvents([Start(T0)]);
        MusicResponse beat = sequencer.ApplyMusicEvents([AudioBeat(T0 + FlashTestKit.Ms(500))]);
        Assert.Equal(13, beat.Groups.Count);
        Assert.All(beat.Groups, g => Assert.Equal(1f, g.PeakBrightness));
        Assert.True(beat.FramesChanged);
    }

    [Fact]
    public void MidiTempoBeats_OnlyStepTheFrames()
    {
        // A MIDI song that opens with a rest: its tempo-map beats come before any note and still light nothing.
        IFlashSequencer sequencer = Dance();
        sequencer.ApplyMusicEvents([Start(T0)]);
        MusicResponse restBeat = sequencer.ApplyMusicEvents([Beat(T0 + FlashTestKit.Ms(500))]);
        Assert.Empty(restBeat.Groups);
        Assert.True(restBeat.FramesChanged);

        // The first beat of a song in the same batch as its first note: only the note's group (E = 4) rises.
        IFlashSequencer other = Dance();
        other.MoveTo(8, T0);
        MusicResponse response = other.ApplyMusicEvents([Start(T0), Beat(T0), Note(T0, 64, 127)]);
        float glow = (float)new BrightnessWave(16, 0).Evaluate(8.5);
        Assert.Equal(13, response.Groups.Count);
        Assert.Equal(1f, Group(response, 4).PeakBrightness, 6);
        Assert.All(response.Groups.Where(g => g.Group != 4), g => Assert.Equal(glow, g.PeakBrightness, 5));
        Assert.True(response.FramesChanged);
    }

    [Fact]
    public void Beats_StepTheAnimationBulbsAtMostEvery120Milliseconds()
    {
        IFlashSequencer sequencer = Dance();
        sequencer.MoveTo(5, T0);
        sequencer.ApplyMusicEvents([Start(T0)]);
        int animated = sequencer.Layout.Placements.First(p => sequencer.GetBulb(p.Ordinal).Kind == BulbAnimationKind.Animation).Ordinal;
        Assert.Equal(1, sequencer.Current[animated].Frame); // continues from Slow Glow's frame 5 mod 4

        sequencer.ApplyMusicEvents([Beat(T0 + FlashTestKit.Ms(100))]);
        Assert.Equal(2, sequencer.Current[animated].Frame);
        MusicResponse quick = sequencer.ApplyMusicEvents([Beat(T0 + FlashTestKit.Ms(200))]);
        Assert.False(quick.FramesChanged);
        Assert.Equal(2, sequencer.Current[animated].Frame);
        sequencer.ApplyMusicEvents([Beat(T0 + FlashTestKit.Ms(230))]);
        Assert.Equal(3, sequencer.Current[animated].Frame);

        // Steps do not move the frames while beats keep coming...
        sequencer.MoveTo(6, T0 + FlashTestKit.Ms(1500));
        Assert.Equal(3, sequencer.Current[animated].Frame);

        // ...but after 2 s without a beat the animation bulbs advance every step.
        sequencer.MoveTo(7, T0 + FlashTestKit.Ms(2300));
        Assert.Equal(0, sequencer.Current[animated].Frame);
        sequencer.MoveTo(9, T0 + FlashTestKit.Ms(2900));
        Assert.Equal(2, sequencer.Current[animated].Frame);
    }

    [Fact]
    public void SongStopAndPause_ReturnToSlowGlowAndResumeDances()
    {
        IFlashSequencer sequencer = Dance();
        sequencer.ApplyMusicEvents([Start(T0), Note(T0, 60, 127)]);
        Assert.True(sequencer.IsDancing);

        MusicResponse stop = sequencer.ApplyMusicEvents([new MusicEvent(MusicEventKind.SongStopped, T0 + FlashTestKit.Ms(4000), 0, 0, 0)]);
        Assert.True(stop.ModeChanged);
        Assert.Empty(stop.Groups);
        Assert.False(sequencer.IsDancing);
        int light = SideLightOfGroup(sequencer, 0);
        Assert.Equal(new BrightnessWave(16, 0), sequencer.GetWave(light));

        MusicResponse resume = sequencer.ApplyMusicEvents([new MusicEvent(MusicEventKind.Resumed, T0 + FlashTestKit.Ms(5000), 0, 0, 0)]);
        Assert.True(resume.ModeChanged);
        Assert.True(sequencer.IsDancing);
        MusicResponse pause = sequencer.ApplyMusicEvents([new MusicEvent(MusicEventKind.Paused, T0 + FlashTestKit.Ms(6000), 0, 0, 0)]);
        Assert.True(pause.ModeChanged);
        Assert.False(sequencer.IsDancing);
        Assert.Same(MusicResponse.None, sequencer.ApplyMusicEvents([new MusicEvent(MusicEventKind.Paused, T0 + FlashTestKit.Ms(6100), 0, 0, 0)]));
    }

    [Fact]
    public void NotesWithoutASongStart_StartTheDanceAndSilentNotesAreIgnored()
    {
        IFlashSequencer sequencer = Dance();
        Assert.Same(MusicResponse.None, sequencer.ApplyMusicEvents([Note(T0, 60, 0)]));
        Assert.False(sequencer.IsDancing);
        MusicResponse response = sequencer.ApplyMusicEvents([Note(T0, 60, 90)]);
        Assert.True(response.ModeChanged);
        Assert.True(sequencer.IsDancing);
        Assert.Equal(13, response.Groups.Count);
    }

    [Fact]
    public void WithoutSmoothFading_TheMusicShowsAsLitOrDark()
    {
        IFlashSequencer sequencer = Dance(smoothFading: false);
        sequencer.ApplyMusicEvents([Start(T0)]);
        long t = T0 + FlashTestKit.Ms(3000);
        sequencer.ApplyMusicEvents([Note(t, 60, 127)]);
        int bulb = SideLightOfGroup(sequencer, 0);
        Assert.Equal(1f, sequencer.Current[bulb].Brightness);

        StepClock clock = StepClock.Start(T0, 5);
        var states = new BulbVisualState[sequencer.Layout.Placements.Count];
        sequencer.Sample(t + FlashTestKit.Ms(150), clock, states); // B = e^-0.48 = 0.62
        Assert.Equal(1f, states[bulb].Brightness);
        sequencer.Sample(t + FlashTestKit.Ms(260), clock, states); // B = e^-0.92 = 0.40
        Assert.Equal(0f, states[bulb].Brightness);
        Assert.Equal(0f, states[bulb].Glow);
    }

    [Fact]
    public void EmptyBatches_ChangeNothing()
    {
        IFlashSequencer sequencer = Dance();
        Assert.Same(MusicResponse.None, sequencer.ApplyMusicEvents([]));
        Assert.False(sequencer.IsDancing);
    }
}
