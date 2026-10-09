namespace HolidayLights.Core.Sprites;

/// <summary>How MMPX reads pixels outside the image.</summary>
public enum MmpxEdge
{
    /// <summary>
    /// Clamp to the nearest edge pixel (the authors' GLSL). Used for bulb frames (PRODUCT-SPEC 5.3.3): cords that run off
    /// the frame edge continue into the neighbouring bulb.
    /// </summary>
    Clamp,

    /// <summary>
    /// Read as transparent (<c>0x00000000</c>), as the authors' JavaScript does for sprites. The reference images in
    /// <c>tests/HolidayLights.Tests/Sprites/Golden/upscale</c> were made this way.
    /// </summary>
    Transparent,
}

/// <summary>
/// MMPX 2x pixel-art magnification (Morgan McGuire and Mara Gagiu, "MMPX Style-Preserving Pixel Art Magnification",
/// JCGT 10(2) 2021, MIT licence): an exact port of the upscaling prototype's MMPX
/// (reference images in <c>tests/HolidayLights.Tests/Sprites/Golden/upscale</c>). It keeps the palette, transparency, single-pixel features and sharp corners and
/// reconstructs 1:1 and 2:1 slopes. 4x is 2x applied twice.
/// </summary>
/// <remarks>
/// MMPX compares colours for exact equality, so every transparent pixel must be canonical (<c>0x00000000</c>) before
/// magnifying; <see cref="PixelArtScaler"/> does that. Pure and thread-safe.
/// </remarks>
public static class Mmpx
{
    /// <summary>Pixels of padding around the source: the rules read up to three pixels away from the centre.</summary>
    private const int Pad = 3;

    /// <summary>Magnifies an image 2x.</summary>
    /// <param name="source">The image (straight alpha, transparent pixels canonical).</param>
    /// <param name="edge">How pixels outside the image are read.</param>
    /// <returns>A new image of twice the width and height.</returns>
    public static Rgba32Image Scale2x(Rgba32Image source, MmpxEdge edge = MmpxEdge.Clamp)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Rgba32Image(source.Width * 2, source.Height * 2, Scale2x(source.Pixels, source.Width, source.Height, edge));
    }

    /// <summary>Magnifies packed pixels 2x.</summary>
    /// <param name="source">Row-major pixels, <paramref name="width"/> x <paramref name="height"/>.</param>
    /// <param name="width">Source width.</param>
    /// <param name="height">Source height.</param>
    /// <param name="edge">How pixels outside the image are read.</param>
    /// <returns>New pixels, <c>2 width</c> x <c>2 height</c>.</returns>
    internal static uint[] Scale2x(uint[] source, int width, int height, MmpxEdge edge)
    {
        var result = new uint[checked(4 * width * height)];
        if (result.Length == 0)
        {
            return result;
        }

        int stride = width + 2 * Pad;
        uint[] p = PadSource(source, width, height, edge);
        int dw = 2 * width;

        for (int y = 0; y < height; y++)
        {
            int c = (y + Pad) * stride + Pad;
            int o = 2 * y * dw;
            for (int x = 0; x < width; x++, c++, o += 2)
            {
                uint a = p[c - stride - 1], b = p[c - stride], cc = p[c - stride + 1];
                uint d = p[c - 1], e = p[c], f = p[c + 1];
                uint g = p[c + stride - 1], h = p[c + stride], i = p[c + stride + 1];
                uint j = e, k = e, l = e, m = e;

                if (((a ^ e) | (b ^ e) | (cc ^ e) | (d ^ e) | (f ^ e) | (g ^ e) | (h ^ e) | (i ^ e)) != 0)
                {
                    Magnify(p, c, stride, a, b, cc, d, e, f, g, h, i, ref j, ref k, ref l, ref m);
                }

                result[o] = j;
                result[o + 1] = k;
                result[o + dw] = l;
                result[o + dw + 1] = m;
            }
        }

        return result;
    }

    /// <summary>
    /// The MMPX rules for one source pixel <c>E</c> with the neighbourhood
    /// <c>P / A B C / Q D E F R / G H I / S</c>; writes the 2x2 block <c>J K / L M</c>.
    /// </summary>
    private static void Magnify(
        uint[] p, int c, int stride,
        uint a, uint b, uint cc, uint d, uint e, uint f, uint g, uint h, uint i,
        ref uint j, ref uint k, ref uint l, ref uint m)
    {
        uint pp = p[c - 2 * stride], s = p[c + 2 * stride], q = p[c - 2], r = p[c + 2];
        uint bl = Luma(b), dl = Luma(d), el = Luma(e), fl = Luma(f), hl = Luma(h);

        // 1:1 slope rules
        if (d == b && d != h && d != f && (el >= dl || e == a) && AnyEq3(e, a, cc, g) && (el < dl || a != d || e != pp || e != q))
        {
            j = d;
        }

        if (b == f && b != d && b != h && (el >= bl || e == cc) && AnyEq3(e, a, cc, i) && (el < bl || cc != b || e != pp || e != r))
        {
            k = b;
        }

        if (h == d && h != f && h != b && (el >= hl || e == g) && AnyEq3(e, a, g, i) && (el < hl || g != h || e != s || e != q))
        {
            l = h;
        }

        if (f == h && f != b && f != d && (el >= fl || e == i) && AnyEq3(e, cc, g, i) && (el < fl || i != h || e != r || e != s))
        {
            m = f;
        }

        // Intersection rules
        if (e != f && AllEq4(e, cc, i, d, q) && AllEq2(f, b, h) && f != p[c + 3])
        {
            k = m = f;
        }

        if (e != d && AllEq4(e, a, g, f, r) && AllEq2(d, b, h) && d != p[c - 3])
        {
            j = l = d;
        }

        if (e != h && AllEq4(e, g, i, b, pp) && AllEq2(h, d, f) && h != p[c + 3 * stride])
        {
            l = m = h;
        }

        if (e != b && AllEq4(e, a, cc, h, s) && AllEq2(b, d, f) && b != p[c - 3 * stride])
        {
            j = k = b;
        }

        if (bl < el && AllEq4(e, g, h, i, s) && NoneEq4(e, a, d, cc, f))
        {
            j = k = b;
        }

        if (hl < el && AllEq4(e, a, b, cc, pp) && NoneEq4(e, d, g, i, f))
        {
            l = m = h;
        }

        if (fl < el && AllEq4(e, a, d, g, q) && NoneEq4(e, b, cc, i, h))
        {
            k = m = f;
        }

        if (dl < el && AllEq4(e, cc, f, i, r) && NoneEq4(e, b, a, g, h))
        {
            j = l = d;
        }

        // 2:1 slope rules
        if (h != b)
        {
            if (h != a && h != e && h != cc)
            {
                if (AllEq3(h, g, f, r) && NoneEq2(h, d, p[c - stride + 2]))
                {
                    l = m;
                }

                if (AllEq3(h, i, d, q) && NoneEq2(h, f, p[c - stride - 2]))
                {
                    m = l;
                }
            }

            if (b != i && b != g && b != e)
            {
                if (AllEq3(b, a, f, r) && NoneEq2(b, d, p[c + stride + 2]))
                {
                    j = k;
                }

                if (AllEq3(b, cc, d, q) && NoneEq2(b, f, p[c + stride - 2]))
                {
                    k = j;
                }
            }
        }

        if (f != d)
        {
            if (d != i && d != e && d != cc)
            {
                if (AllEq3(d, a, h, s) && NoneEq2(d, b, p[c + 2 * stride + 1]))
                {
                    j = l;
                }

                if (AllEq3(d, g, b, pp) && NoneEq2(d, h, p[c - 2 * stride + 1]))
                {
                    l = j;
                }
            }

            if (f != e && f != a && f != g)
            {
                if (AllEq3(f, cc, h, s) && NoneEq2(f, b, p[c + 2 * stride - 1]))
                {
                    k = m;
                }

                if (AllEq3(f, i, b, pp) && NoneEq2(f, h, p[c - 2 * stride - 1]))
                {
                    m = k;
                }
            }
        }
    }

    /// <summary>Copies the source into a buffer with <see cref="Pad"/> pixels of border filled per <paramref name="edge"/>.</summary>
    private static uint[] PadSource(uint[] source, int width, int height, MmpxEdge edge)
    {
        int stride = width + 2 * Pad;
        var padded = new uint[stride * (height + 2 * Pad)];
        for (int py = 0; py < height + 2 * Pad; py++)
        {
            int sy = py - Pad;
            bool insideY = sy >= 0 && sy < height;
            if (!insideY && edge == MmpxEdge.Transparent)
            {
                continue;
            }

            int row = Math.Clamp(sy, 0, height - 1) * width;
            int dst = py * stride;
            Array.Copy(source, row, padded, dst + Pad, width);
            if (edge == MmpxEdge.Clamp)
            {
                padded.AsSpan(dst, Pad).Fill(source[row]);
                padded.AsSpan(dst + Pad + width, Pad).Fill(source[row + width - 1]);
            }
        }

        return padded;
    }

    /// <summary>The alpha-aware luma of MMPX: <c>(R + G + B + 1) x (256 - A)</c>; transparent counts as brightest.</summary>
    private static uint Luma(uint c) => (((c >> 16) & 0xFF) + ((c >> 8) & 0xFF) + (c & 0xFF) + 1) * (256 - (c >> 24));

    private static bool AllEq2(uint b, uint a0, uint a1) => ((b ^ a0) | (b ^ a1)) == 0;

    private static bool AllEq3(uint b, uint a0, uint a1, uint a2) => ((b ^ a0) | (b ^ a1) | (b ^ a2)) == 0;

    private static bool AllEq4(uint b, uint a0, uint a1, uint a2, uint a3) => ((b ^ a0) | (b ^ a1) | (b ^ a2) | (b ^ a3)) == 0;

    private static bool AnyEq3(uint b, uint a0, uint a1, uint a2) => b == a0 || b == a1 || b == a2;

    private static bool NoneEq2(uint b, uint a0, uint a1) => b != a0 && b != a1;

    private static bool NoneEq4(uint b, uint a0, uint a1, uint a2, uint a3) => b != a0 && b != a1 && b != a2 && b != a3;
}
