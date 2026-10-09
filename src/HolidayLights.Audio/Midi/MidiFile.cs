namespace HolidayLights.Audio.Midi;

/// <summary>One channel or SysEx event at an absolute time.</summary>
/// <param name="TimeMicroseconds">Absolute time from the song start (tempo map applied).</param>
/// <param name="Status">Status byte (channel events) or 0xF0/0xF7 (SysEx).</param>
/// <param name="Data1">First data byte.</param>
/// <param name="Data2">Second data byte (0 for one-byte messages).</param>
/// <param name="SysEx">SysEx bytes (0xF0 events start with 0xF0), else null.</param>
public readonly record struct MidiEvent(long TimeMicroseconds, byte Status, byte Data1, byte Data2, byte[]? SysEx);

/// <summary>A tempo change.</summary>
/// <param name="Tick">The tick of the change.</param>
/// <param name="MicrosecondsPerQuarter">The new tempo.</param>
public readonly record struct MidiTempoChange(long Tick, int MicrosecondsPerQuarter);

/// <summary>
/// A Standard MIDI File (format 0/1, running status, meta and SysEx, tempo map).
/// Must parse all 47 shipped files (golden <c>midi.json</c>). Owner: audio.
/// </summary>
/// <remarks>
/// Parsing is tolerant like Windows' sequencer: an <c>.rmi</c> RIFF wrapper is unwrapped, unknown chunks are skipped,
/// running status survives meta and SysEx events, and a damaged or truncated track keeps the events before the damage.
/// Instances are immutable and thread-safe.
/// </remarks>
public sealed class MidiFile
{
    /// <summary>Files larger than this are not MIDI songs (the shipped ones are below 52 KB).</summary>
    private const long MaxFileBytes = 16 * 1024 * 1024;

    /// <summary>Beats of a song with an SMPTE division (no quarter notes): 120 per minute.</summary>
    private const long SmpteBeatMicroseconds = MidiTempoMap.DefaultMicrosecondsPerQuarter;

    /// <summary>Beats listed at most (almost 14 hours at 120 per minute; a damaged file can claim far more).</summary>
    private const int MaxBeats = 100_000;

    private MidiFile()
    {
    }

    /// <summary>SMF format (0 or 1).</summary>
    public int Format { get; private init; }

    /// <summary>Division (ticks per quarter note, or SMPTE).</summary>
    public int Division { get; private init; }

    /// <summary>Number of MTrk chunks read.</summary>
    public int TrackCount { get; private init; }

    /// <summary>Channel and SysEx events of all tracks, merged in time order (stable by track order).</summary>
    public IReadOnlyList<MidiEvent> Events { get; private init; } = [];

    /// <summary>Tempo changes in playback order.</summary>
    public IReadOnlyList<MidiTempoChange> TempoChanges { get; private init; } = [];

    /// <summary>Duration to the last End-of-Track (the trailing rest is kept; songs loop on it).</summary>
    public long DurationMicroseconds { get; private init; }

    /// <summary>Time of the last event that is not End-of-Track (what MCI reports as the length).</summary>
    public long LastEventMicroseconds { get; private init; }

    /// <summary>
    /// Times of the quarter-note beats (ticks 0, division, 2 x division ... before the End-of-Track) with the tempo map
    /// applied; every 500 ms for SMPTE files. They drive the beat events of "Dance to the Music" (PRODUCT-SPEC 5.10).
    /// </summary>
    public IReadOnlyList<long> BeatMicroseconds { get; private init; } = [];

    /// <summary>Reads and parses a file.</summary>
    /// <param name="path">A <c>.mid</c> or <c>.rmi</c> file.</param>
    /// <returns>The parsed song.</returns>
    /// <exception cref="InvalidDataException">Not a Standard MIDI File.</exception>
    public static MidiFile Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxFileBytes)
        {
            throw new InvalidDataException("The file is too large to be a MIDI song.");
        }

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return Parse(bytes);
    }

    /// <summary>Parses SMF bytes (an <c>.rmi</c> RIFF wrapper is unwrapped).</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The parsed song.</returns>
    /// <exception cref="InvalidDataException">Not a Standard MIDI File.</exception>
    public static MidiFile Parse(ReadOnlySpan<byte> data)
    {
        SmfDocument document = SmfReader.Read(data);
        IReadOnlyList<MidiTempoChange> tempoChanges = CollectTempoChanges(document);
        var tempoMap = new MidiTempoMap(document.Division, tempoChanges);

        long endTick = 0;
        long lastEventTick = 0;
        foreach (SmfTrack track in document.Tracks)
        {
            endTick = Math.Max(endTick, track.EndTick);
            foreach (SmfEvent e in track.Events)
            {
                if (!e.IsEndOfTrack)
                {
                    lastEventTick = Math.Max(lastEventTick, e.Tick);
                }
            }
        }

        long duration = tempoMap.ToMicroseconds(endTick);
        return new MidiFile
        {
            Format = document.Format,
            Division = document.Division,
            TrackCount = document.Tracks.Count,
            Events = MergeTracks(document, tempoMap),
            TempoChanges = tempoChanges,
            DurationMicroseconds = duration,
            LastEventMicroseconds = tempoMap.ToMicroseconds(lastEventTick),
            BeatMicroseconds = ComputeBeats(tempoMap, endTick, duration),
        };
    }

    /// <summary>Every Set Tempo of every track, in playback order (tick, then track, then file order).</summary>
    private static IReadOnlyList<MidiTempoChange> CollectTempoChanges(SmfDocument document)
    {
        var changes = new List<(long Tick, int Track, int Index, int Tempo)>();
        for (int t = 0; t < document.Tracks.Count; t++)
        {
            IReadOnlyList<SmfEvent> events = document.Tracks[t].Events;
            for (int i = 0; i < events.Count; i++)
            {
                SmfEvent e = events[i];
                if (e.IsMeta && e.MetaType == SmfEvent.SetTempoType && e.Payload is { Length: 3 } p)
                {
                    changes.Add((e.Tick, t, i, (p[0] << 16) | (p[1] << 8) | p[2]));
                }
            }
        }

        changes.Sort((a, b) => (a.Tick, a.Track, a.Index).CompareTo((b.Tick, b.Track, b.Index)));
        return changes.ConvertAll(c => new MidiTempoChange(c.Tick, c.Tempo));
    }

    /// <summary>Channel and SysEx events of every track in time order; equal ticks keep track order, then file order.</summary>
    private static MidiEvent[] MergeTracks(SmfDocument document, MidiTempoMap tempoMap)
    {
        var merged = new List<(long Tick, int Track, int Index, SmfEvent Event)>();
        for (int t = 0; t < document.Tracks.Count; t++)
        {
            IReadOnlyList<SmfEvent> events = document.Tracks[t].Events;
            for (int i = 0; i < events.Count; i++)
            {
                SmfEvent e = events[i];
                if (e.IsChannelMessage || e.IsSysEx)
                {
                    merged.Add((e.Tick, t, i, e));
                }
            }
        }

        merged.Sort((a, b) => (a.Tick, a.Track, a.Index).CompareTo((b.Tick, b.Track, b.Index)));
        var result = new MidiEvent[merged.Count];
        for (int i = 0; i < merged.Count; i++)
        {
            SmfEvent e = merged[i].Event;
            result[i] = new MidiEvent(tempoMap.ToMicroseconds(e.Tick), e.Status, e.Data1, e.Data2, e.IsSysEx ? e.Payload : null);
        }

        return result;
    }

    private static long[] ComputeBeats(MidiTempoMap tempoMap, long endTick, long durationMicroseconds)
    {
        var beats = new List<long>();
        if (tempoMap.IsSmpte)
        {
            for (long time = 0; time < durationMicroseconds && beats.Count < MaxBeats; time += SmpteBeatMicroseconds)
            {
                beats.Add(time);
            }
        }
        else
        {
            for (long tick = 0; tick < endTick && beats.Count < MaxBeats; tick += tempoMap.TicksPerQuarter)
            {
                beats.Add(tempoMap.ToMicroseconds(tick));
            }
        }

        return [.. beats];
    }
}
