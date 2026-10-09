namespace HolidayLights.Core.Imaging;

/// <summary>
/// A GIF89a decoder with the compositing every current viewer uses: frames are drawn over the previous canvas (a
/// transparent index lets it show through), local colour tables and graphic control blocks apply to their own image only,
/// disposal 2 clears the frame's rectangle to transparent, disposal 3 restores the canvas from before the frame, frames are
/// clipped to the logical screen and delays are kept.
/// </summary>
/// <remarks>
/// <para>As in web browsers the logical screen starts transparent and "restore to background" means transparent (the
/// background colour index is not painted). A logical screen of size 0 takes the size of the first image.</para>
/// <para>Damaged files are read as far as possible: an image whose data ends early keeps the pixels decoded so far, and
/// an unknown block ends the animation. Only a file without any decodable image is rejected.</para>
/// </remarks>
internal ref struct StandardGifReader
{
    private const int MaxCanvasPixels = 16 * 1024 * 1024;
    private const long MaxFramePixels = 32L * 1024 * 1024;
    private const long MaxWork = 256L * 1024 * 1024;
    private const int TableSize = 4096;

    private readonly ReadOnlySpan<byte> gif;
    private readonly List<Rgba32Image> frames = [];
    private readonly List<int> delays = [];
    private readonly ushort[] prefix = new ushort[TableSize];
    private readonly byte[] suffix = new byte[TableSize];
    private readonly byte[] stringStack = new byte[TableSize + 1];

    private int p;
    private int screenWidth;
    private int screenHeight;
    private uint[]? globalTable;
    private uint[]? canvas;
    private long work;

    // Graphic control of the next image.
    private int controlDisposal;
    private bool controlTransparent;
    private int controlTransparentIndex;
    private int controlDelay;

    // Disposal of the previous image.
    private int previousDisposal;
    private RectI previousRect;
    private uint[]? restoreCanvas;

    private StandardGifReader(ReadOnlySpan<byte> gif) => this.gif = gif;

    /// <summary>Decodes a GIF.</summary>
    /// <param name="gif">The GIF file bytes.</param>
    /// <returns>Every frame at the logical screen size and its delay in milliseconds (0 when the file gives none).</returns>
    /// <exception cref="InvalidDataException">The data is not a GIF or holds no decodable image.</exception>
    public static GifAnimation Decode(ReadOnlySpan<byte> gif)
    {
        var reader = new StandardGifReader(gif);
        return reader.Run();
    }

    private GifAnimation Run()
    {
        ReadHeader();
        while (p < gif.Length)
        {
            int block = gif[p++];
            if (block == 0x21)
            {
                ReadExtension();
            }
            else if (block != 0x2C || !ReadImage())
            {
                break;
            }
        }

        if (frames.Count == 0)
        {
            throw new InvalidDataException("The GIF contains no image.");
        }

        return new GifAnimation(new SizeI(screenWidth, screenHeight), frames, delays);
    }

    private void ReadHeader()
    {
        if (gif.Length < 13 || !GifDecoder.HasSignature(gif))
        {
            throw new InvalidDataException("The data is not a GIF.");
        }

        screenWidth = gif[6] | gif[7] << 8;
        screenHeight = gif[8] | gif[9] << 8;
        int packed = gif[10];
        p = 13;
        if ((packed & 0x80) != 0)
        {
            globalTable = ReadColorTable(1 << ((packed & 7) + 1));
        }
    }

    private void ReadExtension()
    {
        if (p >= gif.Length)
        {
            return;
        }

        int label = gif[p++];
        if (label == 0xF9 && p + 4 < gif.Length && gif[p] >= 4)
        {
            int packed = gif[p + 1];
            controlDisposal = (packed >> 2) & 7;
            controlTransparent = (packed & 1) != 0;
            controlDelay = (gif[p + 2] | gif[p + 3] << 8) * 10;
            controlTransparentIndex = gif[p + 4];
        }

        SkipSubBlocks();
    }

    private void SkipSubBlocks()
    {
        while (p < gif.Length)
        {
            int length = gif[p++];
            if (length == 0)
            {
                return;
            }

            p += length;
        }
    }

    /// <summary>Reads, decodes and composites one image. Returns false when the file ends inside its descriptor.</summary>
    private bool ReadImage()
    {
        if (p + 9 > gif.Length)
        {
            return false;
        }

        int left = gif[p] | gif[p + 1] << 8;
        int top = gif[p + 2] | gif[p + 3] << 8;
        int width = gif[p + 4] | gif[p + 5] << 8;
        int height = gif[p + 6] | gif[p + 7] << 8;
        int packed = gif[p + 8];
        p += 9;
        uint[]? table = (packed & 0x80) != 0 ? ReadColorTable(1 << ((packed & 7) + 1)) : globalTable;
        if (p >= gif.Length)
        {
            return false;
        }

        int minimumCodeSize = gif[p++];
        byte[] data = CollectSubBlocks();
        uint[] target = EnsureCanvas(left + width, top + height);
        if ((long)(frames.Count + 1) * target.Length > MaxFramePixels)
        {
            return false;
        }

        ApplyPreviousDisposal(target);
        uint[]? saved = controlDisposal == 3 ? (uint[])target.Clone() : null;

        var image = new FrameTarget(left, top, width, height, (packed & 0x40) != 0, table, controlTransparent ? controlTransparentIndex : -1);
        if (minimumCodeSize is >= 1 and <= 8)
        {
            DecodeLzw(data, minimumCodeSize, image, target);
        }

        frames.Add(new Rgba32Image(screenWidth, screenHeight, (uint[])target.Clone()));
        delays.Add(controlDelay);
        previousDisposal = controlDisposal;
        previousRect = new RectI(left, top, left + width, top + height).Intersect(new RectI(0, 0, screenWidth, screenHeight));
        restoreCanvas = saved;
        controlDisposal = 0;
        controlTransparent = false;
        controlDelay = 0;
        return true;
    }

    private uint[] EnsureCanvas(int firstRight, int firstBottom)
    {
        if (canvas is not null)
        {
            return canvas;
        }

        if (screenWidth == 0 || screenHeight == 0)
        {
            screenWidth = firstRight;
            screenHeight = firstBottom;
        }

        long pixels = (long)screenWidth * screenHeight;
        if (pixels == 0 || pixels > MaxCanvasPixels)
        {
            throw new InvalidDataException($"Unsupported GIF size {screenWidth} x {screenHeight}.");
        }

        canvas = new uint[pixels];
        return canvas;
    }

    private readonly void ApplyPreviousDisposal(uint[] target)
    {
        if (previousDisposal == 2)
        {
            for (int y = previousRect.Top; y < previousRect.Bottom; y++)
            {
                target.AsSpan(y * screenWidth + previousRect.Left, previousRect.Width).Clear();
            }
        }
        else if (previousDisposal == 3 && restoreCanvas is not null)
        {
            restoreCanvas.CopyTo(target, 0);
        }
    }

    private byte[] CollectSubBlocks()
    {
        int start = p;
        int total = 0;
        while (p < gif.Length)
        {
            int length = gif[p++];
            if (length == 0)
            {
                break;
            }

            int available = Math.Min(length, gif.Length - p);
            total += available;
            p += length;
        }

        var data = new byte[total];
        int written = 0;
        for (int q = start; q < gif.Length && written < total;)
        {
            int length = gif[q++];
            int available = Math.Min(length, gif.Length - q);
            gif.Slice(q, available).CopyTo(data.AsSpan(written));
            written += available;
            q += length;
        }

        return data;
    }

    /// <summary>Standard GIF LZW: the table stops growing at 4,096 codes; an invalid code or the end of the data ends the image.</summary>
    private void DecodeLzw(byte[] data, int minimumCodeSize, FrameTarget image, uint[] target)
    {
        int clear = 1 << minimumCodeSize;
        int endOfInformation = clear + 1;
        int codeSize = minimumCodeSize + 1;
        int nextCode = clear + 2;
        int previous = -1;
        int first = 0;
        long bitPosition = 0;
        long totalBits = (long)data.Length * 8;
        while (bitPosition + codeSize <= totalBits && !image.IsComplete)
        {
            int code = ReadCode(data, bitPosition, codeSize);
            bitPosition += codeSize;
            if (code == clear)
            {
                codeSize = minimumCodeSize + 1;
                nextCode = clear + 2;
                previous = -1;
                continue;
            }

            if (code == endOfInformation)
            {
                return;
            }

            if (previous < 0)
            {
                if (code >= clear)
                {
                    return;
                }

                first = code;
                previous = code;
                Emit(image, target, (byte)code);
                continue;
            }

            int length = 0;
            int c;
            if (code < nextCode)
            {
                c = code;
            }
            else if (code == nextCode)
            {
                stringStack[length++] = (byte)first;
                c = previous;
            }
            else
            {
                return;
            }

            while (c > endOfInformation)
            {
                stringStack[length++] = suffix[c];
                c = prefix[c];
            }

            stringStack[length++] = (byte)c;
            first = c;
            for (int i = length - 1; i >= 0 && !image.IsComplete; i--)
            {
                Emit(image, target, stringStack[i]);
            }

            if (nextCode < TableSize)
            {
                prefix[nextCode] = (ushort)previous;
                suffix[nextCode] = (byte)first;
                nextCode++;
                if (nextCode == 1 << codeSize && codeSize < 12)
                {
                    codeSize++;
                }
            }

            previous = code;
        }
    }

    private void Emit(FrameTarget image, uint[] target, byte index)
    {
        if (++work > MaxWork)
        {
            throw new InvalidDataException("The GIF is too complex to decode.");
        }

        image.Draw(target, screenWidth, screenHeight, index);
    }

    private static int ReadCode(byte[] data, long bitPosition, int codeSize)
    {
        long byteIndex = bitPosition >> 3;
        int value = data[byteIndex];
        if (byteIndex + 1 < data.Length)
        {
            value |= data[byteIndex + 1] << 8;
        }

        if (byteIndex + 2 < data.Length)
        {
            value |= data[byteIndex + 2] << 16;
        }

        return (value >> (int)(bitPosition & 7)) & ((1 << codeSize) - 1);
    }

    private uint[] ReadColorTable(int count)
    {
        var table = new uint[256];
        for (int i = 0; i < count; i++)
        {
            if (p + 3 <= gif.Length)
            {
                table[i] = 0xFF000000 | (uint)(gif[p] << 16 | gif[p + 1] << 8 | gif[p + 2]);
            }
            else
            {
                table[i] = 0xFF000000;
            }

            p += 3;
        }

        for (int i = count; i < table.Length; i++)
        {
            table[i] = 0xFF000000;
        }

        return table;
    }

    /// <summary>Where the pixels of one image go: its rectangle, row order, colours and transparent index.</summary>
    private sealed class FrameTarget(int left, int top, int width, int height, bool interlaced, uint[]? table, int transparentIndex)
    {
        private readonly long total = (long)width * height;
        private long written;

        public bool IsComplete => written >= total;

        public void Draw(uint[] canvas, int canvasWidth, int canvasHeight, byte index)
        {
            long row = written / width;
            int column = (int)(written - row * width);
            written++;
            if (index == transparentIndex)
            {
                return;
            }

            int y = top + (interlaced ? InterlacedRow((int)row) : (int)row);
            int x = left + column;
            if (x < canvasWidth && y < canvasHeight)
            {
                canvas[y * canvasWidth + x] = table is null ? 0xFF000000 : table[index];
            }
        }

        private int InterlacedRow(int row)
        {
            int pass1 = (height + 7) / 8;
            if (row < pass1)
            {
                return row * 8;
            }

            row -= pass1;
            int pass2 = (height + 3) / 8;
            if (row < pass2)
            {
                return 4 + row * 8;
            }

            row -= pass2;
            int pass3 = (height + 1) / 4;
            return row < pass3 ? 2 + row * 4 : 1 + (row - pass3) * 2;
        }
    }
}
