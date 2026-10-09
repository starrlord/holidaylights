using System.Diagnostics;

namespace HolidayLights.Core.Flash;

/// <summary>
/// "Dance to the Music" (PRODUCT-SPEC 5.7 pattern 8, 5.10): light bulbs follow the music by pitch class, animation bulbs
/// step on the beat; without music playing the pattern is exactly Slow Glow.
/// </summary>
/// <remarks>
/// <para><b>Groups.</b> Side light bulb <c>q</c> is in group <c>q mod 12</c>, corner light bulbs in group C; each group has one
/// brightness B with a 30 ms linear attack and a decay <c>B x exp(-dt / 250 ms)</c> (<see cref="DanceEnvelope"/>).</para>
/// <para><b>MIDI.</b> Channel 10 notes 35 and 36 (bass drum) raise every group to <c>v / 127</c>; other channel-10 notes raise
/// group C to <c>0.4 + 0.6 v / 127</c>; any other note n raises group <c>n mod 12</c> to <c>0.4 + 0.6 v / 127</c>. A raise
/// only takes effect when it is above the group's current level (the peak while it is still rising).
/// <b>Audio files</b> send beats but no notes: an <see cref="MusicEventKind.AudioBeat"/> raises every group to 1; the
/// tempo-map <see cref="MusicEventKind.Beat"/>s of MIDI songs only move the animation bulbs.</para>
/// <para><b>Frames.</b> Animation bulbs advance one frame per beat, at most every 120 ms; after 2 s without a beat they
/// advance every step. When the music starts they continue from the frame Slow Glow showed.</para>
/// <para><b>Limit Flashing.</b> A group is retriggered at most every 333 ms.</para>
/// <para>Music events cannot be replayed, so this state follows the events received, whatever step the lights show.</para>
/// </remarks>
internal sealed class DanceModel
{
    /// <summary>Animation bulbs take at most one frame per this many milliseconds from beats.</summary>
    public const double BeatFrameMinimumMilliseconds = 120;

    /// <summary>Without a beat for this long, animation bulbs advance every step.</summary>
    public const double NoBeatMilliseconds = 2000;

    // General MIDI: channel 10 (index 9) is percussion; notes 35 and 36 are the bass drums.
    private const byte BassDrum1 = 35;
    private const byte BassDrum2 = 36;
    private const byte DrumChannel = 9;
    private const double MelodyFloor = 0.4;
    private const double MelodyRange = 0.6;

    private readonly bool[] groupInUse = new bool[DanceEnvelope.GroupCount];
    private readonly GroupEnvelope[] envelopes = new GroupEnvelope[DanceEnvelope.GroupCount];
    private readonly bool limitFlashing;
    private long lastBeatOrStart;
    private long lastBeatFrame;
    private bool hasBeatFrame;

    /// <summary>Creates the model.</summary>
    /// <param name="bulbs">The bulb facts (for the groups in use).</param>
    /// <param name="limitFlashing">"Limit Flashing to 3 Flashes per Second".</param>
    public DanceModel(FlashBulbTable bulbs, bool limitFlashing)
    {
        this.limitFlashing = limitFlashing;
        foreach (int group in bulbs.MusicGroups)
        {
            if (group >= 0)
            {
                groupInUse[group] = true;
            }
        }
    }

    /// <summary>True while music plays (between a song start or resume and its stop or pause).</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>The frame counter of animation bulbs while music plays (the frame is the counter mod the frame count).</summary>
    public long FrameCounter { get; private set; }

    /// <summary>Applies a batch of music events.</summary>
    /// <param name="events">Events in timestamp order.</param>
    /// <param name="currentStep">The step the lights show (frames continue from Slow Glow's when the music starts).</param>
    /// <param name="idleBrightness">Slow Glow's brightness now (the groups start from it when the music starts).</param>
    /// <returns>What changed.</returns>
    public MusicResponse Apply(ReadOnlySpan<MusicEvent> events, long currentStep, double idleBrightness)
    {
        bool wasPlaying = IsPlaying;
        long startedAt = 0;
        bool framesChanged = false;
        Span<double> levels = stackalloc double[DanceEnvelope.GroupCount];
        Span<long> times = stackalloc long[DanceEnvelope.GroupCount];
        levels.Fill(-1);
        foreach (MusicEvent e in events)
        {
            switch (e.Kind)
            {
                case MusicEventKind.SongStarted:
                case MusicEventKind.Resumed:
                    lastBeatOrStart = e.Timestamp;
                    Play(e.Timestamp, currentStep, ref startedAt);
                    break;
                case MusicEventKind.SongStopped:
                case MusicEventKind.Paused:
                    IsPlaying = false;
                    levels.Fill(-1);
                    break;
                case MusicEventKind.NoteOn when e.Velocity > 0:
                    Play(e.Timestamp, currentStep, ref startedAt);
                    RaiseForNote(e, levels, times);
                    break;
                case MusicEventKind.Beat:
                case MusicEventKind.AudioBeat:
                    Play(e.Timestamp, currentStep, ref startedAt);
                    lastBeatOrStart = e.Timestamp;
                    framesChanged |= AdvanceOnBeat(e.Timestamp);
                    if (e.Kind == MusicEventKind.AudioBeat)
                    {
                        for (int g = 0; g < levels.Length; g++)
                        {
                            Raise(levels, times, g, 1.0, e.Timestamp);
                        }
                    }

                    break;
            }
        }

        bool modeChanged = IsPlaying != wasPlaying;
        if (!IsPlaying)
        {
            return modeChanged || framesChanged ? new MusicResponse([], framesChanged, modeChanged) : MusicResponse.None;
        }

        var updates = new List<DanceGroupUpdate>();
        for (int g = 0; g < DanceEnvelope.GroupCount; g++)
        {
            if (!groupInUse[g])
            {
                continue;
            }

            bool changed = false;
            if (!wasPlaying)
            {
                envelopes[g] = new GroupEnvelope(idleBrightness, idleBrightness, startedAt, 0, false);
                changed = true;
            }

            if (levels[g] >= 0 && Trigger(g, levels[g], times[g]))
            {
                changed = true;
            }

            if (changed)
            {
                GroupEnvelope envelope = envelopes[g];
                updates.Add(new DanceGroupUpdate(g, (float)envelope.Start, (float)envelope.Peak, envelope.Timestamp));
            }
        }

        return updates.Count == 0 && !framesChanged && !modeChanged ? MusicResponse.None : new MusicResponse(updates, framesChanged, modeChanged);
    }

    /// <summary>The brightness B of a group at a moment.</summary>
    /// <param name="group">0-12.</param>
    /// <param name="timestamp">A <see cref="Stopwatch"/> timestamp.</param>
    /// <returns>0-1.</returns>
    public float Brightness(int group, long timestamp)
    {
        GroupEnvelope envelope = envelopes[group];
        double value = DanceEnvelope.BrightnessAt(envelope.Start, envelope.Peak, Milliseconds(timestamp - envelope.Timestamp));
        return (float)Math.Clamp(value, 0, 1);
    }

    /// <summary>Advances animation bulbs by the steps the lights moved forward once 2 s passed without a beat.</summary>
    /// <param name="steps">Steps moved forward.</param>
    /// <param name="timestamp">When the new step begins.</param>
    public void OnSteps(long steps, long timestamp)
    {
        if (IsPlaying && steps > 0 && Milliseconds(timestamp - lastBeatOrStart) >= NoBeatMilliseconds)
        {
            FrameCounter += steps;
        }
    }

    private static double Milliseconds(long stopwatchTicks) => stopwatchTicks * 1000.0 / Stopwatch.Frequency;

    private static void Raise(Span<double> levels, Span<long> times, int group, double level, long timestamp)
    {
        if (level > levels[group])
        {
            levels[group] = level;
            times[group] = timestamp;
        }
    }

    private static void RaiseForNote(MusicEvent note, Span<double> levels, Span<long> times)
    {
        double velocity = note.Velocity / 127.0;
        double melody = MelodyFloor + (MelodyRange * velocity);
        if (note.Channel != DrumChannel)
        {
            Raise(levels, times, note.Note % 12, melody, note.Timestamp);
        }
        else if (note.Note is BassDrum1 or BassDrum2)
        {
            for (int g = 0; g < levels.Length; g++)
            {
                Raise(levels, times, g, velocity, note.Timestamp);
            }
        }
        else
        {
            Raise(levels, times, DanceEnvelope.CornerGroup, melody, note.Timestamp);
        }
    }

    private void Play(long timestamp, long currentStep, ref long startedAt)
    {
        if (IsPlaying)
        {
            return;
        }

        IsPlaying = true;
        startedAt = timestamp;
        lastBeatOrStart = timestamp;
        FrameCounter = currentStep;
    }

    private bool AdvanceOnBeat(long timestamp)
    {
        if (hasBeatFrame && Milliseconds(timestamp - lastBeatFrame) < BeatFrameMinimumMilliseconds)
        {
            return false;
        }

        FrameCounter++;
        lastBeatFrame = timestamp;
        hasBeatFrame = true;
        return true;
    }

    // A raise starts a new envelope only above the group's level: its peak while it is still rising, else its decayed value.
    private bool Trigger(int group, double level, long timestamp)
    {
        GroupEnvelope envelope = envelopes[group];
        double since = Milliseconds(timestamp - envelope.Timestamp);
        double current = DanceEnvelope.BrightnessAt(envelope.Start, envelope.Peak, since);
        double reference = since < DanceEnvelope.AttackMilliseconds ? Math.Max(envelope.Start, envelope.Peak) : current;
        if (level <= reference + 1e-6)
        {
            return false;
        }

        if (limitFlashing && envelope.HasTriggered && Milliseconds(timestamp - envelope.LastTrigger) < FlashClock.LimitedRetriggerMilliseconds)
        {
            return false;
        }

        envelopes[group] = new GroupEnvelope(current, level, timestamp, timestamp, true);
        return true;
    }

    private readonly record struct GroupEnvelope(double Start, double Peak, long Timestamp, long LastTrigger, bool HasTriggered);
}
