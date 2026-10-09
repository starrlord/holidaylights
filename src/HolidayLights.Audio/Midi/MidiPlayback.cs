namespace HolidayLights.Audio.Midi;

/// <summary>
/// Plays one song on an output against an outside clock, without threads: the owner asks for <see cref="NextDue"/>,
/// waits until then and calls <see cref="Advance"/>. Deterministic, so tests drive it with explicit times.
/// </summary>
/// <remarks>
/// The song starts with GM System On; 50 ms later (the settle time a GM/GS device needs) CC7 goes to every channel and
/// song time 0 begins. CC7 is always the song's value scaled by the level (Music Box volume x fade), and is sent again
/// after any GM/GS/XG reset in the song. Pausing sends All Notes Off and All Sound Off; resuming re-sends program and
/// controller state. The end (End-of-Track, trailing rest kept) and Stop send CC 120, 123 and 121 on all 16 channels
/// (PRODUCT-SPEC 6.1.3).
/// </remarks>
internal sealed class MidiPlayback
{
    /// <summary>GM System On, sent before each song.</summary>
    public static readonly byte[] GmSystemOn = [0xF0, 0x7E, 0x7F, 0x09, 0x01, 0xF7];

    /// <summary>Time a device needs after GM System On before it takes messages (Roland GS devices ask for 50 ms).</summary>
    public static readonly TimeSpan ResetSettleTime = TimeSpan.FromMilliseconds(50);

    private const int ChannelCount = 16;
    private const byte VolumeController = 7;
    private const byte AllSoundOff = 120;
    private const byte ResetAllControllers = 121;
    private const byte AllNotesOff = 123;
    private const byte BeatVelocity = 127;

    /// <summary>SysEx messages after which a device is back at its defaults.</summary>
    private static readonly byte[][] DeviceResets =
    [
        GmSystemOn,
        [0xF0, 0x7E, 0x7F, 0x09, 0x03, 0xF7],
        [0xF0, 0x41, 0x10, 0x42, 0x12, 0x40, 0x00, 0x7F, 0x00, 0x41, 0xF7],
        [0xF0, 0x43, 0x10, 0x4C, 0x00, 0x00, 0x7E, 0x00, 0xF7],
    ];

    private readonly MidiFile song;
    private readonly IMidiOutput output;
    private readonly long frequency;
    private readonly MidiChannelState[] channels = new MidiChannelState[ChannelCount];
    private readonly byte[] sentVolume = new byte[ChannelCount];
    private int nextEvent;
    private int nextBeat;
    private long origin;
    private long pausedElapsed;
    private double level;
    private bool volumePending;

    /// <summary>Prepares a song.</summary>
    /// <param name="song">The song.</param>
    /// <param name="output">The output.</param>
    /// <param name="frequency">Clock ticks per second.</param>
    /// <param name="level">The initial level 0-1 (volume x fade).</param>
    public MidiPlayback(MidiFile song, IMidiOutput output, long frequency, double level)
    {
        this.song = song;
        this.output = output;
        this.frequency = frequency;
        this.level = Math.Clamp(level, 0, 1);
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            channels[ch] = new MidiChannelState();
        }
    }

    /// <summary>True between <see cref="Pause"/> and <see cref="Resume"/>.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>True when the song reached its end or was stopped.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>When something is due next (the start, an event, a beat or the end), or <see cref="long.MaxValue"/> while paused or finished.</summary>
    public long NextDue
    {
        get
        {
            if (IsPaused || IsFinished)
            {
                return long.MaxValue;
            }

            if (volumePending)
            {
                return origin;
            }

            long due = ToTicks(song.DurationMicroseconds);
            if (nextEvent < song.Events.Count)
            {
                due = Math.Min(due, ToTicks(song.Events[nextEvent].TimeMicroseconds));
            }

            if (nextBeat < song.BeatMicroseconds.Count)
            {
                due = Math.Min(due, ToTicks(song.BeatMicroseconds[nextBeat]));
            }

            return origin + due;
        }
    }

    /// <summary>Starts the song: GM System On now; CC7 on every channel and song time 0 after <see cref="ResetSettleTime"/>.</summary>
    /// <param name="now">The clock time.</param>
    public void Start(long now)
    {
        output.SendSysEx(GmSystemOn);
        foreach (MidiChannelState channel in channels)
        {
            channel.Reset();
        }

        volumePending = true;
        origin = now + (long)(ResetSettleTime.TotalSeconds * frequency);
    }

    /// <summary>Sends everything due at <paramref name="now"/>.</summary>
    /// <param name="now">The clock time.</param>
    /// <param name="sent">Receives a note-on event for each note-on sent and a beat event for each quarter note passed (timestamp = <paramref name="now"/>).</param>
    /// <returns>True when the song reached its End-of-Track.</returns>
    public bool Advance(long now, List<MusicEvent> sent)
    {
        if (IsPaused || IsFinished)
        {
            return IsFinished;
        }

        long elapsed = now - origin;
        if (elapsed < 0)
        {
            return false;
        }

        if (volumePending)
        {
            volumePending = false;
            SendVolumes(force: true);
        }

        IReadOnlyList<MidiEvent> events = song.Events;
        while (nextEvent < events.Count && ToTicks(events[nextEvent].TimeMicroseconds) <= elapsed)
        {
            Dispatch(events[nextEvent++], now, sent);
        }

        IReadOnlyList<long> beats = song.BeatMicroseconds;
        while (nextBeat < beats.Count && ToTicks(beats[nextBeat]) <= elapsed)
        {
            sent.Add(new MusicEvent(MusicEventKind.Beat, now, 0, 0, BeatVelocity));
            nextBeat++;
        }

        if (ToTicks(song.DurationMicroseconds) <= elapsed)
        {
            Stop();
        }

        return IsFinished;
    }

    /// <summary>Pauses: the position is kept and every channel is silenced.</summary>
    /// <param name="now">The clock time.</param>
    public void Pause(long now)
    {
        if (IsPaused || IsFinished)
        {
            return;
        }

        pausedElapsed = now - origin;
        IsPaused = true;
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            output.SendShort((byte)(0xB0 | ch), AllNotesOff, 0);
            output.SendShort((byte)(0xB0 | ch), AllSoundOff, 0);
        }
    }

    /// <summary>Resumes from the paused position, sending program and controller state again first.</summary>
    /// <param name="now">The clock time.</param>
    public void Resume(long now)
    {
        if (!IsPaused)
        {
            return;
        }

        for (int ch = 0; ch < ChannelCount; ch++)
        {
            sentVolume[ch] = Scale(channels[ch].SongVolume);
            channels[ch].Replay(ch, output, sentVolume[ch]);
        }

        origin = now - pausedElapsed;
        IsPaused = false;
    }

    /// <summary>Changes the level (Music Box volume x fade); CC7 is sent at once where its scaled value changed.</summary>
    /// <param name="newLevel">The level 0-1.</param>
    public void SetLevel(double newLevel)
    {
        level = Math.Clamp(newLevel, 0, 1);
        if (!IsPaused && !IsFinished && !volumePending)
        {
            SendVolumes(force: false);
        }
    }

    /// <summary>The position in the song.</summary>
    /// <param name="now">The clock time.</param>
    /// <returns>Microseconds from the start, at most the duration.</returns>
    public long PositionMicroseconds(long now)
    {
        long elapsed = IsPaused ? pausedElapsed : now - origin;
        long microseconds = (long)((Int128)Math.Max(0, elapsed) * 1_000_000 / frequency);
        return Math.Min(microseconds, song.DurationMicroseconds);
    }

    /// <summary>Stops: CC 120, 123 and 121 on all 16 channels.</summary>
    public void Stop()
    {
        if (IsFinished)
        {
            return;
        }

        IsFinished = true;
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            output.SendShort((byte)(0xB0 | ch), AllSoundOff, 0);
            output.SendShort((byte)(0xB0 | ch), AllNotesOff, 0);
            output.SendShort((byte)(0xB0 | ch), ResetAllControllers, 0);
        }
    }

    private void Dispatch(in MidiEvent e, long now, List<MusicEvent> sent)
    {
        if (e.SysEx is { } sysEx)
        {
            output.SendSysEx(sysEx);
            if (IsDeviceReset(sysEx))
            {
                ResetChannels();
            }

            return;
        }

        int channel = e.Status & 0x0F;
        int kind = e.Status & 0xF0;
        channels[channel].Observe(e.Status, e.Data1, e.Data2);
        if (kind == 0xB0 && e.Data1 == VolumeController)
        {
            sentVolume[channel] = Scale(e.Data2);
            output.SendShort(e.Status, VolumeController, sentVolume[channel]);
            return;
        }

        output.SendShort(e.Status, e.Data1, e.Data2);
        if (kind == 0x90 && e.Data2 > 0)
        {
            sent.Add(new MusicEvent(MusicEventKind.NoteOn, now, (byte)channel, e.Data1, e.Data2));
        }
    }

    private void ResetChannels()
    {
        foreach (MidiChannelState channel in channels)
        {
            channel.Reset();
        }

        SendVolumes(force: true);
    }

    private void SendVolumes(bool force)
    {
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            byte volume = Scale(channels[ch].SongVolume);
            if (force || volume != sentVolume[ch])
            {
                sentVolume[ch] = volume;
                output.SendShort((byte)(0xB0 | ch), VolumeController, volume);
            }
        }
    }

    private byte Scale(byte songVolume) => (byte)Math.Clamp(Math.Round(songVolume * level, MidpointRounding.AwayFromZero), 0, 127);

    private long ToTicks(long microseconds) => (long)((Int128)microseconds * frequency / 1_000_000);

    private static bool IsDeviceReset(byte[] sysEx)
    {
        foreach (byte[] reset in DeviceResets)
        {
            if (sysEx.AsSpan().SequenceEqual(reset))
            {
                return true;
            }
        }

        return false;
    }
}
