using NAudio.Wave;

namespace HolidayLights.Audio.Playback;

/// <summary>
/// Frédéric Patin's "simple sound energy" beat detector, variance-adaptive (PRODUCT-SPEC
/// 5.10): the instant energy of each 1,024-frame window is compared with the average of the last 43 windows (about 1 s at
/// 44.1 kHz); a beat is <c>E &gt; C x average</c> with <c>C = -0.0025714 x variance + 1.5142857</c> (clamped 1.0-1.6), at
/// most one per 5 windows. It sits in the sample chain before the volume, so beats do not depend on the Music Box volume
/// (as MIDI note events do not).
/// </summary>
internal sealed class BeatDetectingSampleProvider : ISampleProvider
{
    /// <summary>Frames per energy window.</summary>
    public const int WindowFrames = 1024;

    private const int HistoryLength = 43;
    private const int RefractoryWindows = 5;
    private const double EnergyScale = 1000;
    private const double MinimumEnergy = 0.5;

    private readonly ISampleProvider source;
    private readonly Action<long> beat;
    private readonly double[] history = new double[HistoryLength];
    private int historyPosition;
    private int historyCount;
    private int windowFill;
    private int windowsSinceBeat = RefractoryWindows;
    private double windowEnergy;
    private long framesRead;

    /// <summary>Wraps a sample source.</summary>
    /// <param name="source">The decoded song.</param>
    /// <param name="beat">Called on the reading (audio) thread with the index of the first frame of each beat window, counted from the first frame read.</param>
    public BeatDetectingSampleProvider(ISampleProvider source, Action<long> beat)
    {
        this.source = source;
        this.beat = beat;
    }

    /// <inheritdoc />
    public WaveFormat WaveFormat => source.WaveFormat;

    /// <inheritdoc />
    public int Read(Span<float> buffer)
    {
        int count = source.Read(buffer);
        int channels = WaveFormat.Channels;
        for (int i = 0; i + channels <= count; i += channels)
        {
            double frameEnergy = 0;
            for (int c = 0; c < channels; c++)
            {
                frameEnergy += buffer[i + c] * buffer[i + c];
            }

            windowEnergy += frameEnergy / channels;
            framesRead++;
            if (++windowFill == WindowFrames)
            {
                EndWindow();
            }
        }

        return count;
    }

    private void EndWindow()
    {
        double energy = windowEnergy / WindowFrames * EnergyScale;
        double average = 0;
        for (int i = 0; i < historyCount; i++)
        {
            average += history[i];
        }

        average /= Math.Max(1, historyCount);
        double variance = 0;
        for (int i = 0; i < historyCount; i++)
        {
            variance += (history[i] - average) * (history[i] - average);
        }

        variance /= Math.Max(1, historyCount);
        double sensitivity = Math.Clamp(-0.0025714 * variance + 1.5142857, 1.0, 1.6);
        bool candidate = historyCount == HistoryLength && energy > sensitivity * average && energy > MinimumEnergy;
        bool isBeat = candidate && windowsSinceBeat >= RefractoryWindows;
        windowsSinceBeat = isBeat ? 0 : Math.Min(windowsSinceBeat + 1, RefractoryWindows);
        history[historyPosition] = energy;
        historyPosition = (historyPosition + 1) % HistoryLength;
        historyCount = Math.Min(historyCount + 1, HistoryLength);
        windowEnergy = 0;
        windowFill = 0;
        if (isBeat)
        {
            beat(framesRead - WindowFrames);
        }
    }
}
