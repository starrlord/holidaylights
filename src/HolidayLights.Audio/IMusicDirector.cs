namespace HolidayLights.Audio;

/// <summary>
/// The Music Box engine (PRODUCT-SPEC 6.1): play modes, the shuffle bag, the 1 s and
/// 60-180 s gaps, pause rules, MIDI and audio-file engines, volume, music events. Owner: audio.
/// </summary>
/// <remarks>
/// Commands may be called from any thread (the director serializes them on its music thread). <see cref="StateChanged"/>
/// is raised on an arbitrary thread; marshal to the UI thread.
/// </remarks>
public interface IMusicDirector : IDisposable
{
    /// <summary>The current state.</summary>
    MusicState State { get; }

    /// <summary>Note-on and beat events of what plays (timestamps = when the sound is heard).</summary>
    IMusicEventSource Events { get; }

    /// <summary>Raised when <see cref="State"/> changed (position updates at most once a second).</summary>
    event EventHandler? StateChanged;

    /// <summary>Applies the policy; re-evaluates as 5.4 did (a still-allowed song keeps playing; an "Intermittently" gap is honoured).</summary>
    /// <param name="policy">The policy.</param>
    void ApplyPolicy(MusicPolicy policy);

    /// <summary>"Play Now": plays a song at once, ignoring the mode and the check boxes, without touching the bag.</summary>
    /// <param name="songId">The song.</param>
    void PlayNow(string songId);

    /// <summary>"Next Song": another random checked song now (during a gap: at once).</summary>
    void NextSong();

    /// <summary>"Previous": restarts the current song; within 3 s of a restart, plays the song before it.</summary>
    void Previous();

    /// <summary>"Pause": pauses mid-song (until resumed or the program restarts).</summary>
    void Pause();

    /// <summary>"Resume": continues from the same point (MIDI: program and controller state re-sent).</summary>
    void Resume();

    /// <summary>Seeks the current audio file (no effect for MIDI).</summary>
    /// <param name="position">The position.</param>
    void Seek(TimeSpan position);

    /// <summary>"Try Now" / "Try Again": retries the synthesizer or the stopped music at once.</summary>
    void RetryNow();

    /// <summary>The MIDI output device names ("MIDI Output" combo).</summary>
    /// <returns>Device names in device order.</returns>
    IReadOnlyList<string> GetMidiOutputDevices();

    /// <summary>Fades the music out and stops (Exit: 500 ms); afterwards nothing plays again (exit, end of the screen saver).</summary>
    /// <param name="fadeOut">The fade duration.</param>
    /// <returns>A task that completes when the music has stopped and <see cref="State"/> shows it.</returns>
    Task StopAsync(TimeSpan fadeOut);
}
