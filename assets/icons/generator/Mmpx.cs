namespace HolidayLights.Branding;

/// <summary>
/// MMPX 2x pixel-art magnification (Morgan McGuire and Mara Gagiu, "MMPX Style-Preserving Pixel-Art Magnification",
/// JCGT 10(2), 2021; MIT licence). Ported from the verified C# port of the upscaling prototype, which matches the
/// paper's reference outputs pixel for pixel. Used to enlarge the 2003 banner art; samples outside the image clamp to
/// the edge (the GLSL reference).
/// </summary>
internal static class Mmpx
{
    /// <summary>Enlarges an image 2x.</summary>
    /// <param name="image">Straight-alpha source.</param>
    /// <returns>The enlarged image.</returns>
    public static PixelImage Scale2x(PixelImage image)
    {
        int w = image.Width;
        int h = image.Height;
        uint[] s = image.Pixels;
        var d = new uint[4 * w * h];
        int dw = 2 * w;
        uint Src(int x, int y) => s[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                uint a = Src(x - 1, y - 1), b = Src(x, y - 1), c = Src(x + 1, y - 1);
                uint dd = Src(x - 1, y), e = Src(x, y), f = Src(x + 1, y);
                uint g = Src(x - 1, y + 1), hh = Src(x, y + 1), i = Src(x + 1, y + 1);
                uint j = e, k = e, l = e, m = e;
                if (((a ^ e) | (b ^ e) | (c ^ e) | (dd ^ e) | (f ^ e) | (g ^ e) | (hh ^ e) | (i ^ e)) != 0)
                {
                    uint p = Src(x, y - 2), ss = Src(x, y + 2), q = Src(x - 2, y), r = Src(x + 2, y);
                    uint bl = Luma(b), dl = Luma(dd), el = Luma(e), fl = Luma(f), hl = Luma(hh);

                    // 1:1 slope rules.
                    if (dd == b && dd != hh && dd != f && (el >= dl || e == a) && AnyEq3(e, a, c, g) && (el < dl || a != dd || e != p || e != q))
                    {
                        j = dd;
                    }

                    if (b == f && b != dd && b != hh && (el >= bl || e == c) && AnyEq3(e, a, c, i) && (el < bl || c != b || e != p || e != r))
                    {
                        k = b;
                    }

                    if (hh == dd && hh != f && hh != b && (el >= hl || e == g) && AnyEq3(e, a, g, i) && (el < hl || g != hh || e != ss || e != q))
                    {
                        l = hh;
                    }

                    if (f == hh && f != b && f != dd && (el >= fl || e == i) && AnyEq3(e, c, g, i) && (el < fl || i != hh || e != r || e != ss))
                    {
                        m = f;
                    }

                    // Intersection rules.
                    if (e != f && AllEq4(e, c, i, dd, q) && AllEq2(f, b, hh) && f != Src(x + 3, y))
                    {
                        k = m = f;
                    }

                    if (e != dd && AllEq4(e, a, g, f, r) && AllEq2(dd, b, hh) && dd != Src(x - 3, y))
                    {
                        j = l = dd;
                    }

                    if (e != hh && AllEq4(e, g, i, b, p) && AllEq2(hh, dd, f) && hh != Src(x, y + 3))
                    {
                        l = m = hh;
                    }

                    if (e != b && AllEq4(e, a, c, hh, ss) && AllEq2(b, dd, f) && b != Src(x, y - 3))
                    {
                        j = k = b;
                    }

                    if (bl < el && AllEq4(e, g, hh, i, ss) && NoneEq4(e, a, dd, c, f))
                    {
                        j = k = b;
                    }

                    if (hl < el && AllEq4(e, a, b, c, p) && NoneEq4(e, dd, g, i, f))
                    {
                        l = m = hh;
                    }

                    if (fl < el && AllEq4(e, a, dd, g, q) && NoneEq4(e, b, c, i, hh))
                    {
                        k = m = f;
                    }

                    if (dl < el && AllEq4(e, c, f, i, r) && NoneEq4(e, b, a, g, hh))
                    {
                        j = l = dd;
                    }

                    // 2:1 slope rules.
                    if (hh != b)
                    {
                        if (hh != a && hh != e && hh != c)
                        {
                            if (AllEq3(hh, g, f, r) && NoneEq2(hh, dd, Src(x + 2, y - 1)))
                            {
                                l = m;
                            }

                            if (AllEq3(hh, i, dd, q) && NoneEq2(hh, f, Src(x - 2, y - 1)))
                            {
                                m = l;
                            }
                        }

                        if (b != i && b != g && b != e)
                        {
                            if (AllEq3(b, a, f, r) && NoneEq2(b, dd, Src(x + 2, y + 1)))
                            {
                                j = k;
                            }

                            if (AllEq3(b, c, dd, q) && NoneEq2(b, f, Src(x - 2, y + 1)))
                            {
                                k = j;
                            }
                        }
                    }

                    if (f != dd)
                    {
                        if (dd != i && dd != e && dd != c)
                        {
                            if (AllEq3(dd, a, hh, ss) && NoneEq2(dd, b, Src(x + 1, y + 2)))
                            {
                                j = l;
                            }

                            if (AllEq3(dd, g, b, p) && NoneEq2(dd, hh, Src(x + 1, y - 2)))
                            {
                                l = j;
                            }
                        }

                        if (f != e && f != a && f != g)
                        {
                            if (AllEq3(f, c, hh, ss) && NoneEq2(f, b, Src(x - 1, y + 2)))
                            {
                                k = m;
                            }

                            if (AllEq3(f, i, b, p) && NoneEq2(f, hh, Src(x - 1, y - 2)))
                            {
                                m = k;
                            }
                        }
                    }
                }

                int index = 2 * y * dw + 2 * x;
                d[index] = j;
                d[index + 1] = k;
                d[index + dw] = l;
                d[index + dw + 1] = m;
            }
        }

        return new PixelImage(dw, 2 * h, d);
    }

    /// <summary>Enlarges an image 4x (2x twice).</summary>
    /// <param name="image">Straight-alpha source.</param>
    /// <returns>The enlarged image.</returns>
    public static PixelImage Scale4x(PixelImage image) => Scale2x(Scale2x(image));

    private static uint Luma(uint c)
    {
        uint alpha = c >> 24;
        return (((c >> 16) & 0xFF) + ((c >> 8) & 0xFF) + (c & 0xFF) + 1) * (256 - alpha);
    }

    private static bool AllEq2(uint b, uint a0, uint a1) => ((b ^ a0) | (b ^ a1)) == 0;

    private static bool AllEq3(uint b, uint a0, uint a1, uint a2) => ((b ^ a0) | (b ^ a1) | (b ^ a2)) == 0;

    private static bool AllEq4(uint b, uint a0, uint a1, uint a2, uint a3) => ((b ^ a0) | (b ^ a1) | (b ^ a2) | (b ^ a3)) == 0;

    private static bool AnyEq3(uint b, uint a0, uint a1, uint a2) => b == a0 || b == a1 || b == a2;

    private static bool NoneEq2(uint b, uint a0, uint a1) => b != a0 && b != a1;

    private static bool NoneEq4(uint b, uint a0, uint a1, uint a2, uint a3) => b != a0 && b != a1 && b != a2 && b != a3;
}
