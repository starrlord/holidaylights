using System.Globalization;
using HolidayLights.Audio;

namespace HolidayLights.App.Settings;

/// <summary>Why the music rests while a pause rule holds it (<see cref="MusicStatus.PausedByRules"/>).</summary>
public enum MusicPauseReason
{
    /// <summary>Not known for sure (several rules could apply).</summary>
    Unknown,

    /// <summary>A full-screen app or game is in front.</summary>
    FullScreen,

    /// <summary>A presentation is running.</summary>
    Presentation,

    /// <summary>A Windows Focus session is on.</summary>
    FocusSession,
}

/// <summary>The music status lines of Home's "Music" card and the Music Box (PRODUCT-SPEC 3.1, 3.4.2, 3.4.6).</summary>
public static class MusicStatusText
{
    /// <summary>The status sentence.</summary>
    /// <param name="state">The music state.</param>
    /// <param name="now">The time ("Next song in 1:24").</param>
    /// <param name="forHome">True for Home's shorter wording.</param>
    /// <param name="pause">Why a pause rule holds the music (<see cref="PauseReason"/>).</param>
    /// <param name="settingsOnlyWithMusicOn">
    /// True in a settings-only session (<c>/c</c>, Holiday Lights not running) while "Play Holiday Music" is on: no song
    /// starts by itself there, which is not "Music is off." (review r1 #62).
    /// </param>
    /// <returns>The sentence.</returns>
    public static string Text(MusicState state, DateTimeOffset now, bool forHome, MusicPauseReason pause = MusicPauseReason.Unknown, bool settingsOnlyWithMusicOn = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Status switch
        {
            MusicStatus.Off => settingsOnlyWithMusicOn ? "Music plays while Holiday Lights is running." : "Music is off.",
            MusicStatus.NeverMode => "Music is set to Never play.",
            MusicStatus.WaitingForScreenSaver => forHome
                ? "Music plays only while the screen saver is on."
                : "Music plays only while the Holiday Lights screen saver is showing.",
            MusicStatus.NoSongs => "No songs are checked, so no music will play.",
            MusicStatus.Playing => state.CurrentSong is { } song ? $"Playing: {song.Title}" : "Playing",
            MusicStatus.BetweenSongs => state.NextSongAt is { } at ? "Next song in " + Duration(at - now) : "The next song starts soon.",
            MusicStatus.Paused => "Paused",
            MusicStatus.PausedByRules => pause switch
            {
                MusicPauseReason.FullScreen => "Paused while a full-screen app is open",
                MusicPauseReason.Presentation => "Paused during your presentation",
                MusicPauseReason.FocusSession => "Paused during your Focus session",
                _ => "Paused while a full-screen app, a presentation or a Focus session is on",
            },
            MusicStatus.Held => "Music starts when the welcome card closes.",
            MusicStatus.WaitingForSynthesizer => "Music is waiting for the synthesizer.",
            MusicStatus.NoOutputDevice => "No speakers or headphones are available. Music will start when one is connected.",
            MusicStatus.StoppedAfterFailures => "Music stopped because several songs couldn't be played.",
            MusicStatus.Stopped => "Music is stopped during Remote Desktop sessions.",
            MusicStatus.WaitingForScreenSaverToEnd => "The next song plays when the screen saver ends.",
            _ => "",
        };
    }

    /// <summary>
    /// Which pause rule holds the music, as far as the lights' status tells (PRODUCT-SPEC 3.4.2): the lights report a
    /// presentation or a full-screen app only while their own rules for them are on, and Focus sessions not at all, so a
    /// Focus session is named only when no other rule can be the reason. A locked PC is not considered (nobody sees the
    /// window then).
    /// </summary>
    /// <param name="rest">The "When the Lights Rest" settings.</param>
    /// <param name="lights">The lights' status.</param>
    /// <returns>The reason, or <see cref="MusicPauseReason.Unknown"/>.</returns>
    public static MusicPauseReason PauseReason(RestSettings rest, LightsStatus lights)
    {
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(lights);
        if (rest.MusicFullScreen && lights.Paused.HasFlag(PauseReasons.Presentation))
        {
            return MusicPauseReason.Presentation;
        }

        bool someDisplayResting = lights.Paused == PauseReasons.None && lights.Displays.Any(d => d.Resting);
        if (rest.MusicFullScreen && (lights.Paused.HasFlag(PauseReasons.ExclusiveFullScreen) || someDisplayResting))
        {
            return MusicPauseReason.FullScreen;
        }

        // What the lights cannot see: a full-screen app or a presentation whose lights rule is off.
        bool fullScreenPossible = rest.MusicFullScreen && !rest.FullScreen;
        bool presentationPossible = rest.MusicFullScreen && !rest.Presentation;
        return (rest.MusicFocus, fullScreenPossible, presentationPossible) switch
        {
            (true, false, false) => MusicPauseReason.FocusSession,
            (false, true, false) => MusicPauseReason.FullScreen,
            (false, false, true) => MusicPauseReason.Presentation,
            _ => MusicPauseReason.Unknown,
        };
    }

    /// <summary>A duration as "1:24" (or "0:05").</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>Minutes and seconds.</returns>
    public static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        int seconds = (int)Math.Ceiling(duration.TotalSeconds);
        return string.Create(CultureInfo.CurrentCulture, $"{seconds / 60}:{seconds % 60:00}");
    }
}
