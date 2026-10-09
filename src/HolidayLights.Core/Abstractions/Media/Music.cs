using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>"Play the Chosen Songs" (a theme value; PRODUCT-SPEC 6.1.1). Values are the 5.4 "Music Play" numbers 0-4.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlayMode>))]
public enum PlayMode
{
    /// <summary>"Never".</summary>
    [JsonStringEnumMemberName("never")]
    Never = 0,

    /// <summary>"Only When the Screen Saver is On" (the Holiday Lights screen saver, including "Preview Screen Saver").</summary>
    [JsonStringEnumMemberName("saverOn")]
    SaverOn = 1,

    /// <summary>"Only When the Screen Saver is Off".</summary>
    [JsonStringEnumMemberName("saverOff")]
    SaverOff = 2,

    /// <summary>"Always" (default; 1 s between songs).</summary>
    [JsonStringEnumMemberName("always")]
    Always = 3,

    /// <summary>"Intermittently" (60,000 + random(0..119,999) ms between songs).</summary>
    [JsonStringEnumMemberName("intermittently")]
    Intermittently = 4,
}

/// <summary>Kinds of <see cref="MusicEvent"/>.</summary>
public enum MusicEventKind : byte
{
    /// <summary>A MIDI note-on with velocity above 0 (channel, note, velocity set).</summary>
    NoteOn,

    /// <summary>A quarter note of the MIDI tempo map (velocity 127); Dance to the Music steps its animation bulbs.</summary>
    Beat,

    /// <summary>A song started (also after resume of a new song).</summary>
    SongStarted,

    /// <summary>The song ended or was stopped; no music until the next <see cref="SongStarted"/> or <see cref="Resumed"/>.</summary>
    SongStopped,

    /// <summary>Playback was paused.</summary>
    Paused,

    /// <summary>Playback resumed after a pause.</summary>
    Resumed,

    /// <summary>A beat detected in an audio file (velocity 127); Dance to the Music steps its animation bulbs and lights every group.</summary>
    AudioBeat,
}

/// <summary>
/// One music event for "Dance to the Music" (PRODUCT-SPEC 5.10). Published by the music engine (audio) at the moment it
/// sends the note, consumed by the Lights thread and, through the instance pipe, by the screen saver process.
/// </summary>
/// <param name="Kind">The kind.</param>
/// <param name="Timestamp">When the sound is <b>heard</b>: the send time plus the MIDI device's own latency (Microsoft GS Wavetable Synth: 190 ms) plus <c>music.syncOffsetMs</c> (MIDI; published 40 ms before that time), or the output latency (audio), as a <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> value.</param>
/// <param name="Channel">MIDI channel 0-15 (channel 10 of the spec is 9 here); 0 for other kinds.</param>
/// <param name="Note">MIDI note 0-127; 0 for other kinds.</param>
/// <param name="Velocity">Velocity 1-127 for notes and beats; 0 for other kinds.</param>
public readonly record struct MusicEvent(MusicEventKind Kind, long Timestamp, byte Channel, byte Note, byte Velocity);

/// <summary>A single-consumer queue of music events (lock-free; one reader per consumer).</summary>
public interface IMusicEventReader : IDisposable
{
    /// <summary>Copies waiting events into a buffer, oldest first; events that overflowed the queue are dropped.</summary>
    /// <param name="destination">The buffer.</param>
    /// <returns>Number of events copied.</returns>
    int Read(Span<MusicEvent> destination);
}

/// <summary>The stream of music events. Implemented by audio; each consumer subscribes its own reader.</summary>
public interface IMusicEventSource
{
    /// <summary>Creates a reader that receives every event published from now on. Dispose it to unsubscribe.</summary>
    /// <param name="capacity">Queue capacity in events (older events are dropped when it is full).</param>
    /// <returns>The reader.</returns>
    IMusicEventReader Subscribe(int capacity = 4096);
}

/// <summary>The "Type" column of the song list.</summary>
public enum SongKind
{
    /// <summary>"MIDI" (<c>.mid</c>, <c>.rmi</c>).</summary>
    Midi,

    /// <summary>"MP3".</summary>
    Mp3,

    /// <summary>"WAV".</summary>
    Wav,

    /// <summary>"WMA".</summary>
    Wma,

    /// <summary>"AIFF" (<c>.aif</c>, <c>.aifc</c>, <c>.aiff</c>).</summary>
    Aiff,

    /// <summary>"AU" (<c>.au</c>, <c>.snd</c>).</summary>
    Au,

    /// <summary>"MPEG".</summary>
    Mpeg,

    /// <summary>"M4A".</summary>
    M4a,
}

/// <summary>Song category chips (PRODUCT-SPEC 3.4.3, 6.1.4). A song can be in several.</summary>
public enum SongCategory
{
    /// <summary>"Christmas" (31: the songs of the 5.4 "Christmas 1" theme).</summary>
    Christmas,

    /// <summary>"Chanukah".</summary>
    Chanukah,

    /// <summary>"Halloween".</summary>
    Halloween,

    /// <summary>"New Year".</summary>
    NewYear,

    /// <summary>"Patriotic".</summary>
    Patriotic,

    /// <summary>"Folk &amp; Classics".</summary>
    FolkAndClassics,

    /// <summary>"My Songs": every song in My Music.</summary>
    MySongs,
}

/// <summary>One song of the Music Box.</summary>
public sealed record SongInfo
{
    /// <summary>Stable id (<see cref="MediaIds"/>), e.g. <c>bundled:Jingle Bells (Reggae).mid</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The title: the file name without extension (5.4).</summary>
    public required string Title { get; init; }

    /// <summary>Full path of the file (a <c>.lnk</c> for shortcuts).</summary>
    public required string FilePath { get; init; }

    /// <summary>Resolved target of a <c>.lnk</c> shortcut (its type decides the engine); null for ordinary files.</summary>
    public string? ShortcutTarget { get; init; }

    /// <summary>Bundled or My Music.</summary>
    public required MediaOrigin Origin { get; init; }

    /// <summary>The type (of the target for shortcuts).</summary>
    public required SongKind Kind { get; init; }

    /// <summary>Length when known (computed lazily; MIDI from the tempo map).</summary>
    public TimeSpan? Length { get; init; }

    /// <summary>"Arranged by" text: the 6.1.4 credit for bundled songs ("Dean Burris, 1999"), the artist tag for audio files, else null.</summary>
    public string? Arranger { get; init; }

    /// <summary>Category chips the song belongs to.</summary>
    public IReadOnlyList<SongCategory> Categories { get; init; } = [];

    /// <summary>False when Windows cannot decode the file ("Holiday Lights can't play this kind of file."); the shuffle skips it.</summary>
    public bool IsPlayable { get; init; } = true;

    /// <summary>The 5.4 sort key: the upper-cased file name (CRT <c>toupper</c>; sorts [ \ ] ^ _ ` after the letters).</summary>
    public required string SortKey { get; init; }
}

/// <summary>
/// The Music Box library: the bundled songs (<c>Content\Music</c>) and My Music (watched), with hidden bundled songs
/// (settings <c>songs.hidden</c>). Implemented by audio.
/// </summary>
/// <remarks>Mutating members update files and settings and must be called on the UI thread. <see cref="Changed"/> may be raised on any thread.</remarks>
public interface ISongLibrary
{
    /// <summary>Listed songs (not hidden), in 5.4 order (<see cref="SongInfo.SortKey"/>, ordinal).</summary>
    IReadOnlyList<SongInfo> Songs { get; }

    /// <summary>Bundled songs the user removed (hidden); "Restore Removed Songs" brings them back.</summary>
    IReadOnlyList<SongInfo> HiddenSongs { get; }

    /// <summary>Raised when the folders changed or songs were added, removed, hidden or restored.</summary>
    event EventHandler? Changed;

    /// <summary>Scans both folders and starts watching My Music.</summary>
    void Start();

    /// <summary>Finds a song, hidden ones included.</summary>
    /// <param name="id">A song id.</param>
    /// <param name="song">The song when found.</param>
    /// <returns>True when known.</returns>
    bool TryGetSong(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SongInfo? song);

    /// <summary>Copies music files (and <c>.lnk</c> shortcuts) into My Music; new songs start checked.</summary>
    /// <param name="paths">The files.</param>
    /// <returns>One result per file, in order.</returns>
    IReadOnlyList<MediaImportResult> AddFiles(IEnumerable<string> paths);

    /// <summary>"Remove Song": a user song (or shortcut) moves to the holding folder; a bundled song is hidden.</summary>
    /// <param name="id">The song id.</param>
    /// <returns>The held file for user songs (for undo); null for bundled songs.</returns>
    HeldItem? Remove(string id);

    /// <summary>Brings back a removed user song from the holding folder.</summary>
    /// <param name="item">The held file returned by <see cref="Remove"/>.</param>
    void Restore(HeldItem item);

    /// <summary>"Restore Removed Songs": lists every hidden bundled song again.</summary>
    void RestoreHiddenSongs();
}
