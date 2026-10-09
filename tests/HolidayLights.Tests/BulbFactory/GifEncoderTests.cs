using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Core.Bulbs.Writing;
using HolidayLights.Core.Imaging;
using HolidayLights.Tests.Shared;
using static HolidayLights.Tests.BulbFactory.WritingTestData;

namespace HolidayLights.Tests.BulbFactory;

public sealed class GifEncoderTests
{
    [Theory]
    [InlineData(1, 1, 1, 3)]
    [InlineData(40, 30, 3, 50)]
    [InlineData(500, 400, 2, 200)]
    [InlineData(64, 64, 4, 255)]
    public void FewColours_SurviveExactlyInBothDecoders(int width, int height, int frameCount, int colorCount)
    {
        Rgba32Image[] frames = RandomFrames(width, height, frameCount, colorCount, transparentShare: 0.2, seed: width + colorCount);

        byte[] gif = GifEncoder.Encode(frames);

        AssertFrames(frames, GifDecoder.DecodeClassic(gif));
        AssertFrames(frames, GifDecoder.DecodeStandard(gif));
    }

    [Fact]
    public void LongRunsAndNoise_FillTheCodeTableSeveralTimes()
    {
        var random = new Random(7);
        var pixels = new uint[700 * 300];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = i % 3000 < 1500 ? Red : 0xFF000000 | (uint)random.Next(0, 200) << 8;
        }

        Rgba32Image frame = new(700, 300, pixels);
        byte[] gif = GifEncoder.Encode([frame]);

        AssertFrames([frame], GifDecoder.DecodeClassic(gif));
        AssertFrames([frame], GifDecoder.DecodeStandard(gif));
    }

    [Fact]
    public void ManyColours_AreReducedTo255Plus_Transparency()
    {
        var pixels = new uint[128 * 128];
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                pixels[y * 128 + x] = x == y ? 0 : Bgra32.Pack((byte)(x * 2), (byte)(y * 2), (byte)((x + y) & 0xFF), 255);
            }
        }

        var frame = new Rgba32Image(128, 128, pixels);
        GifAnimation decoded = GifDecoder.DecodeClassic(GifEncoder.Encode([frame]));

        uint[] result = decoded.Frames[0].Pixels;
        Assert.True(result.Where(p => p >> 24 != 0).Distinct().Count() <= 255);
        Assert.All(Enumerable.Range(0, 128), i => Assert.Equal(0u, result[i * 128 + i]));
        double meanError = Enumerable.Range(0, pixels.Length).Where(i => pixels[i] != 0).Average(i => Distance(pixels[i], result[i]));
        Assert.True(meanError < 12, $"mean colour error {meanError}");
    }

    [Fact]
    public void OpaquePictures_HaveNoTransparency_AndAlphaIsOneBit()
    {
        Rgba32Image opaque = Solid(4, 4, Blue);
        Rgba32Image soft = Solid(4, 4, 0x80FF0000);
        soft.Pixels[0] = 0x7FFF0000;

        Assert.All(GifDecoder.DecodeClassic(GifEncoder.Encode([opaque])).Frames[0].Pixels, p => Assert.Equal(Blue, p));
        uint[] decoded = GifDecoder.DecodeClassic(GifEncoder.Encode([soft])).Frames[0].Pixels;
        Assert.Equal(0u, decoded[0]);
        Assert.All(decoded[1..], p => Assert.Equal(Red, p));
    }

    [Fact]
    public void Frames_ReplaceEachOtherSoTransparentPixelsStayTransparentEverywhere()
    {
        Rgba32Image first = Solid(3, 3, Red);
        Rgba32Image second = Solid(3, 3, 0);
        second.Pixels[4] = Green;

        byte[] gif = GifEncoder.Encode([first, second]);

        foreach (GifAnimation decoded in new[] { GifDecoder.DecodeClassic(gif), GifDecoder.DecodeStandard(gif) })
        {
            Assert.Equal(second.Pixels, decoded.Frames[1].Pixels);
        }

        Assert.Equal([300, 300], GifDecoder.DecodeStandard(gif).DelaysMilliseconds);
    }

    [Theory]
    [InlineData(37, 23)]
    [InlineData(301, 199)]
    public void WindowsImaging_ReadsTheSamePixels(int width, int height)
    {
        Rgba32Image[] frames = RandomFrames(width, height, 2, 120, transparentShare: 0.3, seed: 3);
        byte[] gif = GifEncoder.Encode(frames);

        StaThread.Run(() =>
        {
            var decoder = new GifBitmapDecoder(new MemoryStream(gif), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(2, decoder.Frames.Count);
            for (int f = 0; f < 2; f++)
            {
                var bitmap = new FormatConvertedBitmap(decoder.Frames[f], PixelFormats.Bgra32, null, 0);
                var pixels = new uint[width * height];
                bitmap.CopyPixels(pixels, width * 4, 0);
                Assert.Equal(frames[f].Pixels, pixels.Select(p => p >> 24 == 0 ? 0u : p));
            }
        });
    }

    [Fact]
    public void Encode_RefusesNoFramesAndFramesOfDifferentSizes()
    {
        Assert.Throws<ArgumentException>(() => GifEncoder.Encode([]));
        Assert.Throws<ArgumentException>(() => GifEncoder.Encode([Solid(2, 2, Red), Solid(2, 3, Red)]));
        Assert.Throws<ArgumentException>(() => GifEncoder.Encode([new Rgba32Image(0, 5)]));
    }

    private static Rgba32Image[] RandomFrames(int width, int height, int frameCount, int colorCount, double transparentShare, int seed)
    {
        var random = new Random(seed);
        uint[] colors = [.. Enumerable.Range(0, colorCount).Select(i => 0xFF000000 | (uint)(i * 0x9E3779B1u & 0xFFFFFF))];
        return [.. Enumerable.Range(0, frameCount).Select(_ =>
        {
            uint[] pixels = [.. Enumerable.Range(0, width * height).Select(_ => random.NextDouble() < transparentShare ? 0u : colors[random.Next(colors.Length)])];
            return new Rgba32Image(width, height, pixels);
        })];
    }

    private static void AssertFrames(IReadOnlyList<Rgba32Image> expected, GifAnimation decoded)
    {
        Assert.Equal(expected.Count, decoded.Frames.Count);
        for (int f = 0; f < expected.Count; f++)
        {
            Assert.Equal(expected[f].Pixels, decoded.Frames[f].Pixels);
        }
    }

    private static double Distance(uint a, uint b) =>
        Math.Sqrt(Square(Bgra32.R(a) - Bgra32.R(b)) + Square(Bgra32.G(a) - Bgra32.G(b)) + Square(Bgra32.B(a) - Bgra32.B(b)));

    private static double Square(double value) => value * value;
}
