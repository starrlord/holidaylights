namespace HolidayLights.Audio.Playback;

/// <summary>
/// Opens MIDI songs with the sequencer and everything else with NAudio. A <c>.lnk</c> shortcut plays with the engine of
/// its target (PRODUCT-SPEC 6.1.2 fixes 5.4, which sent every shortcut to DirectShow).
/// </summary>
internal sealed class SongPlayerFactory : ISongPlayerFactory
{
    private readonly IAppLog log;

    /// <summary>Creates the factory.</summary>
    /// <param name="log">The log.</param>
    public SongPlayerFactory(IAppLog log) => this.log = log;

    /// <inheritdoc />
    public ISongPlayer Open(SongInfo song, SongPlaybackContext context)
    {
        string path = song.ShortcutTarget ?? song.FilePath;
        return song.Kind == SongKind.Midi
            ? MidiSongPlayer.Open(path, context, log)
            : AudioFileSongPlayer.Open(path, song.Kind, context.Events);
    }
}
