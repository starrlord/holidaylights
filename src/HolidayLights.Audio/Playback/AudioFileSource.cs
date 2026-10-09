using NAudio.Wave;

namespace HolidayLights.Audio.Playback;

/// <summary>
/// An open audio file decoded to float samples, chosen like NAudio's <c>AudioFileReader</c> (WAV, AIFF and MP3 readers,
/// Media Foundation for WMA, M4A and MPEG with the file name as hint) plus <see cref="SunAuFileReader"/> for
/// <c>.au</c>/<c>.snd</c>. The file is opened with <see cref="FileShare.Delete"/>, so "Remove Song" can move a playing
/// file to the holding folder.
/// </summary>
internal sealed class AudioFileSource : IDisposable
{
    private readonly FileStream file;

    private AudioFileSource(FileStream file, WaveStream reader)
    {
        this.file = file;
        Reader = reader;
        Samples = reader.ToSampleProvider();
    }

    /// <summary>The reader: position, length and seeking.</summary>
    public WaveStream Reader { get; }

    /// <summary>The same audio as float samples.</summary>
    public ISampleProvider Samples { get; }

    /// <summary>Opens a file.</summary>
    /// <param name="path">The audio file.</param>
    /// <param name="kind">Its type (decides the reader).</param>
    /// <returns>The source.</returns>
    /// <remarks>Throws (I/O, format or Media Foundation exceptions) when the file cannot be read or Windows cannot decode it.</remarks>
    public static AudioFileSource Open(string path, SongKind kind)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        WaveStream? reader = null;
        try
        {
            reader = kind switch
            {
                SongKind.Wav => ToPcm(new WaveFileReader(file)),
                SongKind.Aiff => new AiffFileReader(file),
                SongKind.Mp3 => new Mp3FileReader(file),
                SongKind.Au => new SunAuFileReader(file),
                _ => new StreamMediaFoundationReader(file, null, null, Path.GetFileName(path)),
            };
            return new AudioFileSource(file, reader);
        }
        catch
        {
            reader?.Dispose();
            file.Dispose();
            throw;
        }
    }

    /// <summary>Closes the reader and the file.</summary>
    public void Dispose()
    {
        Reader.Dispose();
        file.Dispose();
    }

    /// <summary>Compressed WAV data (ADPCM, G.711 ...) goes through the Windows codecs to PCM, as in <c>AudioFileReader</c>.</summary>
    private static WaveStream ToPcm(WaveFileReader reader) =>
        reader.WaveFormat.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible
            ? reader
            : new BlockAlignReductionStream(WaveFormatConversionStream.CreatePcmStream(reader));
}
