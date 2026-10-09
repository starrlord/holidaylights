using HolidayLights.Audio.Events;
using HolidayLights.Audio.Midi;

namespace HolidayLights.Audio.Playback;

/// <summary>
/// A MIDI song on its own <see cref="MidiSequencer"/> (one device handle per song, so the synthesizer is free between
/// songs as with 5.4's MCI). Note-on and beat events are published with the time they are heard: the send time + the
/// device's own latency (the GS Wavetable Synth: 190 ms) + <c>music.syncOffsetMs</c>, 40 ms ahead of that time
/// (PRODUCT-SPEC 5.10).
/// </summary>
internal sealed class MidiSongPlayer : ISongPlayer
{
    private readonly MidiFile song;
    private readonly MidiSequencer sequencer;
    private readonly MusicEventHub events;
    private volatile bool stopped;
    private volatile bool paused;

    /// <summary>Wraps an open sequencer.</summary>
    /// <param name="song">The parsed song.</param>
    /// <param name="sequencer">The sequencer (owned by the player).</param>
    /// <param name="context">Latency and event sink.</param>
    internal MidiSongPlayer(MidiFile song, MidiSequencer sequencer, SongPlaybackContext context)
    {
        this.song = song;
        this.sequencer = sequencer;
        events = context.Events;
        sequencer.SyncOffset = context.MidiLatency;
        sequencer.EventSent += OnEventSent;
        sequencer.Finished += OnFinished;
        sequencer.Failed += OnFailed;
    }

    /// <inheritdoc />
    public event EventHandler<SongEndedEventArgs>? Ended;

    /// <inheritdoc />
    public TimeSpan Position => sequencer.Position;

    /// <inheritdoc />
    public bool CanSeek => false;

    /// <summary>MIDI messages take effect as they are sent; nothing is queued ahead.</summary>
    public TimeSpan OutputLatency => TimeSpan.Zero;

    /// <summary>Parses the file, then opens the MIDI device (a damaged file never takes the synthesizer).</summary>
    /// <param name="path">The <c>.mid</c> or <c>.rmi</c> file.</param>
    /// <param name="context">Device, latency and event sink.</param>
    /// <param name="log">The log.</param>
    /// <returns>The player.</returns>
    public static MidiSongPlayer Open(string path, SongPlaybackContext context, IAppLog log)
    {
        MidiFile song = MidiFile.Load(path);
        return new MidiSongPlayer(song, new MidiSequencer(context.MidiDevice, log), context);
    }

    /// <inheritdoc />
    public void Start(double volume, TimeSpan fadeIn)
    {
        sequencer.Volume = volume;
        if (fadeIn > TimeSpan.Zero)
        {
            sequencer.FadeTo(0, TimeSpan.Zero);
            sequencer.Play(song);
            sequencer.FadeTo(1, fadeIn);
        }
        else
        {
            sequencer.Play(song);
        }
    }

    /// <summary>Pauses; an event the sequencer is raising right now is not published after the pause.</summary>
    public void Pause()
    {
        paused = true;
        sequencer.Pause();
    }

    /// <inheritdoc />
    public void Resume()
    {
        paused = false;
        sequencer.Resume();
    }

    /// <summary>MIDI progress is read-only (PRODUCT-SPEC 3.4.2); nothing happens.</summary>
    /// <param name="position">Ignored.</param>
    public void Seek(TimeSpan position)
    {
    }

    /// <inheritdoc />
    public void SetVolume(double volume) => sequencer.Volume = volume;

    /// <inheritdoc />
    public void FadeOut(TimeSpan duration) => sequencer.FadeTo(0, duration);

    /// <summary>MIDI events are stamped from the sequencer's clock at send time; nothing drifts.</summary>
    public void Synchronize()
    {
    }

    /// <inheritdoc />
    public void Stop()
    {
        stopped = true;
        sequencer.Stop();
    }

    /// <summary>Stops and closes the MIDI device.</summary>
    public void Dispose()
    {
        stopped = true;
        sequencer.EventSent -= OnEventSent;
        sequencer.Finished -= OnFinished;
        sequencer.Failed -= OnFailed;
        sequencer.Dispose();
    }

    private void OnEventSent(object? sender, MusicEvent e)
    {
        if (!stopped && !paused)
        {
            events.Publish(e);
        }
    }

    private void OnFinished(object? sender, EventArgs e)
    {
        if (!stopped)
        {
            Ended?.Invoke(this, new SongEndedEventArgs(null));
        }
    }

    private void OnFailed(object? sender, ErrorEventArgs e)
    {
        if (!stopped)
        {
            Ended?.Invoke(this, new SongEndedEventArgs(e.GetException()));
        }
    }
}
