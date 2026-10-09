using System.Buffers.Binary;

namespace HolidayLights.Core.Imaging;

/// <summary>
/// Parses one Windows BMP file (<c>BITMAPFILEHEADER</c> + <c>BITMAPCOREHEADER</c>/<c>BITMAPINFOHEADER</c>/V4/V5) into
/// an opaque image: 1, 4 and 8 bpp with a colour table (uncompressed or RLE), 16 and 32 bpp (RGB or bit fields) and
/// 24 bpp, bottom-up or top-down.
/// </summary>
/// <remarks>
/// Rows that the file does not contain (a truncated picture) stay black. The pixel data starts at <c>bfOffBits</c> when
/// that points between the headers and the end of the file (the colour table ends there at the latest), otherwise right
/// after the colour table (the 5.4 reader ignored <c>bfOffBits</c>; both agree for every 5.4 bitmap).
/// </remarks>
internal static class BmpReader
{
    /// <summary>Size of <c>BITMAPFILEHEADER</c>.</summary>
    public const int FileHeaderSize = 14;

    private const int MaxDimension = 32768;
    private const long MaxPixels = 64L * 1024 * 1024;

    private const int CoreHeaderSize = 12;
    private const int InfoHeaderSize = 40;

    private const uint BiRgb = 0;
    private const uint BiRle8 = 1;
    private const uint BiRle4 = 2;
    private const uint BiBitFields = 3;
    private const uint BiAlphaBitFields = 6;

    /// <summary>Reads <c>bfSize</c>, the size the file header claims for this bitmap.</summary>
    /// <param name="file">At least the 14-byte file header.</param>
    /// <returns>The claimed size, or -1 when the data does not start with a file header.</returns>
    public static long ReadClaimedSize(ReadOnlySpan<byte> file) =>
        file.Length >= 6 && file[0] == (byte)'B' && file[1] == (byte)'M' ? BinaryPrimitives.ReadUInt32LittleEndian(file[2..]) : -1;

    /// <summary>Decodes the bitmap at the start of <paramref name="file"/>.</summary>
    /// <param name="file">The file bytes.</param>
    /// <returns>The image; every pixel has alpha 255.</returns>
    /// <exception cref="InvalidDataException">The data is not a supported BMP.</exception>
    public static Rgba32Image Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < FileHeaderSize + CoreHeaderSize || file[0] != (byte)'B' || file[1] != (byte)'M')
        {
            throw new InvalidDataException("The data is not a BMP file.");
        }

        BmpLayout layout = ReadLayout(file);
        var pixels = new uint[checked(layout.Width * layout.Height)];
        Array.Fill(pixels, 0xFF000000u);
        ReadOnlySpan<byte> bits = layout.BitsOffset < file.Length ? file[layout.BitsOffset..] : [];
        switch (layout.Compression)
        {
            case BiRle8:
            case BiRle4:
                DecodeRle(bits, layout, pixels);
                break;
            default:
                DecodeRows(bits, layout, pixels);
                break;
        }

        return new Rgba32Image(layout.Width, layout.Height, pixels);
    }

    private static BmpLayout ReadLayout(ReadOnlySpan<byte> file)
    {
        ReadOnlySpan<byte> dib = file[FileHeaderSize..];
        int headerSize = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(dib), int.MaxValue);
        int width;
        int height;
        int bpp;
        uint compression = BiRgb;
        int colorsUsed = 0;
        int paletteEntrySize;
        if (headerSize == CoreHeaderSize)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(dib[4..]);
            height = BinaryPrimitives.ReadUInt16LittleEndian(dib[6..]);
            bpp = BinaryPrimitives.ReadUInt16LittleEndian(dib[10..]);
            paletteEntrySize = 3;
        }
        else if (headerSize >= InfoHeaderSize && file.Length >= FileHeaderSize + InfoHeaderSize)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
            bpp = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
            compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
            colorsUsed = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]), 4096);
            paletteEntrySize = 4;
        }
        else
        {
            throw new InvalidDataException($"Unsupported BMP header size {headerSize}.");
        }

        bool topDown = height < 0;
        long absHeight = Math.Abs((long)height);
        if (width <= 0 || absHeight == 0 || width > MaxDimension || absHeight > MaxDimension || width * absHeight > MaxPixels)
        {
            throw new InvalidDataException($"Unsupported BMP size {width} x {height}.");
        }

        ValidateFormat(bpp, compression);

        long maskStart = FileHeaderSize + (long)headerSize;
        uint[] masks = ReadMasks(file, headerSize, maskStart, bpp, compression);
        long paletteStart = maskStart;
        if (headerSize == InfoHeaderSize && compression is BiBitFields or BiAlphaBitFields)
        {
            paletteStart += compression == BiBitFields ? 12 : 16;
        }

        // The colour table ends where the bits start when bfOffBits says so (some writers store fewer entries than
        // 2^bpp); a bfOffBits that points nowhere sensible is ignored and the bits follow the table (as in 5.4).
        int paletteCount = bpp <= 8 ? (colorsUsed == 0 ? 1 << bpp : Math.Min(colorsUsed, 1 << bpp)) : colorsUsed;
        long claimedOffset = BinaryPrimitives.ReadUInt32LittleEndian(file[10..]);
        bool claimedValid = claimedOffset >= paletteStart && claimedOffset < file.Length;
        if (claimedValid)
        {
            paletteCount = (int)Math.Min(paletteCount, (claimedOffset - paletteStart) / paletteEntrySize);
        }

        uint[] palette = ReadPalette(file, paletteStart, bpp <= 8 ? paletteCount : 0, paletteEntrySize);
        long bitsOffset = claimedValid ? claimedOffset : paletteStart + (long)paletteCount * paletteEntrySize;
        return new BmpLayout(width, (int)absHeight, topDown, bpp, compression, palette, masks, (int)Math.Min(bitsOffset, int.MaxValue));
    }

    private static void ValidateFormat(int bpp, uint compression)
    {
        bool supported = compression switch
        {
            BiRgb => bpp is 1 or 4 or 8 or 16 or 24 or 32,
            BiRle8 => bpp == 8,
            BiRle4 => bpp == 4,
            BiBitFields or BiAlphaBitFields => bpp is 16 or 32,
            _ => false,
        };
        if (!supported)
        {
            throw new InvalidDataException($"Unsupported BMP format ({bpp} bpp, compression {compression}).");
        }
    }

    private static uint[] ReadMasks(ReadOnlySpan<byte> file, int headerSize, long maskStart, int bpp, uint compression)
    {
        if (compression is not (BiBitFields or BiAlphaBitFields))
        {
            // BI_RGB: 16 bpp is x1r5g5b5, 32 bpp is x8r8g8b8.
            return bpp == 16 ? [0x7C00, 0x03E0, 0x001F] : [0x00FF0000, 0x0000FF00, 0x000000FF];
        }

        // V2+ headers carry the masks inside the header (offset 40); a plain info header is followed by them.
        long offset = headerSize > InfoHeaderSize ? FileHeaderSize + InfoHeaderSize : maskStart;
        if (offset + 12 > file.Length)
        {
            throw new InvalidDataException("The BMP bit-field masks are missing.");
        }

        ReadOnlySpan<byte> m = file[(int)offset..];
        return
        [
            BinaryPrimitives.ReadUInt32LittleEndian(m),
            BinaryPrimitives.ReadUInt32LittleEndian(m[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(m[8..]),
        ];
    }

    private static uint[] ReadPalette(ReadOnlySpan<byte> file, long start, int count, int entrySize)
    {
        var palette = new uint[256];
        for (int i = 0; i < count; i++)
        {
            long at = start + (long)i * entrySize;
            if (at + 3 > file.Length)
            {
                break;
            }

            // RGBQUAD / RGBTRIPLE are stored blue, green, red.
            palette[i] = (uint)(file[(int)at + 2] << 16 | file[(int)at + 1] << 8 | file[(int)at]);
        }

        return palette;
    }

    private static void DecodeRows(ReadOnlySpan<byte> bits, BmpLayout layout, uint[] pixels)
    {
        long stride = ((long)layout.Width * layout.BitsPerPixel + 31) / 32 * 4;
        var fields = layout.BitsPerPixel is 16 or 32 ? BitField.FromMasks(layout.Masks) : null;
        for (int row = 0; row < layout.Height; row++)
        {
            int fileRow = layout.TopDown ? row : layout.Height - 1 - row;
            long start = fileRow * stride;
            if (start + stride > bits.Length)
            {
                continue;
            }

            ReadOnlySpan<byte> src = bits.Slice((int)start, (int)stride);
            Span<uint> dst = pixels.AsSpan(row * layout.Width, layout.Width);
            DecodeRow(src, dst, layout, fields);
        }
    }

    private static void DecodeRow(ReadOnlySpan<byte> src, Span<uint> dst, BmpLayout layout, BitField[]? fields)
    {
        uint[] palette = layout.Palette;
        switch (layout.BitsPerPixel)
        {
            case 1:
                for (int x = 0; x < dst.Length; x++)
                {
                    dst[x] = 0xFF000000 | palette[(src[x >> 3] >> (7 - (x & 7))) & 1];
                }

                break;
            case 4:
                for (int x = 0; x < dst.Length; x++)
                {
                    int b = src[x >> 1];
                    dst[x] = 0xFF000000 | palette[(x & 1) == 0 ? b >> 4 : b & 0x0F];
                }

                break;
            case 8:
                for (int x = 0; x < dst.Length; x++)
                {
                    dst[x] = 0xFF000000 | palette[src[x]];
                }

                break;
            case 16:
                for (int x = 0; x < dst.Length; x++)
                {
                    dst[x] = BitField.Compose(fields!, BinaryPrimitives.ReadUInt16LittleEndian(src[(x * 2)..]));
                }

                break;
            case 24:
                for (int x = 0; x < dst.Length; x++)
                {
                    int i = x * 3;
                    dst[x] = 0xFF000000 | (uint)(src[i + 2] << 16 | src[i + 1] << 8 | src[i]);
                }

                break;
            default:
                for (int x = 0; x < dst.Length; x++)
                {
                    dst[x] = BitField.Compose(fields!, BinaryPrimitives.ReadUInt32LittleEndian(src[(x * 4)..]));
                }

                break;
        }
    }

    private static void DecodeRle(ReadOnlySpan<byte> data, BmpLayout layout, uint[] pixels)
    {
        bool rle4 = layout.Compression == BiRle4;
        int x = 0;
        int y = 0;
        int p = 0;
        while (p + 1 < data.Length && y < layout.Height)
        {
            int count = data[p];
            int value = data[p + 1];
            p += 2;
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = rle4 ? (i & 1) == 0 ? value >> 4 : value & 0x0F : value;
                    PutRle(layout, pixels, x++, y, index);
                }

                continue;
            }

            switch (value)
            {
                case 0:
                    x = 0;
                    y++;
                    break;
                case 1:
                    return;
                case 2:
                    if (p + 1 >= data.Length)
                    {
                        return;
                    }

                    x += data[p];
                    y += data[p + 1];
                    p += 2;
                    break;
                default:
                    int bytes = rle4 ? (value + 1) / 2 : value;
                    for (int i = 0; i < value && p + (rle4 ? i / 2 : i) < data.Length; i++)
                    {
                        int index = rle4 ? (i & 1) == 0 ? data[p + i / 2] >> 4 : data[p + i / 2] & 0x0F : data[p + i];
                        PutRle(layout, pixels, x++, y, index);
                    }

                    p += bytes + (bytes & 1);
                    break;
            }
        }
    }

    private static void PutRle(BmpLayout layout, uint[] pixels, int x, int y, int index)
    {
        if (x >= layout.Width || y >= layout.Height)
        {
            return;
        }

        int row = layout.TopDown ? y : layout.Height - 1 - y;
        pixels[row * layout.Width + x] = 0xFF000000 | layout.Palette[index];
    }

    private sealed record BmpLayout(
        int Width, int Height, bool TopDown, int BitsPerPixel, uint Compression, uint[] Palette, uint[] Masks, int BitsOffset);

    /// <summary>One colour channel of a bit-field pixel format.</summary>
    private sealed class BitField
    {
        private readonly uint mask;
        private readonly int shift;
        private readonly uint max;

        private BitField(uint mask)
        {
            this.mask = mask;
            shift = mask == 0 ? 0 : System.Numerics.BitOperations.TrailingZeroCount(mask);
            max = mask == 0 ? 0 : mask >> shift;
        }

        public static BitField[] FromMasks(uint[] masks) => [new(masks[0]), new(masks[1]), new(masks[2])];

        public static uint Compose(BitField[] fields, uint value) =>
            0xFF000000 | fields[0].Extract(value) << 16 | fields[1].Extract(value) << 8 | fields[2].Extract(value);

        private uint Extract(uint value)
        {
            if (max == 0)
            {
                return 0;
            }

            uint v = (value & mask) >> shift;
            return (uint)(((ulong)v * 255 + max / 2) / max);
        }
    }
}
