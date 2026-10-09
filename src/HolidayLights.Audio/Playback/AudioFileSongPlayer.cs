using System.Diagnostics;
using HolidayLights.Audio.Events;
using NAudio.Wave;

namespace HolidayLights.Audio.Playback;

/// <summary>
/// An audio file (<c>.mp3 .wav .wma .aif .aifc .aiff .au .snd .mpeg .m4a</c>) through NAudio 3.1 (PRODUCT-SPEC 6.1.3):
/// <see cref="AudioFileSource"/> (Media Foundation for WMA, M4A and MPEG) -> seek point -> beat detector -> volume and
/// fades -> <c>WaveOut</c> on the default device. Detected beats are published as <see cref="MusicEventKind.AudioBeat"/>
/// events with the time they are heard.
/// </summary>
internal sealed class AudioFileSongPlayer : ISongPlayer
{
    /// <summary>WaveOut buffers: 3 x 50 ms keeps volume changes and fades prompt without risking gaps.</summary>
    private const int BufferMilliseconds = 50;
    private const int BufferCount = 3;
    private const byte BeatVelocity = 127;

    /// <summary>Estimated delay from the WaveOut position to the speakers (Windows audio engine and device buffers).</summary>
    private static readonly TimeSpan DeviceLatency = TimeSpan.FromMilliseconds(30);

    private readonly AudioFileSource file;
    private readonly SeekableSampleSource source;
    private readonly GainSampleProvider gain;
    private readonly AudioTimeline timeline;
    private readonly WaveOut output;
    private readonly MusicEventHub events;
    private volatile bool stopped;

    private AudioFileSongPlayer(AudioFileSource file, MusicEventHub events)
    {
        this.file = file;
        this.events = events;
        source = new SeekableSampleSource(file.Reader, file.Samples);
        timeline = new AudioTimeline(file.Samples.WaveFormat.SampleRate, DeviceLatency);
        gain = new GainSampleProvider(new BeatDetectingSampleProvider(source, OnBeat));
        output = new WaveOut { DeviceNumber = -1, BufferMilliseconds = BufferMilliseconds, NumberOfBuffers = BufferCount };
        try
        {
            output.Init(gain);
        }
        catch
        {
            output.Dispose();
            throw;
        }

        output.PlaybackStopped += OnPlaybackStopped;
    }

    /// <inheritdoc />
    public event EventHandler<SongEndedEventArgs>? Ended;

    /// <inheritdoc />
    public TimeSpan Position
    {
        get
        {
            TimeSpan total = file.Reader.TotalTime;
            TimeSpan current = file.Reader.CurrentTime;
            return current > total ? total : current;
        }
    }

    /// <inheritdoc />
    public bool CanSeek => file.Reader.CanSeek;

    /// <inheritdoc />
    public TimeSpan OutputLatency => TimeSpan.FromMilliseconds(BufferMilliseconds * BufferCount);

    /// <summary>Opens a file with the reader its type needs and prepares the output.</summary>
    /// <param name="path">The audio file.</param>
    /// <param name="kind">Its type.</param>
    /// <param name="events">Where audio beat events go.</param>
    /// <returns>The player.</returns>
    public static AudioFileSongPlayer Open(string path, SongKind kind, MusicEventHub events)
    {
        AudioFileSource file = AudioFileSource.Open(path, kind);
        try
        {
            return new AudioFileSongPlayer(file, events);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Start(double volume, TimeSpan fadeIn)
    {
        gain.SetVolume(volume);
        if (fadeIn > TimeSpan.Zero)
        {
            gain.FadeTo(1, fadeIn, from: 0);
        }

        timeline.SetAnchor(0, Stopwatch.GetTimestamp());
        output.Play();
    }

    /// <inheritdoc />
    public void Pause() => output.Pause();

    /// <inheritdoc />
    public void Resume()
    {
        // WaveOut.Play continues a paused output from the same position.
        output.Play();
        Synchronize();
    }

    /// <inheritdoc />
    public void Seek(TimeSpan position) => source.RequestSeek(position);

    /// <inheritdoc />
    public void SetVolume(double volume) => gain.SetVolume(volume);

    /// <inheritdoc />
    public void FadeOut(TimeSpan duration) => gain.FadeTo(0, duration);

    /// <inheritdoc />
    public void Synchronize()
    {
        long bytes = output.GetPosition();
        timeline.SetAnchor(bytes / output.OutputWaveFormat.BlockAlign, Stopwatch.GetTimestamp());
    }

    /// <inheritdoc />
    public void Stop()
    {
        stopped = true;
        output.Stop();
    }

    /// <summary>Stops and releases the output and the file.</summary>
    public void Dispose()
    {
        stopped = true;
        output.PlaybackStopped -= OnPlaybackStopped;
        output.Dispose();
        file.Dispose();
    }

    private void OnBeat(long frame) =>
        events.Publish(new MusicEvent(MusicEventKind.AudioBeat, timeline.HeardAt(frame), 0, 0, BeatVelocity));

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!stopped)
        {
            Ended?.Invoke(this, new SongEndedEventArgs(e.Exception));
        }
    }
}
