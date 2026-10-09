namespace HolidayLights.Tests.Bulbs;

/// <summary>One image of a <see cref="TestGif"/>.</summary>
internal sealed record TestGifFrame
{
    /// <summary>Left of the image rectangle.</summary>
    public int Left { get; init; }

    /// <summary>Top of the image rectangle.</summary>
    public int Top { get; init; }

    /// <summary>Width of the image rectangle.</summary>
    public required int Width { get; init; }

    /// <summary>Height of the image rectangle.</summary>
    public required int Height { get; init; }

    /// <summary>Colour indices, row by row (top to bottom).</summary>
    public required byte[] Pixels { get; init; }

    /// <summary>A local colour table (RGB triples), or null.</summary>
    public (byte R, byte G, byte B)[]? LocalColors { get; init; }

    /// <summary>Store the rows interlaced.</summary>
    public bool Interlaced { get; init; }

    /// <summary>Write a graphic control extension before the image.</summary>
    public bool Control { get; init; } = true;

    /// <summary>Disposal method of the control extension.</summary>
    public int Disposal { get; init; }

    /// <summary>Transparent index of the control extension, or -1.</summary>
    public int TransparentIndex { get; init; } = -1;

    /// <summary>Delay in centiseconds.</summary>
    public int DelayCs { get; init; }
}

/// <summary>Writes small, valid GIF files for tests (literal-only LZW, so the code size never grows).</summary>
internal static class TestGif
{
    /// <summary>Encodes a GIF.</summary>
    /// <param name="width">Logical screen width.</param>
    /// <param name="height">Logical screen height.</param>
    /// <param name="globalColors">The global colour table (2, 4, 8, ... 256 entries), or null.</param>
    /// <param name="frames">The images.</param>
    /// <param name="background">Background colour index.</param>
    /// <param name="trailer">Write the 0x3B trailer.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] Encode(int width, int height, (byte R, byte G, byte B)[]? globalColors, IEnumerable<TestGifFrame> frames, int background = 0, bool trailer = true)
    {
        var bytes = new List<byte>();
        bytes.AddRange("GIF89a"u8.ToArray());
        AddShort(bytes, width);
        AddShort(bytes, height);
        bytes.Add((byte)(globalColors is null ? 0 : 0x80 | 0x70 | TableBits(globalColors.Length)));
        bytes.Add((byte)background);
        bytes.Add(0);
        AddColors(bytes, globalColors);
        foreach (TestGifFrame frame in frames)
        {
            if (frame.Control)
            {
                bool transparent = frame.TransparentIndex >= 0;
                bytes.AddRange([0x21, 0xF9, 4, (byte)(frame.Disposal << 2 | (transparent ? 1 : 0))]);
                AddShort(bytes, frame.DelayCs);
                bytes.AddRange([(byte)Math.Max(frame.TransparentIndex, 0), 0]);
            }

            bytes.Add(0x2C);
            AddShort(bytes, frame.Left);
            AddShort(bytes, frame.Top);
            AddShort(bytes, frame.Width);
            AddShort(bytes, frame.Height);
            bytes.Add((byte)((frame.LocalColors is null ? 0 : 0x80 | TableBits(frame.LocalColors.Length)) | (frame.Interlaced ? 0x40 : 0)));
            AddColors(bytes, frame.LocalColors);
            int colors = (frame.LocalColors ?? globalColors)?.Length ?? 256;
            int codeSize = Math.Max(2, TableBits(colors) + 1);
            bytes.Add((byte)codeSize);
            byte[] ordered = frame.Interlaced ? Interlace(frame.Pixels, frame.Width, frame.Height) : frame.Pixels;
            AddSubBlocks(bytes, Lzw(ordered, codeSize));
        }

        if (trailer)
        {
            bytes.Add(0x3B);
        }

        return [.. bytes];
    }

    /// <summary>A grey ramp palette of 2^bits entries.</summary>
    /// <param name="bits">1-8.</param>
    /// <returns>The palette.</returns>
    public static (byte R, byte G, byte B)[] Palette(int bits) =>
        [.. Enumerable.Range(0, 1 << bits).Select(i => ((byte)(i * 255 / ((1 << bits) - 1)), (byte)(i * 7 % 256), (byte)(255 - i * 255 / ((1 << bits) - 1))))];

    /// <summary>Encodes indices with literal codes only, sending a clear code before the table would grow.</summary>
    private static byte[] Lzw(byte[] pixels, int minimumCodeSize)
    {
        int clear = 1 << minimumCodeSize;
        int codeSize = minimumCodeSize + 1;
        int perChunk = (1 << codeSize) - clear - 2;
        var writer = new BitWriter();
        for (int i = 0; i < pixels.Length; i += perChunk)
        {
            writer.Write(clear, codeSize);
            for (int j = i; j < Math.Min(i + perChunk, pixels.Length); j++)
            {
                writer.Write(pixels[j], codeSize);
            }
        }

        writer.Write(clear + 1, codeSize);
        return writer.ToArray();
    }

    private static byte[] Interlace(byte[] pixels, int width, int height)
    {
        var rows = new List<int>();
        foreach ((int start, int step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
        {
            for (int y = start; y < height; y += step)
            {
                rows.Add(y);
            }
        }

        return [.. rows.SelectMany(y => pixels.Skip(y * width).Take(width))];
    }

    private static int TableBits(int count)
    {
        int bits = 0;
        while (2 << bits < count)
        {
            bits++;
        }

        return bits;
    }

    private static void AddColors(List<byte> bytes, (byte R, byte G, byte B)[]? colors)
    {
        if (colors is null)
        {
            return;
        }

        int size = 2 << TableBits(colors.Length);
        for (int i = 0; i < size; i++)
        {
            (byte r, byte g, byte b) = i < colors.Length ? colors[i] : ((byte)0, (byte)0, (byte)0);
            bytes.AddRange([r, g, b]);
        }
    }

    private static void AddSubBlocks(List<byte> bytes, byte[] data)
    {
        for (int i = 0; i < data.Length; i += 255)
        {
            int length = Math.Min(255, data.Length - i);
            bytes.Add((byte)length);
            bytes.AddRange(data.Skip(i).Take(length));
        }

        bytes.Add(0);
    }

    private static void AddShort(List<byte> bytes, int value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
    }

    private sealed class BitWriter
    {
        private readonly List<byte> bytes = [];
        private int buffer;
        private int bits;

        public void Write(int code, int size)
        {
            buffer |= code << bits;
            bits += size;
            while (bits >= 8)
            {
                bytes.Add((byte)buffer);
                buffer >>= 8;
                bits -= 8;
            }
        }

        public byte[] ToArray() => bits > 0 ? [.. bytes, (byte)buffer] : [.. bytes];
    }
}
