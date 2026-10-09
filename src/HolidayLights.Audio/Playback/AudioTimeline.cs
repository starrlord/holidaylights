using System.Diagnostics;

namespace HolidayLights.Audio.Playback;

/// <summary>
/// Maps frames of an audio file (counted as the sample chain reads them, ahead of the output) to the
/// <see cref="Stopwatch"/> time they are heard: an anchor pairs the output's play position with the moment it was read,
/// and frames advance at the sample rate from there; a fixed device latency is added (PRODUCT-SPEC 5.10: "audio beats +
/// the output latency"). The anchor is replaced from the director's thread and read on the audio thread.
/// </summary>
internal sealed class AudioTimeline
{
    private readonly long sampleRate;
    private readonly long deviceLatencyTicks;
    private Anchor anchor;

    /// <summary>Creates a timeline anchored at frame 0 now.</summary>
    /// <param name="sampleRate">Frames per second.</param>
    /// <param name="deviceLatency">Latency between the output and the speakers.</param>
    public AudioTimeline(int sampleRate, TimeSpan deviceLatency)
    {
        this.sampleRate = sampleRate;
        deviceLatencyTicks = (long)(deviceLatency.TotalSeconds * Stopwatch.Frequency);
        anchor = new Anchor(0, Stopwatch.GetTimestamp());
    }

    /// <summary>Records that the output is playing <paramref name="framesPlayed"/> at <paramref name="timestamp"/>.</summary>
    /// <param name="framesPlayed">Frames played by the output since playback started.</param>
    /// <param name="timestamp">When that position was read.</param>
    public void SetAnchor(long framesPlayed, long timestamp) => Volatile.Write(ref anchor, new Anchor(framesPlayed, timestamp));

    /// <summary>When a frame is heard.</summary>
    /// <param name="frame">A frame index counted from the first frame read.</param>
    /// <returns>A <see cref="Stopwatch"/> timestamp.</returns>
    public long HeardAt(long frame)
    {
        Anchor current = Volatile.Read(ref anchor);
        return current.Timestamp + (long)((Int128)(frame - current.Frames) * Stopwatch.Frequency / sampleRate) + deviceLatencyTicks;
    }

    private sealed record Anchor(long Frames, long Timestamp);
}
