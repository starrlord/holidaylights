namespace HolidayLights.Audio;

/// <summary>What the music is doing (Music Box status line, Home card, tray tooltip; PRODUCT-SPEC 3.4.2, 3.4.6).</summary>
public enum MusicStatus
{
    /// <summary>"Music is off." (Play Holiday Music is off).</summary>
    Off,

    /// <summary>"Music is set to Never play."</summary>
    NeverMode,

    /// <summary>"Music plays only while the Holiday Lights screen saver is showing."</summary>
    WaitingForScreenSaver,

    /// <summary>"No songs are checked, so no music will play."</summary>
    NoSongs,

    /// <summary>A song is playing.</summary>
    Playing,

    /// <summary>"Next song in 1:24" (the 1 s or 60-180 s gap; see <see cref="MusicState.NextSongAt"/>).</summary>
    BetweenSongs,

    /// <summary>"Paused" (by the user).</summary>
    Paused,

    /// <summary>"Paused while a full-screen app is open" / "Paused during your Focus session" / locked (a pause rule).</summary>
    PausedByRules,

    /// <summary>Waiting for the welcome card to close (5.4 import).</summary>
    Held,

    /// <summary>Another app holds the MIDI synthesizer (<c>MMSYSERR_ALLOCATED</c>); retried every 30 s.</summary>
    WaitingForSynthesizer,

    /// <summary>"No speakers or headphones are available."</summary>
    NoOutputDevice,

    /// <summary>"Music stopped because several songs couldn't be played." (3 failures in a row).</summary>
    StoppedAfterFailures,

    /// <summary>Stopped for a Remote Desktop session.</summary>
    Stopped,

    /// <summary>"Only When the Screen Saver is Off" while the Holiday Lights screen saver shows: a song starts when it ends.</summary>
    WaitingForScreenSaverToEnd,
}

/// <summary>A snapshot of the music state.</summary>
public sealed record MusicState
{
    /// <summary>The initial state.</summary>
    public static MusicState Initial { get; } = new();

    /// <summary>The status.</summary>
    public MusicStatus Status { get; init; } = MusicStatus.Off;

    /// <summary>The song that plays or is paused, else null.</summary>
    public SongInfo? CurrentSong { get; init; }

    /// <summary>Elapsed time of <see cref="CurrentSong"/>.</summary>
    public TimeSpan Position { get; init; }

    /// <summary>When the next song starts during a gap, else null.</summary>
    public DateTimeOffset? NextSongAt { get; init; }

    /// <summary>True when the current song can be seeked (audio files; MIDI progress is read-only).</summary>
    public bool CanSeek { get; init; }

    /// <summary>Holiday Lights is muted or at 0 in the Windows volume mixer (WASAPI session of this process); null when unknown.</summary>
    public bool? MutedInMixer { get; init; }
}
