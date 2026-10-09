namespace HolidayLights.Core.Imaging;

/// <summary>
/// The faithful 5.4 GIF decoder: a port of 5.4's <c>Gif_DecodeToStrip</c>, which it matches bit for bit on all 4,511 GIFs
/// of the bundled bulbs (golden <c>bul-frames.json.gz</c>).
/// </summary>
/// <remarks>
/// <para>Deviations from the GIF specification that are reproduced on purpose: frame delays are ignored; a transparent
/// pixel inside a frame's rectangle punches a hole instead of showing the previous frame; a local colour table and the
/// black transparent entry persist into later frames; earlier frames are re-mapped to the nearest colour of each new
/// palette; graphic control values persist; disposal 2 and 3 fill the whole frame slot (and the remembered disposal is
/// the one of the control block before the latest); image rectangles are not clipped (writes wrap through the strip
/// like the original's DIB memory); decoding stops at the first unexpected block byte.</para>
/// <para>Malformed input the original would have crashed or hung on (a logical screen of size 0, an LZW code size above
/// 11, pictures beyond <see cref="MaxStripPixels"/>, more than <see cref="MaxWork"/> pixel writes and colour re-mappings)
/// is rejected with <see cref="InvalidDataException"/>; none of the bundled bulbs comes near these limits.</para>
/// </remarks>
internal ref struct ClassicGifReader
{
    /// <summary>Largest strip (frames x width x height) decoded, to bound memory for malformed files.</summary>
    private const long MaxStripPixels = 32L * 1024 * 1024;

    /// <summary>Most pixel writes plus colour re-mappings spent on one GIF, to bound the time a malformed file can take.</summary>
    private const long MaxWork = 256L * 1024 * 1024;

    private const int TableSize = 4096;
    private const int MaxChain = 4200;

    private readonly ReadOnlySpan<byte> gif;
    private readonly bool firstFrameOnly;
    private readonly uint[] palette = new uint[256];
    private readonly ushort[] prefix = new ushort[TableSize];
    private readonly byte[] suffix = new byte[TableSize];
    private readonly byte[] stringStack = new byte[MaxChain + 8];
    private readonly List<byte[]> colorSlots = [];
    private readonly List<byte[]> maskSlots = [];

    private int p;
    private int width;
    private int height;
    private int background;
    private int paletteMask;
    private int paletteCount = -1;
    private uint[]? stripPalette;

    private int disposalCurrent;
    private int disposalPrevious;
    private bool transparent;
    private int transparentIndex;

    private int previousCode;
    private int firstCharacter;
    private long work;

    // Image rectangle and write position of the frame being decoded (strip coordinates).
    private long x0;
    private long y0;
    private long x1;
    private long y1;
    private long x;
    private long y;
    private int pass;
    private bool interlaced;
    private long stripWidth;

    // The slot of the frame being decoded: every write inside the strip lands in it (x0 >= its left edge, x < stripWidth).
    private byte[] currentColor = [];
    private byte[] currentMask = [];
    private long slotOrigin;

    private ClassicGifReader(ReadOnlySpan<byte> gif, bool firstFrameOnly)
    {
        this.gif = gif;
        this.firstFrameOnly = firstFrameOnly;
    }

    /// <summary>Decodes a GIF the way 5.4 did.</summary>
    /// <param name="gif">The GIF file bytes.</param>
    /// <param name="firstFrameOnly">Stop at the second image descriptor (5.4 GIF backgrounds of the screen saver).</param>
    /// <returns>The strip.</returns>
    /// <exception cref="InvalidDataException">The original decoder would have thrown (the art is drawn as WARNING).</exception>
    public static ClassicGifStrip Decode(ReadOnlySpan<byte> gif, bool firstFrameOnly)
    {
        var reader = new ClassicGifReader(gif, firstFrameOnly);
        return reader.Run();
    }

    private ClassicGifStrip Run()
    {
        ReadHeader();
        int block = At(p++);
        int frames = 0;
        while (block != 0x3B)
        {
            if (!SkipExtensions(ref block, frames))
            {
                break;
            }

            frames++;
            if (frames > 1 && firstFrameOnly)
            {
                break;
            }

            DecodeFrame(frames);
            block = At(p++);
        }

        if (colorSlots.Count == 0 || stripPalette is null)
        {
            throw new InvalidDataException("The GIF contains no image.");
        }

        return new ClassicGifStrip(width, height, colorSlots, maskSlots, stripPalette);
    }

    private void ReadHeader()
    {
        if (gif.Length == 0)
        {
            throw new InvalidDataException("The GIF is empty.");
        }

        if (gif[^1] != 0x3B)
        {
            throw new InvalidDataException("The GIF does not end with its trailer byte.");
        }

        if (gif.Length < 13)
        {
            throw new InvalidDataException("The GIF header is truncated.");
        }

        if (!GifDecoder.HasSignature(gif))
        {
            throw new InvalidDataException("The data is not a GIF.");
        }

        width = gif[6] | gif[7] << 8;
        height = gif[8] | gif[9] << 8;
        if (width == 0 || height == 0)
        {
            throw new InvalidDataException("The GIF has an empty logical screen.");
        }

        int packed = gif[10];
        background = gif[11];
        paletteMask = (1 << ((packed & 7) + 1)) - 1;
        p = 13;
        if ((packed & 0x80) != 0)
        {
            int count = paletteMask + 1;
            ReadColorTable(count);
            paletteCount = count;
        }
    }

    /// <summary>The extension loop. Returns false when decoding stops before another image.</summary>
    private bool SkipExtensions(ref int block, int frames)
    {
        while (block != 0x2C)
        {
            if (block != 0x21)
            {
                return frames > 0 ? false : throw new InvalidDataException("Unexpected block before the first GIF image.");
            }

            int label = At(p++);
            switch (label)
            {
                case 0xF9:
                    // Assumes block size 4; the delay is ignored. The previous disposal is the one used for the next image.
                    int packed = At(p + 1);
                    disposalPrevious = disposalCurrent;
                    disposalCurrent = (packed >> 2) & 7;
                    transparent = (packed & 1) != 0;
                    transparentIndex = At(p + 4);
                    p += 6;
                    break;
                case 0xFE:
                case 0xFF:
                    if (label == 0xFF)
                    {
                        p += 12;
                    }

                    SkipSubBlocks();
                    break;
                case 0x01:
                    // Plain text: skips 13 bytes, then scans byte by byte to the first 0 (not sub-block aware).
                    p += 13;
                    while (true)
                    {
                        int c = At(p++);
                        if (c == 0 || p > gif.Length)
                        {
                            break;
                        }
                    }

                    break;
                default:
                    return frames > 0 ? false : throw new InvalidDataException("Unknown GIF extension before the first image.");
            }

            block = At(p++);
        }

        return true;
    }

    private void SkipSubBlocks()
    {
        while (true)
        {
            int length = At(p);
            p += length + 1;
            if (length == 0 || p > gif.Length)
            {
                return;
            }
        }
    }

    private void DecodeFrame(int n)
    {
        int left = At(p) | At(p + 1) << 8;
        int top = At(p + 2) | At(p + 3) << 8;
        int frameWidth = At(p + 4) | At(p + 5) << 8;
        int frameHeight = At(p + 6) | At(p + 7) << 8;
        int packed = At(p + 8);
        p += 9;

        stripWidth = (long)width * n;
        if (stripWidth * height > MaxStripPixels)
        {
            throw new InvalidDataException("The GIF is too large.");
        }

        x0 = left + (long)(n - 1) * width;
        y0 = top;
        x1 = x0 + frameWidth;
        y1 = y0 + frameHeight;
        interlaced = ((packed >> 6) & 1) != 0;
        if ((packed & 0x80) != 0)
        {
            int count = 1 << ((packed & 7) + 1);
            ReadColorTable(count);
            paletteCount = count;
            paletteMask = count - 1;
        }

        if (transparent)
        {
            palette[transparentIndex] = 0;
        }

        uint[] framePalette = new uint[256];
        Array.Copy(palette, framePalette, paletteCount < 0 ? 256 : paletteCount);
        AddSlot(framePalette);
        DecodeImageData();
    }

    /// <summary>Creates the bitmaps of frame slot n (step 4 of the 5.4 decoder).</summary>
    private void AddSlot(uint[] framePalette)
    {
        int size = width * height;
        var color = new byte[size];
        var mask = new byte[size];
        if (stripPalette is null)
        {
            Array.Fill(color, (byte)transparentIndex);
            Array.Fill(mask, (byte)1);
        }
        else
        {
            if (!stripPalette.AsSpan().SequenceEqual(framePalette))
            {
                AddWork((long)colorSlots.Count * size);
                byte[] translation = BuildTranslation(stripPalette, framePalette);
                foreach (byte[] slot in colorSlots)
                {
                    for (int i = 0; i < slot.Length; i++)
                    {
                        slot[i] = translation[slot[i]];
                    }
                }
            }

            switch (disposalPrevious)
            {
                case 0:
                case 1:
                    colorSlots[^1].CopyTo(color, 0);
                    maskSlots[^1].CopyTo(mask, 0);
                    break;
                case 2:
                case 3:
                    bool clear = transparent || disposalPrevious == 3;
                    Array.Fill(color, (byte)(clear ? transparentIndex : background));
                    Array.Fill(mask, clear ? (byte)1 : (byte)0);
                    break;
                default:
                    // Disposal 4-7: the slot keeps index 0 and an opaque mask.
                    break;
            }
        }

        colorSlots.Add(color);
        maskSlots.Add(mask);
        currentColor = color;
        currentMask = mask;
        slotOrigin = (long)(colorSlots.Count - 1) * width;
        stripPalette = framePalette;
    }

    /// <summary>GDI's colour translation between DIB colour tables: nearest colour, lowest index on ties.</summary>
    private static byte[] BuildTranslation(uint[] from, uint[] to)
    {
        var map = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            uint c = from[i];
            int r = (int)(c >> 16 & 0xFF);
            int g = (int)(c >> 8 & 0xFF);
            int b = (int)(c & 0xFF);
            int best = int.MaxValue;
            int bestIndex = 0;
            for (int j = 0; j < 256; j++)
            {
                uint d = to[j];
                int dr = r - (int)(d >> 16 & 0xFF);
                int dg = g - (int)(d >> 8 & 0xFF);
                int db = b - (int)(d & 0xFF);
                int distance = dr * dr + dg * dg + db * db;
                if (distance < best)
                {
                    best = distance;
                    bestIndex = j;
                    if (distance == 0)
                    {
                        break;
                    }
                }
            }

            map[i] = (byte)bestIndex;
        }

        return map;
    }

    private void DecodeImageData()
    {
        int minimumCodeSize = At(p++);
        if (minimumCodeSize > 11)
        {
            throw new InvalidDataException("The GIF image data has an invalid code size.");
        }

        int remaining = gif.Length - p;
        byte[] data = CollectSubBlocks(remaining);
        DecodeLzw(new LzwBitReader(data, remaining), minimumCodeSize);
    }

    /// <summary>Concatenates the image sub-blocks; throws when they would exceed the bytes left in the GIF.</summary>
    private byte[] CollectSubBlocks(int remaining)
    {
        // First pass: the 5.4 bounds check and the number of bytes that are really there.
        int scan = p;
        int collected = 0;
        while (true)
        {
            int blockLength = At(scan++);
            if (remaining < collected + blockLength)
            {
                throw new InvalidDataException("The GIF image data runs past the end of the file.");
            }

            collected += Math.Clamp(gif.Length - scan, 0, blockLength);
            scan += blockLength;
            if (blockLength == 0)
            {
                break;
            }
        }

        var data = new byte[collected];
        int length = 0;
        while (p < scan)
        {
            int blockLength = At(p++);
            int available = Math.Clamp(gif.Length - p, 0, blockLength);
            if (available > 0)
            {
                gif.Slice(p, available).CopyTo(data.AsSpan(length));
                length += available;
            }

            p += blockLength;
        }

        return data;
    }

    /// <summary>The LZW loop: stops at EOI or as soon as a code leaves the write position outside the image.</summary>
    private void DecodeLzw(LzwBitReader reader, int minimumCodeSize)
    {
        int clear = 1 << minimumCodeSize;
        int endOfInformation = clear + 1;
        int initialCodeSize = minimumCodeSize + 1;
        int codeSize = initialCodeSize;
        int threshold = 1 << codeSize;
        int codeMask = threshold - 1;
        int nextCode = clear + 2;
        x = x0;
        y = y0;
        pass = 0;

        int code = reader.Read(codeSize, codeMask);
        while (code != endOfInformation)
        {
            if (code == clear)
            {
                codeSize = initialCodeSize;
                threshold = 1 << codeSize;
                codeMask = threshold - 1;
                nextCode = clear + 2;
                previousCode = reader.Read(codeSize, codeMask);
                firstCharacter = previousCode & paletteMask;
                AddWork(1);
                Emit((byte)firstCharacter);
            }
            else
            {
                EmitString(code >= nextCode ? previousCode : code, code >= nextCode);
                if (nextCode < TableSize)
                {
                    prefix[nextCode] = (ushort)previousCode;
                    suffix[nextCode] = (byte)firstCharacter;
                }

                nextCode++;
                previousCode = code;
                if (nextCode >= threshold && codeSize < 12)
                {
                    codeSize++;
                    threshold *= 2;
                    codeMask = (1 << codeSize) - 1;
                }
            }

            if (x >= x1 || y >= y1)
            {
                return;
            }

            code = reader.Read(codeSize, codeMask);
        }
    }

    /// <summary>
    /// Outputs the string of a code by walking its prefixes while the code is above the palette mask; for a code not in
    /// the table yet (KwKwK) the string of the previous code plus the previous first character. Sets the new first character.
    /// </summary>
    private void EmitString(int start, bool appendFirstCharacter)
    {
        int length = 0;
        if (appendFirstCharacter)
        {
            stringStack[length++] = (byte)firstCharacter;
        }

        int c = start;
        while (c > paletteMask)
        {
            if (c >= TableSize || length > MaxChain)
            {
                throw new InvalidDataException("The GIF image data is corrupt.");
            }

            stringStack[length++] = suffix[c];
            c = (short)prefix[c];
        }

        firstCharacter = c & paletteMask;
        stringStack[length++] = (byte)firstCharacter;
        AddWork(length);
        for (int i = length - 1; i >= 0; i--)
        {
            Emit(stringStack[i]);
        }
    }

    /// <summary>Writes one pixel at the current position if it is inside the image rectangle, then advances.</summary>
    private void Emit(byte index)
    {
        if (y < y1 && x < x1)
        {
            if (x < stripWidth && y < height)
            {
                int i = (int)y * width + (int)(x - slotOrigin);
                currentColor[i] = index;
                currentMask[i] = transparent && index == transparentIndex ? (byte)1 : (byte)0;
            }
            else
            {
                PlaceOutside(x, y, index);
            }
        }

        x++;
        if (x < x1)
        {
            return;
        }

        x = x0;
        if (!interlaced)
        {
            y++;
            return;
        }

        switch (pass)
        {
            case 0:
                y += 8;
                if (y >= y1)
                {
                    pass = 1;
                    y = y0 + 4;
                }

                break;
            case 1:
                y += 8;
                if (y >= y1)
                {
                    pass = 2;
                    y = y0 + 2;
                }

                break;
            case 2:
                y += 4;
                if (y >= y1)
                {
                    pass = 3;
                    y = y0 + 1;
                }

                break;
            default:
                y += 2;
                break;
        }
    }

    /// <summary>
    /// Stores a pixel that lies outside the strip where the original's flat DIB offset <c>stride * y + x</c> points,
    /// separately for the colour plane (stride rounded to 4 bytes) and the mask plane (1 bit per pixel, stride rounded to
    /// 4 bytes), or nowhere when that is row padding or past the bitmap (an image larger than the logical screen).
    /// </summary>
    private void PlaceOutside(long px, long py, byte index)
    {
        byte bit = transparent && index == transparentIndex ? (byte)1 : (byte)0;
        long colorStride = (stripWidth + 3) & ~3L;
        long colorOffset = colorStride * py + px;
        long colorRow = colorOffset / colorStride;
        long colorColumn = colorOffset % colorStride;
        if (colorRow < height && colorColumn < stripWidth)
        {
            Store(colorSlots, colorColumn, colorRow, index);
        }

        long maskStride = (((stripWidth + 7) >> 3) + 3) & ~3L;
        long maskOffset = maskStride * py + (px >> 3);
        long maskRow = maskOffset / maskStride;
        long maskColumn = maskOffset % maskStride * 8 + (px & 7);
        if (maskRow < height && maskColumn < stripWidth)
        {
            Store(maskSlots, maskColumn, maskRow, bit);
        }
    }

    private void Store(List<byte[]> slots, long px, long py, byte value)
    {
        int slot = (int)(px / width);
        slots[slot][(int)py * width + (int)(px - (long)slot * width)] = value;
    }

    private void AddWork(long amount)
    {
        work += amount;
        if (work > MaxWork)
        {
            throw new InvalidDataException("The GIF is too complex to decode.");
        }
    }

    private void ReadColorTable(int count)
    {
        for (int i = 0; i < count; i++)
        {
            palette[i] = (uint)(At(p) << 16 | At(p + 1) << 8 | At(p + 2));
            p += 3;
        }
    }

    private readonly int At(int index) => (uint)index < (uint)gif.Length ? gif[index] : 0;

    /// <summary>
    /// The code reader: LSB-first codes, 2 or 3 bytes per read; it throws once the byte index passes
    /// the number of bytes that were left in the GIF, and bytes past the collected data read as 0.
    /// </summary>
    private struct LzwBitReader(byte[] data, int remaining)
    {
        private long bitPosition;

        public int Read(int codeSize, int codeMask)
        {
            long byteIndex = bitPosition >> 3;
            if (remaining < byteIndex)
            {
                throw new InvalidDataException("The GIF image data is truncated.");
            }

            int value = ByteAt(byteIndex) | ByteAt(byteIndex + 1) << 8;
            if (codeSize > 7)
            {
                value |= ByteAt(byteIndex + 2) << 16;
            }

            int shift = (int)(bitPosition & 7);
            bitPosition += codeSize;
            return (value >> shift) & codeMask;
        }

        private readonly int ByteAt(long index) => index < data.Length ? data[index] : 0;
    }
}
