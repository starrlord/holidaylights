using HolidayLights.Audio.Events;

namespace HolidayLights.Audio.Playback;

/// <summary>Why a song player stopped by itself.</summary>
internal sealed class SongEndedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="error">The failure, or null when the song reached its end.</param>
    public SongEndedEventArgs(Exception? error) => Error = error;

    /// <summary>The failure, or null when the song reached its end.</summary>
    public Exception? Error { get; }
}

/// <summary>
/// One song being played by an engine (the MIDI sequencer or NAudio). Created open and silent by
/// <see cref="ISongPlayerFactory"/>; controlled from the director's music thread only.
/// </summary>
internal interface ISongPlayer : IDisposable
{
    /// <summary>Raised on an engine thread when the song ended or failed by itself (not after <see cref="Stop"/>).</summary>
    event EventHandler<SongEndedEventArgs>? Ended;

    /// <summary>Elapsed time of the song.</summary>
    TimeSpan Position { get; }

    /// <summary>True when <see cref="Seek"/> works (audio files; MIDI progress is read-only).</summary>
    bool CanSeek { get; }

    /// <summary>How much audio is already queued in the output, out of reach of volume changes and fades (MIDI: none).</summary>
    TimeSpan OutputLatency { get; }

    /// <summary>Starts playing from the beginning.</summary>
    /// <param name="volume">The Music Box volume 0-1 (0 when muted).</param>
    /// <param name="fadeIn">Fade-in time (the first song after program start); zero starts at full level.</param>
    void Start(double volume, TimeSpan fadeIn);

    /// <summary>Pauses mid-song.</summary>
    void Pause();

    /// <summary>Continues where <see cref="Pause"/> stopped.</summary>
    void Resume();

    /// <summary>Moves to a position (only when <see cref="CanSeek"/>).</summary>
    /// <param name="position">The position.</param>
    void Seek(TimeSpan position);

    /// <summary>Changes the Music Box volume at once.</summary>
    /// <param name="volume">The volume 0-1 (0 when muted).</param>
    void SetVolume(double volume);

    /// <summary>Fades out over <paramref name="duration"/> (exit); the song keeps running silently until stopped.</summary>
    /// <param name="duration">The fade time.</param>
    void FadeOut(TimeSpan duration);

    /// <summary>Re-reads the output position so event timestamps keep matching what is heard (called about once a second).</summary>
    void Synchronize();

    /// <summary>Stops at once; <see cref="Ended"/> is not raised.</summary>
    void Stop();
}

/// <summary>What an engine needs besides the song.</summary>
/// <param name="MidiDevice">"MIDI Output" (<c>music.midiDevice</c>); empty = automatic.</param>
/// <param name="MidiLatency">The way from the MIDI device to the listener (<c>music.syncOffsetMs</c>), added with the device's own latency to MIDI event timestamps so they mark when the sound is heard.</param>
/// <param name="Events">Where note and beat events go.</param>
internal sealed record SongPlaybackContext(string MidiDevice, TimeSpan MidiLatency, MusicEventHub Events);

/// <summary>Opens songs with the engine their type needs (a shortcut plays with the engine of its target).</summary>
internal interface ISongPlayerFactory
{
    /// <summary>Opens a song, ready to <see cref="ISongPlayer.Start"/>.</summary>
    /// <param name="song">The song.</param>
    /// <param name="context">Device, latency and event sink.</param>
    /// <returns>The player.</returns>
    /// <exception cref="Midi.MidiOutputException">The MIDI device cannot be opened (<see cref="Midi.MidiOutputException.IsDeviceBusy"/> when another app holds it).</exception>
    /// <remarks>Any other exception means the file cannot be read or decoded (a song failure).</remarks>
    ISongPlayer Open(SongInfo song, SongPlaybackContext context);
}
