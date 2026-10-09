namespace HolidayLights.Core.Bulbs.Writing;

/// <summary>
/// The global colour table of a GIF written by <see cref="GifEncoder"/>: the opaque colours of every frame (exactly when
/// there are few enough, otherwise reduced by median cut) and, when some pixel is transparent, a black transparent entry
/// at index 0. Pixels with alpha below <see cref="OpaqueThreshold"/> are transparent (bulb art has 1-bit masks).
/// </summary>
internal sealed class GifPalette
{
    /// <summary>The lowest alpha drawn as opaque.</summary>
    public const byte OpaqueThreshold = 128;

    /// <summary>Most entries of a GIF colour table.</summary>
    private const int MaxEntries = 256;

    /// <summary>The smallest table written: 4 entries, so the LZW alphabet and the table always have the same size.</summary>
    private const int MinTableBits = 2;

    private readonly Dictionary<uint, byte> indexByColor;

    private GifPalette(IReadOnlyList<uint> colors, int transparentIndex, Dictionary<uint, byte> indexByColor)
    {
        Colors = colors;
        TransparentIndex = transparentIndex;
        this.indexByColor = indexByColor;
        int bits = MinTableBits;
        while (1 << bits < colors.Count)
        {
            bits++;
        }

        TableBits = bits;
    }

    /// <summary>The entries in index order as <c>0x00RRGGBB</c> (the transparent entry, when present, is index 0 and black).</summary>
    public IReadOnlyList<uint> Colors { get; }

    /// <summary>The transparent entry, or -1 when every pixel of every frame is opaque.</summary>
    public int TransparentIndex { get; }

    /// <summary>The table holds <c>2^TableBits</c> entries (2-8), padded with black.</summary>
    public int TableBits { get; }

    /// <summary>Builds the table for a set of frames.</summary>
    /// <param name="frames">Straight-alpha frames.</param>
    /// <returns>The palette.</returns>
    public static GifPalette Build(IReadOnlyList<Rgba32Image> frames)
    {
        var counts = new Dictionary<uint, int>();
        bool transparent = false;
        foreach (Rgba32Image frame in frames)
        {
            foreach (uint pixel in frame.Pixels)
            {
                if (pixel >> 24 < OpaqueThreshold)
                {
                    transparent = true;
                }
                else
                {
                    uint rgb = pixel & 0xFFFFFF;
                    counts[rgb] = counts.GetValueOrDefault(rgb) + 1;
                }
            }
        }

        int first = transparent ? 1 : 0;
        List<List<KeyValuePair<uint, int>>> groups = counts.Count <= MaxEntries - first
            ? [.. counts.OrderBy(c => c.Key).Select(c => new List<KeyValuePair<uint, int>> { c })]
            : MedianCut([.. counts], MaxEntries - first);

        var colors = new List<uint>();
        if (transparent)
        {
            colors.Add(0);
        }

        var indexByColor = new Dictionary<uint, byte>(counts.Count);
        foreach (List<KeyValuePair<uint, int>> group in groups)
        {
            byte index = (byte)colors.Count;
            colors.Add(Average(group));
            foreach (KeyValuePair<uint, int> color in group)
            {
                indexByColor[color.Key] = index;
            }
        }

        return new GifPalette(colors, transparent ? 0 : -1, indexByColor);
    }

    /// <summary>The table index of every pixel of a frame.</summary>
    /// <param name="frame">A frame of the set the palette was built from.</param>
    /// <returns>One index per pixel, row by row.</returns>
    public byte[] Map(Rgba32Image frame)
    {
        var indices = new byte[frame.Pixels.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            uint pixel = frame.Pixels[i];
            indices[i] = pixel >> 24 < OpaqueThreshold ? (byte)TransparentIndex : indexByColor[pixel & 0xFFFFFF];
        }

        return indices;
    }

    /// <summary>Splits the colours into at most <paramref name="maxGroups"/> groups: the group with the widest channel is cut at its weighted median, repeatedly.</summary>
    private static List<List<KeyValuePair<uint, int>>> MedianCut(List<KeyValuePair<uint, int>> colors, int maxGroups)
    {
        var groups = new List<ColorGroup> { ColorGroup.Of(colors) };
        while (groups.Count < maxGroups)
        {
            int widest = -1;
            for (int g = 0; g < groups.Count; g++)
            {
                if (groups[g].Range > 0 && (widest < 0 || groups[g].Range > groups[widest].Range))
                {
                    widest = g;
                }
            }

            if (widest < 0)
            {
                break;
            }

            (List<KeyValuePair<uint, int>> low, List<KeyValuePair<uint, int>> high) = Split(groups[widest].Colors, groups[widest].Shift);
            groups[widest] = ColorGroup.Of(low);
            groups.Add(ColorGroup.Of(high));
        }

        return [.. groups.Select(g => g.Colors)];
    }

    private static (List<KeyValuePair<uint, int>> Low, List<KeyValuePair<uint, int>> High) Split(List<KeyValuePair<uint, int>> group, int shift)
    {
        List<KeyValuePair<uint, int>> sorted = [.. group.OrderBy(c => (c.Key >> shift) & 0xFF).ThenBy(c => c.Key)];
        long total = sorted.Sum(c => (long)c.Value);
        long running = 0;
        int cut = 1;
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            running += sorted[i].Value;
            cut = i + 1;
            if (running * 2 >= total)
            {
                break;
            }
        }

        return (sorted[..cut], sorted[cut..]);
    }

    /// <summary>The pixel-weighted mean colour of a group.</summary>
    private static uint Average(List<KeyValuePair<uint, int>> group)
    {
        long r = 0;
        long g = 0;
        long b = 0;
        long n = 0;
        foreach ((uint rgb, int count) in group)
        {
            r += ((rgb >> 16) & 0xFF) * (long)count;
            g += ((rgb >> 8) & 0xFF) * (long)count;
            b += (rgb & 0xFF) * (long)count;
            n += count;
        }

        return (uint)((r + n / 2) / n) << 16 | (uint)((g + n / 2) / n) << 8 | (uint)((b + n / 2) / n);
    }

    /// <summary>A group of colours with its widest channel (bit shift 16 = red, 8 = green, 0 = blue) and that channel's range.</summary>
    private sealed record ColorGroup(List<KeyValuePair<uint, int>> Colors, int Shift, int Range)
    {
        public static ColorGroup Of(List<KeyValuePair<uint, int>> colors)
        {
            var best = new ColorGroup(colors, 0, -1);
            foreach (int shift in (ReadOnlySpan<int>)[16, 8, 0])
            {
                int min = 255;
                int max = 0;
                foreach (KeyValuePair<uint, int> color in colors)
                {
                    int value = (int)(color.Key >> shift) & 0xFF;
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }

                if (max - min > best.Range)
                {
                    best = best with { Shift = shift, Range = max - min };
                }
            }

            return best;
        }
    }
}
