using NAudio.Wave;

namespace HolidayLights.Audio.Playback;

/// <summary>
/// The decoded file at the head of the sample chain. A seek requested from the director's thread is carried out by the
/// audio thread before its next read, so the reader is never repositioned while it decodes.
/// </summary>
internal sealed class SeekableSampleSource : ISampleProvider
{
    private const long NoSeek = -1;

    private readonly WaveStream stream;
    private readonly ISampleProvider samples;
    private long pendingSeekTicks = NoSeek;

    /// <summary>Wraps a reader.</summary>
    /// <param name="stream">The reader (position and length).</param>
    /// <param name="samples">The same reader as float samples.</param>
    public SeekableSampleSource(WaveStream stream, ISampleProvider samples)
    {
        this.stream = stream;
        this.samples = samples;
    }

    /// <inheritdoc />
    public WaveFormat WaveFormat => samples.WaveFormat;

    /// <summary>Requests a new position (applied before the next read).</summary>
    /// <param name="position">The position; clamped to the song.</param>
    public void RequestSeek(TimeSpan position) => Interlocked.Exchange(ref pendingSeekTicks, Math.Max(0, position.Ticks));

    /// <inheritdoc />
    public int Read(Span<float> buffer)
    {
        long seek = Interlocked.Exchange(ref pendingSeekTicks, NoSeek);
        if (seek != NoSeek)
        {
            stream.CurrentTime = TimeSpan.FromTicks(Math.Min(seek, stream.TotalTime.Ticks));
        }

        return samples.Read(buffer);
    }
}
