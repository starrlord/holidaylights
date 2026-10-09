using System.Buffers.Binary;
using System.Diagnostics;
using HolidayLights.Audio.Playback;
using NAudio.Codecs;
using NAudio.Wave;

namespace HolidayLights.Tests.Audio;

/// <summary>The audio-file sample chain: beat detector, volume and fades, the Sun audio reader and the heard-time mapping.</summary>
public sealed class AudioChainTests
{
    /// <summary>The beat times the audio prototype verified on this signal.</summary>
    private static readonly double[] PocBeatWindowEnds =
        [1.02, 1.51, 2.02, 2.51, 3.02, 3.51, 4.02, 4.50, 5.02, 5.50, 6.01, 6.52, 7.01, 7.52, 8.01, 8.52, 9.01, 9.52];

    [Fact]
    public void Bursts_at_120_bpm_give_one_beat_each_as_verified_by_the_poc()
    {
        var beats = new List<long>();
        var detector = new BeatDetectingSampleProvider(new SyntheticBeats(), beats.Add);
        var buffer = new float[4096];
        while (detector.Read(buffer) > 0)
        {
        }

        Assert.Equal(PocBeatWindowEnds.Length, beats.Count);
        for (int i = 0; i < beats.Count; i++)
        {
            double windowEnd = (beats[i] + BeatDetectingSampleProvider.WindowFrames) / 44100.0;
            Assert.InRange(windowEnd, PocBeatWindowEnds[i] - 0.0051, PocBeatWindowEnds[i] + 0.0051);
            double burst = 1.0 + i * 0.5;
            Assert.InRange(beats[i] / 44100.0, burst - 0.03, burst + 0.03);
        }
    }

    [Fact]
    public void Silence_has_no_beats()
    {
        var beats = new List<long>();
        var detector = new BeatDetectingSampleProvider(new ConstantSamples(0f, 44100 * 3, channels: 2), beats.Add);
        var buffer = new float[4096];
        while (detector.Read(buffer) > 0)
        {
        }

        Assert.Empty(beats);
    }

    [Fact]
    public void Volume_follows_the_synthesizer_curve()
    {
        var gain = new GainSampleProvider(new ConstantSamples(1f, 100, channels: 2));
        gain.SetVolume(0.5);
        var buffer = new float[200];

        Assert.Equal(200, gain.Read(buffer));

        Assert.All(buffer, s => Assert.Equal(0.25f, s));
    }

    [Fact]
    public void A_fade_ramps_the_gain_linearly_frame_by_frame()
    {
        var gain = new GainSampleProvider(new ConstantSamples(1f, 300, channels: 1, sampleRate: 1000));
        gain.SetVolume(1);
        gain.FadeTo(1, TimeSpan.FromMilliseconds(100), from: 0);
        var buffer = new float[300];

        gain.Read(buffer);

        for (int frame = 0; frame < 100; frame++)
        {
            double fade = (frame + 1) / 100.0;
            Assert.Equal(fade * fade, buffer[frame], 5);
        }

        Assert.All(buffer[100..], s => Assert.Equal(1f, s));
    }

    [Fact]
    public void A_fade_out_starts_from_the_current_gain()
    {
        var gain = new GainSampleProvider(new ConstantSamples(1f, 40, channels: 1, sampleRate: 1000));
        gain.SetVolume(1);
        var buffer = new float[20];
        gain.Read(buffer);

        gain.FadeTo(0, TimeSpan.FromMilliseconds(10));
        gain.Read(buffer);

        Assert.Equal(0.81, buffer[0], 5);
        Assert.All(buffer[9..], s => Assert.Equal(0f, s));
    }

    [Theory]
    [InlineData(2, new byte[] { 0x80, 0x00, 0x7F }, new[] { -1f, 0f, 127f / 128 })]
    [InlineData(3, new byte[] { 0x80, 0x00, 0x00, 0x00, 0x7F, 0xFF }, new[] { -1f, 0f, 32767f / 32768 })]
    [InlineData(4, new byte[] { 0x80, 0x00, 0x00, 0x40, 0x00, 0x00 }, new[] { -1f, 0.5f })]
    [InlineData(5, new byte[] { 0xC0, 0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00 }, new[] { -0.5f, 0.25f })]
    [InlineData(6, new byte[] { 0x3F, 0x00, 0x00, 0x00, 0xBE, 0x80, 0x00, 0x00 }, new[] { 0.5f, -0.25f })]
    [InlineData(7, new byte[] { 0x3F, 0xC0, 0, 0, 0, 0, 0, 0 }, new[] { 0.125f })]
    public void Sun_audio_linear_and_float_encodings_decode_exactly(int encoding, byte[] data, float[] expected)
    {
        using var stream = new MemoryStream(SunAu(encoding, 8000, 1, data));
        var reader = new SunAuFileReader(stream);

        float[] samples = ReadAll(reader);

        Assert.Equal(expected, samples);
        Assert.Equal(WaveFormatEncoding.IeeeFloat, reader.WaveFormat.Encoding);
    }

    [Fact]
    public void Sun_audio_g711_decodes_like_the_windows_codecs()
    {
        short[] values = [0, 1000, -1000, 32000, -32000];
        byte[] muLaw = values.Select(MuLawEncoder.LinearToMuLawSample).ToArray();
        byte[] aLaw = values.Select(ALawEncoder.LinearToALawSample).ToArray();

        float[] fromMuLaw = ReadAll(new SunAuFileReader(new MemoryStream(SunAu(1, 8000, 1, muLaw))));
        float[] fromALaw = ReadAll(new SunAuFileReader(new MemoryStream(SunAu(27, 8000, 1, aLaw))));

        Assert.Equal(muLaw.Select(b => MuLawDecoder.MuLawToLinearSample(b) / 32768f), fromMuLaw);
        Assert.Equal(aLaw.Select(b => ALawDecoder.ALawToLinearSample(b) / 32768f), fromALaw);
    }

    [Fact]
    public void Sun_audio_seeks_whole_frames_and_reads_to_the_end_when_the_size_is_unknown()
    {
        byte[] stereo = [0x10, 0x00, 0x20, 0x00, 0x30, 0x00, 0x40, 0x00, 0x50, 0x00, 0x60, 0x00];
        byte[] file = SunAu(3, 1000, 2, stereo);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8), 0xFFFFFFFF);
        var reader = new SunAuFileReader(new MemoryStream(file));

        Assert.Equal(TimeSpan.FromMilliseconds(3), reader.TotalTime);
        reader.Position = 10;
        Assert.Equal(8, reader.Position);
        Assert.Equal([0x3000 / 32768f, 0x4000 / 32768f, 0x5000 / 32768f, 0x6000 / 32768f], ReadAll(reader));
    }

    [Theory]
    [InlineData(new byte[] { 0x2E, 0x73, 0x6E, 0x64, 0, 0, 0, 24, 0, 0, 0, 0, 0, 0, 0, 23, 0, 0, 0x1F, 0x40, 0, 0, 0, 1 })]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 24, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0x1F, 0x40, 0, 0, 0, 1 })]
    [InlineData(new byte[] { 0x2E, 0x73, 0x6E, 0x64, 0, 0, 0, 24, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0x1F, 0x40, 0, 0, 0, 0 })]
    public void Unsupported_or_damaged_sun_audio_is_refused(byte[] header) =>
        Assert.Throws<InvalidDataException>(() => new SunAuFileReader(new MemoryStream(header)));

    [Fact]
    public void Frames_map_to_the_time_they_are_heard()
    {
        var timeline = new AudioTimeline(48000, TimeSpan.FromMilliseconds(30));
        long anchor = 1_000_000;

        timeline.SetAnchor(4800, anchor);

        long expected = anchor + Stopwatch.Frequency / 2 + Stopwatch.Frequency * 30 / 1000;
        Assert.Equal(expected, timeline.HeardAt(4800 + 24000));
    }

    [Fact]
    public void Audio_sources_open_wave_and_sun_audio_and_refuse_garbage()
    {
        string folder = Path.Combine(Path.GetTempPath(), "HolidayLightsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string wave = Path.Combine(folder, "a.wav");
            MusicLibraryTests.WriteWave(wave, TimeSpan.FromSeconds(0.5));
            string au = Path.Combine(folder, "b.au");
            MusicLibraryTests.WriteSunAu(au, 8000, 1);
            string garbage = Path.Combine(folder, "c.wma");
            File.WriteAllText(garbage, "not audio");

            using (AudioFileSource source = AudioFileSource.Open(wave, SongKind.Wav))
            {
                Assert.Equal(TimeSpan.FromSeconds(0.5), source.Reader.TotalTime);
                Assert.Equal(8000, source.Samples.WaveFormat.SampleRate);
            }

            using (AudioFileSource source = AudioFileSource.Open(au, SongKind.Au))
            {
                Assert.Equal(TimeSpan.FromSeconds(1), source.Reader.TotalTime);
                File.Move(au, au + ".moved");
            }

            Assert.ThrowsAny<Exception>(() => AudioFileSource.Open(garbage, SongKind.Wma));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static byte[] SunAu(int encoding, int rate, int channels, byte[] data)
    {
        var file = new byte[24 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(file, 0x2E736E64);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), 24);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8), (uint)data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(12), (uint)encoding);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(16), (uint)rate);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(20), (uint)channels);
        data.CopyTo(file, 24);
        return file;
    }

    private static float[] ReadAll(WaveStream reader)
    {
        ISampleProvider samples = reader.ToSampleProvider();
        var all = new List<float>();
        var buffer = new float[3];
        int count;
        while ((count = samples.Read(buffer)) > 0)
        {
            all.AddRange(buffer.AsSpan(0, count));
        }

        return [.. all];
    }

    /// <summary>The audio prototype's test signal: 10 s of stereo noise at 0.05 with 60 ms bursts at 0.8 every 0.5 s (Random(1)).</summary>
    private sealed class SyntheticBeats : ISampleProvider
    {
        private const int Rate = 44100;
        private readonly Random random = new(1);
        private long frame;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

        public int Read(Span<float> buffer)
        {
            int n = 0;
            for (; n + 1 < buffer.Length && frame < Rate * 10; n += 2, frame++)
            {
                bool burst = frame % (Rate / 2) < Rate * 60 / 1000;
                float value = (float)((random.NextDouble() * 2 - 1) * (burst ? 0.8 : 0.05));
                buffer[n] = value;
                buffer[n + 1] = value;
            }

            return n;
        }
    }

    /// <summary>A constant signal of a given length in frames.</summary>
    private sealed class ConstantSamples(float value, int frames, int channels, int sampleRate = 44100) : ISampleProvider
    {
        private int left = frames;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        public int Read(Span<float> buffer)
        {
            int count = Math.Min(buffer.Length / channels, left) * channels;
            buffer[..count].Fill(value);
            left -= count / channels;
            return count;
        }
    }
}
