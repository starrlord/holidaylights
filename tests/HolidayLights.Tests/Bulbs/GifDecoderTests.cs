using System.Collections.Concurrent;
using System.Text.Json;
using HolidayLights.Core.Bulbs;
using HolidayLights.Core.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

public sealed class GifDecoderTests
{
    private const uint Black = 0xFF000000;
    private const uint Red = 0xFFFF0000;
    private const uint Green = 0xFF00FF00;
    private const uint Blue = 0xFF0000FF;
    private const uint Yellow = 0xFFFFFF00;
    private const uint Cyan = 0xFF00FFFF;

    private static readonly (byte, byte, byte)[] Colors = [(0, 0, 0), (255, 0, 0), (0, 255, 0), (0, 0, 255)];
    private static readonly (byte, byte, byte)[] OtherColors = [(255, 255, 0), (0, 255, 255), (255, 0, 255), (255, 255, 255)];

    [Fact]
    public void TransparentPixels_PunchHolesInClassicButShowThePreviousFrameInStandard()
    {
        byte[] gif = TestGif.Encode(2, 1, Colors,
        [
            Frame(2, 1, [1, 2]) with { Disposal = 1 },
            Frame(2, 1, [0, 3]) with { Disposal = 1, TransparentIndex = 0 },
        ]);

        Assert.Equal([0u, Blue], GifDecoder.DecodeClassic(gif).Frames[1].Pixels);
        Assert.Equal([Red, Blue], GifDecoder.DecodeStandard(gif).Frames[1].Pixels);
    }

    [Fact]
    public void LocalColorTable_PersistsAndRecoloursEarlierFramesInClassicOnly()
    {
        byte[] gif = TestGif.Encode(1, 1, Colors,
        [
            Frame(1, 1, [1]) with { Control = false },
            Frame(1, 1, [1]) with { Control = false, LocalColors = OtherColors },
            Frame(1, 1, [1]) with { Control = false },
        ]);

        // Red has no exact match in the local table: GDI picks the nearest colour, lowest index on ties (yellow).
        Assert.Equal([Yellow, Cyan, Cyan], GifDecoder.DecodeClassic(gif).Frames.Select(f => f.Pixels[0]));
        Assert.Equal([Red, Cyan, Red], GifDecoder.DecodeStandard(gif).Frames.Select(f => f.Pixels[0]));
    }

    [Fact]
    public void Disposal2_FillsTheWholeSlotInClassicAndClearsTheRectangleInStandard()
    {
        byte[] gif = TestGif.Encode(2, 1, Colors,
        [
            Frame(2, 1, [1, 1]) with { Disposal = 2 },
            Frame(1, 1, [3]) with { Left = 1 },
        ], background: 2);

        // Without transparency 5.4 fills the slot with the opaque background colour.
        Assert.Equal([Green, Blue], GifDecoder.DecodeClassic(gif).Frames[1].Pixels);
        Assert.Equal([0u, Blue], GifDecoder.DecodeStandard(gif).Frames[1].Pixels);
    }

    [Fact]
    public void GraphicControl_PersistsInClassicButAppliesToOneImageInStandard()
    {
        byte[] gif = TestGif.Encode(1, 1, Colors,
        [
            Frame(1, 1, [1]) with { TransparentIndex = 0 },
            Frame(1, 1, [0]) with { Control = false },
        ]);

        Assert.Equal(0u, GifDecoder.DecodeClassic(gif).Frames[1].Pixels[0]);
        Assert.Equal(Black, GifDecoder.DecodeStandard(gif).Frames[1].Pixels[0]);
    }

    [Fact]
    public void Delays_AreIgnoredByClassicAndKeptByStandard()
    {
        byte[] gif = TestGif.Encode(1, 1, Colors, [Frame(1, 1, [1]) with { DelayCs = 7 }, Frame(1, 1, [2]) with { DelayCs = 25 }]);

        Assert.Equal([0, 0], GifDecoder.DecodeClassic(gif).DelaysMilliseconds);
        Assert.Equal([70, 250], GifDecoder.DecodeStandard(gif).DelaysMilliseconds);
    }

    [Fact]
    public void Disposal3_RestoresTheCanvasInStandard()
    {
        byte[] gif = TestGif.Encode(2, 1, Colors,
        [
            Frame(2, 1, [1, 1]) with { Disposal = 1 },
            Frame(1, 1, [3]) with { Disposal = 3 },
            Frame(1, 1, [2]) with { Left = 1 },
        ]);

        GifAnimation standard = GifDecoder.DecodeStandard(gif);

        Assert.Equal([Blue, Red], standard.Frames[1].Pixels);
        Assert.Equal([Red, Green], standard.Frames[2].Pixels);
    }

    [Fact]
    public void InterlacedRows_ComeOutInPictureOrder()
    {
        byte[] pixels = [.. Enumerable.Range(0, 10).Select(i => (byte)(i % 4))];
        byte[] gif = TestGif.Encode(1, 10, Colors, [Frame(1, 10, pixels) with { Interlaced = true }]);
        uint[] expected = [.. pixels.Select(i => Colors[i]).Select(c => 0xFF000000 | (uint)(c.Item1 << 16 | c.Item2 << 8 | c.Item3))];

        Assert.Equal(expected, GifDecoder.DecodeClassic(gif).Frames[0].Pixels);
        Assert.Equal(expected, GifDecoder.DecodeStandard(gif).Frames[0].Pixels);
    }

    [Fact]
    public void StrayByteAfterAnImage_EndsTheAnimation()
    {
        byte[] first = TestGif.Encode(1, 1, Colors, [Frame(1, 1, [1])], trailer: false);
        byte[] second = TestGif.Encode(1, 1, Colors, [Frame(1, 1, [2])]);
        byte[] gif = [.. first, 0x00, .. second.AsSpan(13 + 3 * Colors.Length)];

        Assert.Single(GifDecoder.DecodeClassic(gif).Frames);
        Assert.Single(GifDecoder.DecodeStandard(gif).Frames);
        Assert.Equal(2, GifDecoder.DecodeStandard([.. first, .. second.AsSpan(13 + 3 * Colors.Length)]).Frames.Count);
    }

    [Fact]
    public void Classic_RejectsWhatTheOriginalRejected()
    {
        byte[] good = TestGif.Encode(1, 1, Colors, [Frame(1, 1, [1])]);

        Assert.Throws<InvalidDataException>(() => GifDecoder.DecodeClassic([]));
        Assert.Throws<InvalidDataException>(() => GifDecoder.DecodeClassic(good.AsSpan(0, good.Length - 1)));
        Assert.Throws<InvalidDataException>(() => GifDecoder.DecodeClassic([.. good.AsSpan(0, 13 + 12), 0x00, 0x3B]));
        Assert.Throws<InvalidDataException>(() => GifDecoder.DecodeClassic([(byte)'P', (byte)'N', (byte)'G', (byte)'8', (byte)'9', (byte)'a', 1, 0, 1, 0, 0, 0, 0, 0x3B]));
        Assert.Throws<InvalidDataException>(() => GifDecoder.DecodeClassic([.. good.AsSpan(0, 13 + 12), 0x3B]));
    }

    [Fact]
    public void Standard_ReadsDamagedFilesAsFarAsPossible()
    {
        byte[] good = TestGif.Encode(4, 1, Colors, [Frame(4, 1, [1, 2, 3, 1])]);
        byte[] cut = good.AsSpan(0, good.Length - 4).ToArray();

        GifAnimation animation = GifDecoder.DecodeStandard(cut);

        Assert.Single(animation.Frames);
        Assert.Equal(Red, animation.Frames[0].Pixels[0]);
        Assert.Throws<InvalidDataException>(() => GifDecoder.DecodeClassic(cut));
        Assert.Throws<InvalidDataException>(() => GifDecoder.DecodeStandard("GIF89a"u8));
        Assert.Throws<InvalidDataException>(() => GifDecoder.DecodeStandard([.. good.AsSpan(0, 13 + 12), 0x3B]));
    }

    [Fact]
    public void Standard_ClipsToTheLogicalScreenAndSizesAnEmptyOne()
    {
        byte[] clipped = TestGif.Encode(1, 1, Colors, [Frame(2, 1, [1, 2])]);
        byte[] empty = TestGif.Encode(0, 0, Colors, [Frame(2, 1, [1, 2])]);

        Assert.Equal([Red], GifDecoder.DecodeStandard(clipped).Frames[0].Pixels);
        GifAnimation sized = GifDecoder.DecodeStandard(empty);
        Assert.Equal(new SizeI(2, 1), sized.Size);
        Assert.Equal([Red, Green], sized.Frames[0].Pixels);
    }

    [Fact]
    public void ReadLogicalScreenSize_ReadsTheHeaderOnly()
    {
        byte[] gif = TestGif.Encode(321, 123, Colors, [Frame(1, 1, [1])]);

        Assert.Equal(new SizeI(321, 123), GifDecoder.ReadLogicalScreenSize(gif.AsSpan(0, 10)));
        Assert.Throws<InvalidDataException>(() => GifDecoder.ReadLogicalScreenSize("GIF89a"u8));
        Assert.Throws<InvalidDataException>(() => GifDecoder.ReadLogicalScreenSize([(byte)'J', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 1, 0, 1, 0]));
    }

    [Fact]
    public void Standard_MatchesPillowOnTheBundledGifsExceptWherePillowsOwnConventionsDiffer()
    {
        // Pillow keeps the previous disposal method when a control block says 0 ("unspecified") and grows the canvas
        // for images larger than the logical screen; the GIF specification and browsers do neither.
        string[] knownDifferences = ["Bolt.bul#2", "CarrotFlash.bul#0", "Psyduck.bul#0", "Butterfly5.bul#0", "SantaSleigh.bul#0"];
        var differences = new ConcurrentBag<string>();
        int compared = 0;
        Parallel.ForEach(BulbGoldens.BulFiles, golden =>
        {
            string name = golden.GetProperty("file").GetString()!;
            BulFile file = BulFile.Parse(BulbGoldens.ReadBundled(name), name);
            foreach (JsonElement entry in golden.GetProperty("entries").EnumerateArray())
            {
                JsonElement expected = entry.GetProperty("pillowSame").GetBoolean() ? entry : entry.GetProperty("pillow");
                if (expected.TryGetProperty("error", out _))
                {
                    continue;
                }

                int index = entry.GetProperty("index").GetInt32();
                GifAnimation animation = GifDecoder.DecodeStandard(file.Entries[index].Gif.Span);
                Interlocked.Increment(ref compared);
                if (!animation.Frames.Select(GoldenData.RgbaSha256).SequenceEqual(BulbGoldens.Strings(expected, "sha256")))
                {
                    differences.Add($"{name}#{index}");
                }
            }
        });

        Assert.Equal(4510, compared);
        Assert.Equal(knownDifferences.Order(), differences.Order());
    }

    private static TestGifFrame Frame(int width, int height, byte[] pixels) => new() { Width = width, Height = height, Pixels = pixels };
}
