using System.Diagnostics;

namespace HolidayLights.Tests.Rendering.Fakes;

/// <summary>Stand-in for core-layout's flash engine (only what the rendering tests need).</summary>
internal sealed class FakeFlashEngine : IFlashEngine
{
    public int Created;

    public IFlashSequencer CreateSequencer(LightsLayout layout, IBulbResolver bulbs, FlashOptions options)
    {
        Interlocked.Increment(ref Created);
        return new FakeSequencer(layout, bulbs, options);
    }
}

/// <summary>
/// A simplified sequencer: Don't Flash and "Stop Flashing" show every bulb lit; Flash Together alternates every step;
/// Alternating lights every other bulb of a ring; Chase Around lights one bulb in three moving clockwise; Slow Glow (and the
/// idle Dance) is the raised cosine; Dance follows note-on events per pitch class.
/// </summary>
internal sealed class FakeSequencer : IFlashSequencer
{
    private readonly FlashBulbInfo[] facts;
    private readonly BulbVisualState[] states;
    private readonly float[] groupBrightness = new float[DanceEnvelope.GroupCount];
    private bool dancing;

    /// <param name="layout">The layout.</param>
    /// <param name="bulbs">The bulbs.</param>
    /// <param name="options">The pattern.</param>
    /// <param name="stripFrames">The strip frame count reported for every bulb (2 matches the patterns' period).</param>
    public FakeSequencer(LightsLayout layout, IBulbResolver bulbs, FlashOptions options, int stripFrames = 2)
    {
        Layout = layout;
        Options = options;
        facts = new FlashBulbInfo[layout.Placements.Count];
        states = new BulbVisualState[facts.Length];
        int light = 0;
        foreach (BulbPlacement placement in layout.Placements)
        {
            BulbAnimationInfo info = bulbs.TryGetBulb(placement.BulbId, out IBulb? bulb)
                ? bulb.GetAnimation(placement.Slot, placement.Flavor)
                : new BulbAnimationInfo(BulbAnimationKind.Static, 1, 0, default);
            bool isLight = info.Kind == BulbAnimationKind.LightBulb;
            int group = isLight ? (placement.IsCorner ? DanceEnvelope.CornerGroup : light % 12) : -1;
            facts[placement.Ordinal] = new FlashBulbInfo(placement.Ordinal, info.Kind, info.FrameCount, info.LitFrame, stripFrames, group, isLight ? light : -1, -1);
            if (isLight)
            {
                light++;
            }
        }

        Step = -1;
        MoveTo(0, Stopwatch.GetTimestamp());
    }

    public LightsLayout Layout { get; }

    public FlashOptions Options { get; }

    public long Step { get; private set; }

    public ReadOnlySpan<BulbVisualState> Current => states;

    public bool IsDancing => Options.Pattern == FlashPatternId.DanceToMusic && dancing;

    public int MoveCount { get; private set; }

    public FlashBulbInfo GetBulb(int ordinal) => facts[ordinal];

    public void MoveTo(long step, long timestamp)
    {
        MoveCount++;
        Step = step;
        foreach (BulbPlacement placement in Layout.Placements)
        {
            int ordinal = placement.Ordinal;
            FlashBulbInfo info = facts[ordinal];
            if (info.Kind != BulbAnimationKind.LightBulb)
            {
                states[ordinal] = new BulbVisualState(info.Kind == BulbAnimationKind.Animation ? (int)(step % info.FrameCount) : 0, 1, 0);
                continue;
            }

            float brightness = Options.StopFlashing ? 1 : Options.Pattern switch
            {
                FlashPatternId.DontFlash => 1,
                FlashPatternId.FlashTogether => step % 2 == 0 ? 1 : 0,
                FlashPatternId.Alternating => (step + placement.RingIndex) % 2 == 0 ? 1 : 0,
                FlashPatternId.ChaseAround => ((placement.RingIndex - step) % 3 + 3) % 3 == 0 ? 1 : 0,
                FlashPatternId.SlowGlow or FlashPatternId.DanceToMusic => (float)new BrightnessWave(16, 0).Evaluate(step),
                _ => 1,
            };
            int frame = brightness >= 0.5f ? info.LitFrame : 1 - info.LitFrame;
            states[ordinal] = new BulbVisualState(frame, brightness, brightness);
        }
    }

    public FadeProfile GetFadeProfile(double stepPeriodMilliseconds)
    {
        if (!Options.SmoothFading)
        {
            return FadeProfile.Instant;
        }

        double d = Math.Min(0.4 * stepPeriodMilliseconds, 150);
        return new FadeProfile(0.6 * d, d);
    }

    public BrightnessWave? GetWave(int ordinal) =>
        facts[ordinal].Kind == BulbAnimationKind.LightBulb
        && (Options.Pattern == FlashPatternId.SlowGlow || (Options.Pattern == FlashPatternId.DanceToMusic && !dancing))
            ? new BrightnessWave(16, 0)
            : null;

    public MusicResponse ApplyMusicEvents(ReadOnlySpan<MusicEvent> events)
    {
        if (Options.Pattern != FlashPatternId.DanceToMusic || events.IsEmpty)
        {
            return MusicResponse.None;
        }

        bool modeChanged = !dancing;
        dancing = true;
        var updates = new List<DanceGroupUpdate>();
        foreach (MusicEvent musicEvent in events)
        {
            if (musicEvent.Kind != MusicEventKind.NoteOn)
            {
                continue;
            }

            int group = musicEvent.Note % 12;
            float start = groupBrightness[group];
            float peak = Math.Max(start, 0.4f + 0.6f * musicEvent.Velocity / 127f);
            groupBrightness[group] = peak;
            updates.Add(new DanceGroupUpdate(group, start, peak, musicEvent.Timestamp));
        }

        return new MusicResponse(updates, FramesChanged: false, modeChanged);
    }

    public void Sample(long timestamp, StepClock clock, Span<BulbVisualState> destination) => states.CopyTo(destination);
}

/// <summary>A music event source whose events the test publishes.</summary>
internal sealed class FakeMusicEventSource : IMusicEventSource
{
    private readonly List<Reader> readers = [];

    public int Subscriptions => readers.Count;

    public IMusicEventReader Subscribe(int capacity = 4096)
    {
        var reader = new Reader(this);
        lock (readers)
        {
            readers.Add(reader);
        }

        return reader;
    }

    public void Publish(MusicEvent musicEvent)
    {
        lock (readers)
        {
            foreach (Reader reader in readers)
            {
                reader.Queue.Enqueue(musicEvent);
            }
        }
    }

    private sealed class Reader(FakeMusicEventSource source) : IMusicEventReader
    {
        public System.Collections.Concurrent.ConcurrentQueue<MusicEvent> Queue { get; } = new();

        public int Read(Span<MusicEvent> destination)
        {
            int count = 0;
            while (count < destination.Length && Queue.TryDequeue(out MusicEvent musicEvent))
            {
                destination[count++] = musicEvent;
            }

            return count;
        }

        public void Dispose()
        {
            lock (source.readers)
            {
                source.readers.Remove(this);
            }
        }
    }
}
