using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.Codecs;
using NAudio.Wave;

namespace HolidayLights.Audio.Playback;

/// <summary>
/// Reads Sun/NeXT audio files (<c>.au</c>, <c>.snd</c>), which 5.4 played through DirectShow and Media Foundation does
/// not decode: G.711 µ-law and A-law, 8/16/24/32-bit linear PCM and 32/64-bit float, big-endian, any channel count.
/// The output is 32-bit IEEE float at the file's rate.
/// </summary>
internal sealed class SunAuFileReader : WaveStream
{
    private const uint Magic = 0x2E736E64;
    private const int HeaderBytes = 24;
    private const uint UnknownDataSize = 0xFFFFFFFF;
    private const int OutputBytesPerSample = sizeof(float);
    private const int MaxChannels = 32;
    private const int MaxSampleRate = 768_000;

    private readonly Stream input;
    private readonly SampleEncoding encoding;
    private readonly int encodedBytesPerSample;
    private readonly int channels;
    private readonly long dataStart;
    private readonly long dataLength;
    private readonly WaveFormat format;
    private byte[] scratch = [];
    private long encodedPosition;

    /// <summary>Reads the header of a Sun audio stream.</summary>
    /// <param name="input">A readable, seekable stream positioned at the header; the caller keeps ownership.</param>
    /// <exception cref="InvalidDataException">Not a Sun audio file, or an encoding Holiday Lights can't play.</exception>
    public SunAuFileReader(Stream input)
    {
        this.input = input;
        Span<byte> header = stackalloc byte[HeaderBytes];
        if (input.ReadAtLeast(header, HeaderBytes, throwOnEndOfStream: false) < HeaderBytes
            || BinaryPrimitives.ReadUInt32BigEndian(header) != Magic)
        {
            throw new InvalidDataException("The file is not a Sun audio file.");
        }

        uint offset = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        uint size = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
        encoding = (SampleEncoding)BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
        uint rate = BinaryPrimitives.ReadUInt32BigEndian(header[16..]);
        uint channelCount = BinaryPrimitives.ReadUInt32BigEndian(header[20..]);
        encodedBytesPerSample = BytesPerSample(encoding);
        if (offset < HeaderBytes || offset > input.Length || rate is 0 or > MaxSampleRate || channelCount is 0 or > MaxChannels)
        {
            throw new InvalidDataException("The Sun audio file header is damaged.");
        }

        channels = (int)channelCount;
        dataStart = offset;
        long available = input.Length - offset;
        long declared = size == UnknownDataSize ? available : Math.Min(size, available);
        long frameBytes = (long)encodedBytesPerSample * channels;
        dataLength = declared - declared % frameBytes;
        format = WaveFormat.CreateIeeeFloatWaveFormat((int)rate, channels);
        input.Position = dataStart;
    }

    /// <summary>The encodings of the Sun audio format that can be played.</summary>
    private enum SampleEncoding : uint
    {
        MuLaw8 = 1,
        Linear8 = 2,
        Linear16 = 3,
        Linear24 = 4,
        Linear32 = 5,
        Float32 = 6,
        Float64 = 7,
        ALaw8 = 27,
    }

    /// <inheritdoc />
    public override WaveFormat WaveFormat => format;

    /// <summary>Length of the decoded (float) data in bytes.</summary>
    public override long Length => dataLength / encodedBytesPerSample * OutputBytesPerSample;

    /// <summary>Position in the decoded (float) data in bytes, aligned to whole frames.</summary>
    public override long Position
    {
        get => encodedPosition / encodedBytesPerSample * OutputBytesPerSample;
        set
        {
            long sample = Math.Clamp(value / OutputBytesPerSample, 0, dataLength / encodedBytesPerSample);
            encodedPosition = (sample - sample % channels) * encodedBytesPerSample;
            input.Position = dataStart + encodedPosition;
        }
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        int frameSamples = channels;
        long samplesLeft = (dataLength - encodedPosition) / encodedBytesPerSample;
        int samples = (int)Math.Min(buffer.Length / OutputBytesPerSample, samplesLeft);
        samples -= samples % frameSamples;
        if (samples <= 0)
        {
            return 0;
        }

        int encodedBytes = samples * encodedBytesPerSample;
        if (scratch.Length < encodedBytes)
        {
            scratch = new byte[encodedBytes];
        }

        int read = input.ReadAtLeast(scratch.AsSpan(0, encodedBytes), encodedBytes, throwOnEndOfStream: false);
        samples = read / encodedBytesPerSample;
        samples -= samples % frameSamples;
        encodedPosition += (long)samples * encodedBytesPerSample;
        Decode(scratch.AsSpan(0, samples * encodedBytesPerSample), MemoryMarshal.Cast<byte, float>(buffer)[..samples]);
        return samples * OutputBytesPerSample;
    }

    private static int BytesPerSample(SampleEncoding encoding) => encoding switch
    {
        SampleEncoding.MuLaw8 or SampleEncoding.ALaw8 or SampleEncoding.Linear8 => 1,
        SampleEncoding.Linear16 => 2,
        SampleEncoding.Linear24 => 3,
        SampleEncoding.Linear32 or SampleEncoding.Float32 => 4,
        SampleEncoding.Float64 => 8,
        _ => throw new InvalidDataException($"Sun audio encoding {(uint)encoding} can't be played."),
    };

    private void Decode(ReadOnlySpan<byte> source, Span<float> destination)
    {
        int size = encodedBytesPerSample;
        for (int i = 0; i < destination.Length; i++)
        {
            ReadOnlySpan<byte> sample = source.Slice(i * size, size);
            destination[i] = encoding switch
            {
                SampleEncoding.MuLaw8 => MuLawDecoder.MuLawToLinearSample(sample[0]) / 32768f,
                SampleEncoding.ALaw8 => ALawDecoder.ALawToLinearSample(sample[0]) / 32768f,
                SampleEncoding.Linear8 => (sbyte)sample[0] / 128f,
                SampleEncoding.Linear16 => BinaryPrimitives.ReadInt16BigEndian(sample) / 32768f,
                SampleEncoding.Linear24 => ((sample[0] << 24) | (sample[1] << 16) | (sample[2] << 8)) / 2147483648f,
                SampleEncoding.Linear32 => BinaryPrimitives.ReadInt32BigEndian(sample) / 2147483648f,
                SampleEncoding.Float32 => BinaryPrimitives.ReadSingleBigEndian(sample),
                _ => (float)BinaryPrimitives.ReadDoubleBigEndian(sample),
            };
        }
    }
}
