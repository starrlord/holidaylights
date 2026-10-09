namespace HolidayLights.Audio.Midi;

/// <summary>
/// Converts ticks to microseconds exactly as the golden data (<c>midi.json</c>) does: 500,000 µs per quarter
/// note until the first Set Tempo, then the sum over tempo segments of <c>ticks x µs-per-quarter</c>, divided by the
/// division and rounded down. SMPTE divisions use frames per second x ticks per frame (29 = 29.97 drop frame).
/// </summary>
internal sealed class MidiTempoMap
{
    /// <summary>The tempo of a song without Set Tempo events (120 beats per minute).</summary>
    public const int DefaultMicrosecondsPerQuarter = 500_000;

    private readonly int division;
    private readonly bool smpte;
    private readonly long[] segmentTicks;
    private readonly int[] segmentTempos;
    private readonly Int128[] segmentSums;

    /// <summary>Builds the map.</summary>
    /// <param name="division">The raw MThd division.</param>
    /// <param name="changes">Tempo changes in playback order.</param>
    public MidiTempoMap(int division, IReadOnlyList<MidiTempoChange> changes)
    {
        this.division = division;
        smpte = (division & 0x8000) != 0;
        var ticks = new List<long> { 0 };
        var tempos = new List<int> { DefaultMicrosecondsPerQuarter };
        var sums = new List<Int128> { 0 };
        foreach (MidiTempoChange change in changes)
        {
            long lastTick = ticks[^1];
            if (change.Tick == lastTick)
            {
                // Several changes at one tick: the last one wins.
                tempos[^1] = change.MicrosecondsPerQuarter;
                continue;
            }

            sums.Add(sums[^1] + (Int128)(change.Tick - lastTick) * tempos[^1]);
            ticks.Add(change.Tick);
            tempos.Add(change.MicrosecondsPerQuarter);
        }

        segmentTicks = [.. ticks];
        segmentTempos = [.. tempos];
        segmentSums = [.. sums];
    }

    /// <summary>True for SMPTE (frame-based) divisions, where Set Tempo is ignored.</summary>
    public bool IsSmpte => smpte;

    /// <summary>Ticks per quarter note (PPQN divisions only).</summary>
    public int TicksPerQuarter => smpte ? 0 : division;

    /// <summary>The time of a tick from the start of the song.</summary>
    /// <param name="tick">An absolute tick (0 or more).</param>
    /// <returns>Microseconds, rounded down.</returns>
    public long ToMicroseconds(long tick)
    {
        if (tick <= 0)
        {
            return 0;
        }

        if (smpte)
        {
            int framesPerSecond = 256 - (division >> 8);
            int ticksPerFrame = division & 0xFF;
            Int128 microseconds = framesPerSecond == 29
                ? (Int128)tick * 1_000_000 * 1001 / (30_000L * ticksPerFrame)
                : (Int128)tick * 1_000_000 / ((long)framesPerSecond * ticksPerFrame);
            return Clamp(microseconds);
        }

        int segment = Array.BinarySearch(segmentTicks, tick);
        if (segment < 0)
        {
            segment = ~segment - 1;
        }

        Int128 sum = segmentSums[segment] + (Int128)(tick - segmentTicks[segment]) * segmentTempos[segment];
        return Clamp(sum / division);
    }

    private static long Clamp(Int128 microseconds) => microseconds > long.MaxValue / 4 ? long.MaxValue / 4 : (long)microseconds;
}
