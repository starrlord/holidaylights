using HolidayLights.Audio;

namespace HolidayLights.App.Shell;

/// <summary>Builds the <see cref="MusicPolicy"/> of the running app (CONTRACTS 6.3; PRODUCT-SPEC 6.1.1, 5.12.2). Pure.</summary>
public static class MusicPolicyRules
{
    /// <summary>The policy for the current settings and conditions.</summary>
    /// <param name="settings">The settings (<c>music.*</c>, <c>current.music</c>).</param>
    /// <param name="pause">What rests (pause rules, Remote Desktop).</param>
    /// <param name="saverRunning">The Holiday Lights screen saver (or "Preview Screen Saver") is showing.</param>
    /// <param name="holdFirstSong">Hold the first song (the Welcome card is open).</param>
    /// <returns>The policy.</returns>
    public static MusicPolicy Build(AppSettings settings, PauseState pause, bool saverRunning, bool holdFirstSong)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(pause);
        return new MusicPolicy
        {
            Enabled = settings.Music.Enabled,
            Mode = settings.Current.Music.Mode,
            DisabledSongs = new HashSet<string>(settings.Current.Music.DisabledSongs, MediaIds.Comparer),
            Volume = settings.Music.Volume,
            Muted = settings.Music.Muted,
            MidiDevice = settings.Music.MidiDevice,
            SyncOffsetMs = settings.Music.SyncOffsetMs,
            SaverRunning = saverRunning,
            PausedByRules = pause.MusicPausedByRules,
            Stopped = pause.MusicStopped,
            HoldFirstSong = holdFirstSong,
        };
    }

    /// <summary>
    /// The policy of a settings-only session (PRODUCT-SPEC 2.5.4: no music of its own): no song starts by itself, but a
    /// song the user plays from the Music Box follows the settings (Volume, Mute, MIDI Output, the checked songs).
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The policy.</returns>
    public static MusicPolicy ForSettingsOnly(AppSettings settings) =>
        Build(settings, PauseState.None, saverRunning: false, holdFirstSong: false) with { Enabled = false };

    /// <summary>True when two policies ask for the same thing (the song sets compared as sets).</summary>
    /// <param name="a">A policy.</param>
    /// <param name="b">Another policy.</param>
    /// <returns>True when equivalent.</returns>
    public static bool AreEquivalent(MusicPolicy a, MusicPolicy b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Enabled == b.Enabled && a.Mode == b.Mode && a.Volume == b.Volume && a.Muted == b.Muted
            && string.Equals(a.MidiDevice, b.MidiDevice, StringComparison.Ordinal) && a.SyncOffsetMs == b.SyncOffsetMs
            && a.SaverRunning == b.SaverRunning && a.PausedByRules == b.PausedByRules && a.Stopped == b.Stopped
            && a.HoldFirstSong == b.HoldFirstSong && a.DisabledSongs.SetEquals(b.DisabledSongs);
    }
}
