namespace HolidayLights.Audio;

/// <summary>
/// Everything that decides whether and what music plays (PRODUCT-SPEC 6.1.1, 5.12.2). Built by app-shell from settings
/// and pause signals; the screen saver process builds it when it plays music itself.
/// </summary>
public sealed record MusicPolicy
{
    /// <summary>"Play Holiday Music" (the global switch).</summary>
    public bool Enabled { get; init; }

    /// <summary>"Play the Chosen Songs".</summary>
    public PlayMode Mode { get; init; } = PlayMode.Always;

    /// <summary>Unchecked songs (settings <c>current.music.disabledSongs</c>).</summary>
    public IReadOnlySet<string> DisabledSongs { get; init; } = new HashSet<string>(MediaIds.Comparer);

    /// <summary>Volume 0-100.</summary>
    public int Volume { get; init; } = 60;

    /// <summary>The Music Box "Mute" button.</summary>
    public bool Muted { get; init; }

    /// <summary>"MIDI Output" device name; empty = automatic.</summary>
    public string MidiDevice { get; init; } = "";

    /// <summary>
    /// <c>music.syncOffsetMs</c>: the way from the MIDI device to the listener (audio engine, device buffers, speakers), in
    /// ms. MIDI music events are stamped with the send time + the device's own latency (the Microsoft GS Wavetable Synth:
    /// 190 ms, see <see cref="Midi.MidiOutputDevices"/>) + this, so with the default 40 ms a GS synth note is stamped 230 ms
    /// after it is sent (measured on the reference PC: its sound reaches the audio engine 216-220 ms after the send).
    /// </summary>
    public int SyncOffsetMs { get; init; } = 40;

    /// <summary>The Holiday Lights screen saver (or "Preview Screen Saver") is showing.</summary>
    public bool SaverRunning { get; init; }

    /// <summary>A pause rule applies (full screen, presentation, Focus session, lock, per the settings): pause mid-song, resume where paused.</summary>
    public bool PausedByRules { get; init; }

    /// <summary>A Remote Desktop session: music stops.</summary>
    public bool Stopped { get; init; }

    /// <summary>Hold the first song (5.4 import: until the Welcome card closes).</summary>
    public bool HoldFirstSong { get; init; }
}
