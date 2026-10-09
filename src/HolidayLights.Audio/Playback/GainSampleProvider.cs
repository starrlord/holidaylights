using NAudio.Wave;

namespace HolidayLights.Audio.Playback;

/// <summary>
/// Applies the Music Box volume and fades to an audio file in the sample chain (never the Windows mixer). The amplitude
/// is <c>(volume x fade)^2</c>, the same curve the MIDI synthesizer applies to CC7 (DLS: 40 log10(CC7/127) dB), so a
/// volume of 60 % sounds alike for MIDI songs and audio files.
/// </summary>
/// <remarks>Control members are called from the director's thread; <see cref="Read"/> runs on the audio thread.</remarks>
internal sealed class GainSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private double volume;
    private FadeRequest? pendingFade;
    private double fade = 1;
    private double fadeTarget = 1;
    private double fadeStep;
    private long fadeFramesLeft;

    /// <summary>Wraps a sample source.</summary>
    /// <param name="source">The source.</param>
    public GainSampleProvider(ISampleProvider source) => this.source = source;

    /// <inheritdoc />
    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>Sets the Music Box volume (applies from the next buffer).</summary>
    /// <param name="value">0-1 (0 when muted).</param>
    public void SetVolume(double value) => Volatile.Write(ref volume, Math.Clamp(value, 0, 1));

    /// <summary>Starts a fade from the next buffer on.</summary>
    /// <param name="target">The fade gain 0-1 to reach.</param>
    /// <param name="duration">How long the fade takes; zero is immediate.</param>
    /// <param name="from">The gain to start from, or null to start from the current gain.</param>
    public void FadeTo(double target, TimeSpan duration, double? from = null) =>
        Volatile.Write(ref pendingFade, new FadeRequest(Math.Clamp(target, 0, 1), duration, from));

    /// <inheritdoc />
    public int Read(Span<float> buffer)
    {
        int count = source.Read(buffer);
        if (Interlocked.Exchange(ref pendingFade, null) is { } request)
        {
            BeginFade(request);
        }

        double level = Volatile.Read(ref volume);
        int channels = WaveFormat.Channels;
        for (int i = 0; i < count; i += channels)
        {
            if (fadeFramesLeft > 0)
            {
                fade = --fadeFramesLeft == 0 ? fadeTarget : fade + fadeStep;
            }

            double gain = level * fade;
            float amplitude = (float)(gain * gain);
            int end = Math.Min(i + channels, count);
            for (int s = i; s < end; s++)
            {
                buffer[s] *= amplitude;
            }
        }

        return count;
    }

    private void BeginFade(FadeRequest request)
    {
        if (request.From is { } from)
        {
            fade = Math.Clamp(from, 0, 1);
        }

        long frames = (long)(request.Duration.TotalSeconds * WaveFormat.SampleRate);
        fadeTarget = request.Target;
        if (frames <= 0)
        {
            fade = request.Target;
            fadeFramesLeft = 0;
            return;
        }

        fadeFramesLeft = frames;
        fadeStep = (request.Target - fade) / frames;
    }

    private sealed record FadeRequest(double Target, TimeSpan Duration, double? From);
}
