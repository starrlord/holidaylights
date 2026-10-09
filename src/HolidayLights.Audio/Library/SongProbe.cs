using System.Collections.Concurrent;
using HolidayLights.Audio.Midi;
using HolidayLights.Audio.Playback;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace HolidayLights.Audio.Library;

/// <summary>What a song file holds.</summary>
/// <param name="Length">The length (MIDI: to the End-of-Track), or null when unknown.</param>
/// <param name="IsPlayable">False when Windows cannot decode the file ("Holiday Lights can't play this kind of file.").</param>
/// <param name="Artist">The artist tag of an audio file (the Arranger column of your songs), or null.</param>
internal sealed record SongDetails(TimeSpan? Length, bool IsPlayable, string? Artist);

/// <summary>
/// Reads lengths, playability and artist tags of song files (on a background thread: decoding is I/O) and caches them
/// by path, size and last write time. Thread-safe.
/// </summary>
internal sealed class SongProbe
{
    private readonly ConcurrentDictionary<string, (FileStamp Stamp, SongDetails Details)> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the cached details while the file is unchanged.</summary>
    /// <param name="file">The song file.</param>
    /// <param name="details">The details.</param>
    /// <returns>True when the file was probed in its current state.</returns>
    public bool TryGetCached(SongFile file, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SongDetails? details)
    {
        if (cache.TryGetValue(file.AudioPath, out var entry) && entry.Stamp == file.Stamp)
        {
            details = entry.Details;
            return true;
        }

        details = null;
        return false;
    }

    /// <summary>Opens the file the way the player would and records what it found.</summary>
    /// <param name="file">The song file.</param>
    /// <returns>The details.</returns>
    public SongDetails Probe(SongFile file)
    {
        SongDetails details = file.Kind == SongKind.Midi ? ProbeMidi(file.AudioPath) : ProbeAudio(file.AudioPath, file.Kind);
        cache[file.AudioPath] = (file.Stamp, details);
        return details;
    }

    private static SongDetails ProbeMidi(string path)
    {
        try
        {
            MidiFile song = MidiFile.Load(path);
            return new SongDetails(TimeSpan.FromMicroseconds(song.DurationMicroseconds), true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new SongDetails(null, false, null);
        }
    }

    private static SongDetails ProbeAudio(string path, SongKind kind)
    {
        TimeSpan length;
        try
        {
            using AudioFileSource source = AudioFileSource.Open(path, kind);
            length = source.Reader.TotalTime;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new SongDetails(null, false, null);
        }

        return new SongDetails(length, true, ReadArtist(path));
    }

    /// <summary>The artist (or album artist) tag through the Windows property system; null when there is none.</summary>
    private static string? ReadArtist(string path)
    {
        try
        {
            StorageFile file = StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
            MusicProperties music = file.Properties.GetMusicPropertiesAsync().AsTask().GetAwaiter().GetResult();
            string artist = string.IsNullOrWhiteSpace(music.Artist) ? music.AlbumArtist : music.Artist;
            return string.IsNullOrWhiteSpace(artist) ? null : artist.Trim();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
