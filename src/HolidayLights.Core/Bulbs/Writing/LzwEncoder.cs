namespace HolidayLights.Core.Bulbs.Writing;

/// <summary>
/// The variable-length LZW compression of GIF image data (GIF89a appendix F), written so that both the faithful 5.4 decoder
/// and standard decoders read it: codes grow when the decoder's next free code needs another bit, and a clear code is sent
/// as soon as the table is full (the decoder never needs a 13-bit code or a 4,097th entry).
/// </summary>
internal static class LzwEncoder
{
    private const int MaxCodeSize = 12;
    private const int TableLimit = 1 << MaxCodeSize;
    private const int MaxSubBlockLength = 255;

    /// <summary>Appends the image data of one picture: the minimum code size, the compressed codes in sub-blocks and the block terminator.</summary>
    /// <param name="indices">Colour indices, every one below <c>2^minimumCodeSize</c>.</param>
    /// <param name="minimumCodeSize">The LZW minimum code size (2-8).</param>
    /// <param name="output">Receives the bytes.</param>
    public static void Encode(ReadOnlySpan<byte> indices, int minimumCodeSize, List<byte> output)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumCodeSize, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumCodeSize, 8);
        if (indices.IsEmpty)
        {
            throw new ArgumentException("A picture has at least one pixel.", nameof(indices));
        }

        int clear = 1 << minimumCodeSize;
        var writer = new BitWriter();
        var table = new Dictionary<int, int>();
        int codeSize = minimumCodeSize + 1;
        int next = clear + 2;
        writer.Write(clear, codeSize);
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int key = prefix << 8 | indices[i];
            if (table.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }

            writer.Write(prefix, codeSize);
            table[key] = next;
            Advance(ref next, ref codeSize);
            if (next == TableLimit)
            {
                writer.Write(clear, codeSize);
                table.Clear();
                next = clear + 2;
                codeSize = minimumCodeSize + 1;
            }

            prefix = indices[i];
        }

        writer.Write(prefix, codeSize);

        // The decoder adds an entry for the last code too, which can widen the end-of-information code.
        Advance(ref next, ref codeSize);
        writer.Write(clear + 1, codeSize);

        output.Add((byte)minimumCodeSize);
        ReadOnlySpan<byte> data = writer.ToArray();
        for (int start = 0; start < data.Length; start += MaxSubBlockLength)
        {
            ReadOnlySpan<byte> block = data.Slice(start, Math.Min(MaxSubBlockLength, data.Length - start));
            output.Add((byte)block.Length);
            output.AddRange(block);
        }

        output.Add(0);
    }

    /// <summary>One more table entry; the next code is one bit wider once the decoder's next free code needs it.</summary>
    private static void Advance(ref int next, ref int codeSize)
    {
        next++;
        if (next > 1 << codeSize && codeSize < MaxCodeSize)
        {
            codeSize++;
        }
    }

    /// <summary>Packs codes least significant bit first.</summary>
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
