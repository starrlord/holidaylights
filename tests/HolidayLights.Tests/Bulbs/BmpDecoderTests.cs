using System.Buffers.Binary;
using HolidayLights.Core.Imaging;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

public sealed class BmpDecoderTests
{
    private const uint Red = 0xFFFF0000;
    private const uint Green = 0xFF00FF00;
    private const uint Blue = 0xFF0000FF;
    private const uint White = 0xFFFFFFFF;
    private const uint Black = 0xFF000000;

    /// <summary>The second bitmap of each shipped picture, hashed with Pillow (file, width, height, RGBA SHA-256).</summary>
    public static TheoryData<string, int, int, string> Pictures => new()
    {
        { "Cake.BMP", 200, 221, "8ec2109a7b97063f7c5cd80e253e37230aca005fbc4d4c4a7861e28425289942" },
        { "Easter Bunny.BMP", 122, 220, "9105827e4854fd14e89aeecda458b51517af256a43c4b58eae46959cf85ff45f" },
        { "Flag.BMP", 250, 158, "10d0fa8903866e7d6e89005dc775fb292b11f5e7302a64d938d08a6c841ddb3d" },
        { "Hearts.BMP", 178, 114, "978dd9a4d0d5dd710e386a3299d3c8c2d753504f017b137e3e1774d773fc10a0" },
        { "New Year Clock.BMP", 102, 145, "0e9df98e95c18e2045f72cf943d0fffd6a1b07cb829e235352a81e4650cc9437" },
        { "Pot of Gold.BMP", 178, 163, "8d69b370c52cd8ab091d71d54d4fe3140db5b070e864aafa6d96bd9c9e2b48d0" },
        { "Pumpkin.BMP", 200, 200, "245fa33da77efd3405ab74dfb3a2271c7bbb7ec4824f159924636ca347eb65b4" },
        { "Santa Candle.BMP", 282, 297, "1eee3befe1544ab0f154548d5af8736cdc479a50ddada05371523ab7e4a9214a" },
        { "Snowman.BMP", 300, 256, "12cc2b510a8010884fa83f47b2297dc9c0fb2173ce4f4d53f8de4361af2f743e" },
        { "Turkey.BMP", 192, 188, "cb158d4b2e6b5d74491d5b71e738cec57b3602533029461ee323e1b798aad187" },
        { "Witch.BMP", 192, 191, "e3ab341a203a1854a7c5ea928cab46ca437d385da3bd88ec6f221bfd4dbb9b4e" },
    };

    [Theory]
    [MemberData(nameof(Pictures))]
    public void DecodePicture_UsesTheSecondBitmapOfTheShippedPictures(string file, int width, int height, string sha256)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(TestPaths.ContentFolder, "Pictures", file));

        Rgba32Image picture = BmpDecoder.DecodePicture(data);

        Assert.Equal(new SizeI(width, height), picture.Size);
        Assert.Equal(sha256, GoldenData.RgbaSha256(picture));
        // The first bitmap is the 1 bpp licence notice.
        Assert.Equal(file == "Pot of Gold.BMP" ? new SizeI(304, 120) : new SizeI(310, 77), BmpDecoder.Decode(data).Size);
    }

    [Fact]
    public void DecodePicture_ReadsASingleBitmap()
    {
        byte[] bmp = Build(24, 2, 1, row => [0, 0, 255, 255, 0, 0]);

        Rgba32Image image = BmpDecoder.DecodePicture(bmp);

        Assert.Equal([Red, Blue], image.Pixels);
    }

    [Fact]
    public void DecodeMasked_SunMaskShorterThanTheArtLeavesTheLastRowsTransparent()
    {
        Rgba32Image sun = BmpDecoder.DecodeMasked(
            EmbeddedAssets.ReadAllBytes(EmbeddedAssets.BuiltInBitmap(1081)), EmbeddedAssets.ReadAllBytes(EmbeddedAssets.BuiltInBitmap(1082)));

        Assert.Equal(new SizeI(136, 32), sun.Size);
        Assert.Equal("ed1102c1f22eca1d0033d6742a346a1f58aeb9427539f363977a4fa8198c849c", GoldenData.RgbaSha256(sun));
        Assert.All(sun.GetRow(30).ToArray().Concat(sun.GetRow(31).ToArray()), p => Assert.Equal(0u, p));
        Assert.All(sun.Pixels, p => Assert.True(Bgra32.A(p) is 0 or 255 && (Bgra32.A(p) == 255 || p == 0)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void Decode_PalettedBottomUp(int bpp)
    {
        // Rows are stored bottom-up: the first stored row is the bottom one.
        byte[] bmp = Build(bpp, 3, 2, row => Packed(bpp, row == 0 ? [1, 0, 1] : [0, 1, 0]), palette: [White, Red]);

        Rgba32Image image = BmpDecoder.Decode(bmp);

        Assert.Equal([White, Red, White, Red, White, Red], image.Pixels);
    }

    [Fact]
    public void Decode_TopDown24AndBgrx32()
    {
        byte[] topDown = Build(24, 1, 2, row => row == 0 ? [255, 0, 0] : [0, 255, 0], topDown: true);
        byte[] bgrx = Build(32, 2, 1, _ => [255, 0, 0, 7, 0, 0, 255, 99]);

        Assert.Equal([Blue, Green], BmpDecoder.Decode(topDown).Pixels);
        Assert.Equal([Blue, Red], BmpDecoder.Decode(bgrx).Pixels);
    }

    [Fact]
    public void Decode_Rgb555AndBitFields565()
    {
        byte[] rgb555 = Build(16, 2, 1, _ => [0x00, 0x7C, 0x1F, 0x00]);
        byte[] rgb565 = Build(16, 2, 1, _ => [0x00, 0xF8, 0xE0, 0x07], masks: [0xF800, 0x07E0, 0x001F]);

        Assert.Equal([Red, Blue], BmpDecoder.Decode(rgb555).Pixels);
        Assert.Equal([Red, Green], BmpDecoder.Decode(rgb565).Pixels);
    }

    [Fact]
    public void Decode_Rle8AndRle4()
    {
        // RLE8: run of 2 x index 1, absolute run of 3 (0,1,0 + pad), end of line, delta skip, end of bitmap.
        byte[] rle8 = Build(8, 5, 2, _ => [], palette: [White, Red], compression: 1,
            data: [2, 1, 0, 3, 0, 1, 0, 0, 0, 0, 1, 1, 0, 1]);
        // RLE4: run of 5 alternating nibbles 1,0,1,0,1.
        byte[] rle4 = Build(4, 5, 1, _ => [], palette: [White, Red], compression: 2, data: [5, 0x10, 0, 1]);

        // RLE rows run bottom-up; pixels the data skips stay black.
        Rgba32Image image8 = BmpDecoder.Decode(rle8);
        Assert.Equal([Red, Black, Black, Black, Black], image8.GetRow(0).ToArray());
        Assert.Equal([Red, Red, White, Red, White], image8.GetRow(1).ToArray());
        Assert.Equal([Red, White, Red, White, Red], BmpDecoder.Decode(rle4).Pixels);
    }

    [Fact]
    public void Decode_CoreHeader()
    {
        var bmp = new List<byte>();
        bmp.AddRange("BM"u8.ToArray());
        bmp.AddRange(new byte[12]);
        bmp.AddRange(Le32(12));
        bmp.AddRange([2, 0, 1, 0, 1, 0, 8, 0]);
        bmp.AddRange([255, 255, 255, 0, 0, 255]);
        bmp.AddRange([1, 0, 0, 0]);
        byte[] data = [.. bmp];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 14 + 12 + 6);

        Assert.Equal([Red, White], BmpDecoder.Decode(data).Pixels);
    }

    [Fact]
    public void Decode_RejectsWhatIsNotASupportedBmp()
    {
        Assert.Throws<InvalidDataException>(() => BmpDecoder.Decode([]));
        Assert.Throws<InvalidDataException>(() => BmpDecoder.Decode("GIF89a........................................................"u8));
        Assert.Throws<InvalidDataException>(() => BmpDecoder.Decode(Build(24, 0, 1, _ => [])));
        Assert.Throws<InvalidDataException>(() => BmpDecoder.Decode(Build(24, 1, 1, _ => [0, 0, 0], compression: 4)));
        Assert.Throws<InvalidDataException>(() => BmpDecoder.Decode(Build(8, 1, 1, _ => [0], compression: 2)));
    }

    [Fact]
    public void Decode_TruncatedPixelsLeaveMissingRowsBlack()
    {
        byte[] full = Build(24, 1, 2, row => row == 0 ? [255, 255, 255] : [0, 0, 255]);

        Rgba32Image image = BmpDecoder.Decode(full.AsSpan(0, full.Length - 4));

        Assert.Equal([Black, White], image.Pixels);
    }

    private static byte[] Packed(int bpp, int[] indices)
    {
        var bytes = new byte[(indices.Length * bpp + 7) / 8];
        for (int i = 0; i < indices.Length; i++)
        {
            int bit = i * bpp;
            bytes[bit / 8] |= (byte)(indices[i] << (8 - bpp - bit % 8));
        }

        return bytes;
    }

    private static byte[] Build(
        int bpp, int width, int height, Func<int, byte[]> row, uint[]? palette = null, bool topDown = false,
        uint[]? masks = null, uint compression = 0, byte[]? data = null)
    {
        int stride = (width * bpp + 31) / 32 * 4;
        var pixels = new List<byte>();
        if (data is not null)
        {
            pixels.AddRange(data);
        }
        else
        {
            for (int r = 0; r < height; r++)
            {
                byte[] bytes = row(r);
                pixels.AddRange(bytes);
                pixels.AddRange(new byte[Math.Max(0, stride - bytes.Length)]);
            }
        }

        uint actualCompression = masks is null ? compression : 3;
        var file = new List<byte>();
        file.AddRange("BM"u8.ToArray());
        file.AddRange(new byte[12]);
        file.AddRange(Le32(40));
        file.AddRange(Le32(width));
        file.AddRange(Le32(topDown ? -height : height));
        file.AddRange([1, 0, (byte)bpp, 0]);
        file.AddRange(Le32((int)actualCompression));
        file.AddRange(Le32(pixels.Count));
        file.AddRange(new byte[8]);
        file.AddRange(Le32(palette?.Length ?? 0));
        file.AddRange(Le32(0));
        foreach (uint mask in masks ?? [])
        {
            file.AddRange(Le32((int)mask));
        }

        foreach (uint color in palette ?? [])
        {
            file.AddRange([(byte)color, (byte)(color >> 8), (byte)(color >> 16), 0]);
        }

        int offset = file.Count;
        file.AddRange(pixels);
        byte[] result = [.. file];
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(2), result.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(10), offset);
        return result;
    }

    private static byte[] Le32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
