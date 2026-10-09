using HolidayLights.Rendering.Interop;

namespace HolidayLights.Rendering.Overlays;

/// <summary>A GDI font: face, em height in pixels and weight (400 regular, 600 semibold).</summary>
internal readonly record struct FontSpec(string Face, int PixelHeight, int Weight);

/// <summary>A line of text drawn at a position (pixels, top-left of the line box).</summary>
internal readonly record struct TextRun(string Text, FontSpec Font, int X, int Y);

/// <summary>
/// Grayscale-antialiased text through GDI into a 32-bpp DIB, returned as coverage (0-255) per pixel. Used for the pill and
/// Identify, which are drawn once per use on the Lights thread (Rendering does not reference WPF or DirectWrite).
/// </summary>
internal static unsafe class GdiText
{
    private const uint DefaultCharset = 1;
    private const uint OutTrueTypePrecision = 4;
    private const uint AntialiasedQuality = 4;
    private const int Transparent = 1;
    private const uint SingleLineNoPrefixNoClip = 0x0020 | 0x0800 | 0x0100;
    private const uint MarkNonexistingGlyphs = 0x0001;

    /// <summary>Measures a line of text.</summary>
    public static SizeI Measure(string text, FontSpec font)
    {
        nint dc = Gdi32.CreateCompatibleDC(0);
        nint gdiFont = CreateFont(font);
        nint previous = Gdi32.SelectObject(dc, gdiFont);
        try
        {
            fixed (char* chars = text)
            {
                return Gdi32.GetTextExtentPoint32W(dc, chars, text.Length, out SIZE size) ? new SizeI(size.Cx, size.Cy) : default;
            }
        }
        finally
        {
            Gdi32.SelectObject(dc, previous);
            Gdi32.DeleteObject(gdiFont);
            Gdi32.DeleteDC(dc);
        }
    }

    /// <summary>True when the font has a real glyph for every character (used to pick an icon font).</summary>
    public static bool HasGlyphs(string text, FontSpec font)
    {
        nint dc = Gdi32.CreateCompatibleDC(0);
        nint gdiFont = CreateFont(font);
        nint previous = Gdi32.SelectObject(dc, gdiFont);
        try
        {
            ushort* indices = stackalloc ushort[text.Length];
            fixed (char* chars = text)
            {
                if (Gdi32.GetGlyphIndicesW(dc, chars, text.Length, indices, MarkNonexistingGlyphs) == uint.MaxValue)
                {
                    return false;
                }
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (indices[i] == 0xFFFF)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            Gdi32.SelectObject(dc, previous);
            Gdi32.DeleteObject(gdiFont);
            Gdi32.DeleteDC(dc);
        }
    }

    /// <summary>Draws white text runs on black and returns the coverage of every pixel (row-major).</summary>
    public static byte[] RenderCoverage(int width, int height, IEnumerable<TextRun> runs)
    {
        var coverage = new byte[width * height];
        if (width <= 0 || height <= 0)
        {
            return coverage;
        }

        var header = new BITMAPINFOHEADER
        {
            Size = (uint)sizeof(BITMAPINFOHEADER),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
        void* bits;
        nint dc = Gdi32.CreateCompatibleDC(0);
        nint bitmap = Gdi32.CreateDIBSection(dc, &header, 0, &bits, 0, 0);
        if (bitmap == 0 || bits is null)
        {
            Gdi32.DeleteDC(dc);
            return coverage;
        }

        nint previousBitmap = Gdi32.SelectObject(dc, bitmap);
        try
        {
            new Span<uint>(bits, width * height).Clear();
            Gdi32.SetBkMode(dc, Transparent);
            Gdi32.SetTextColor(dc, 0x00FFFFFF);
            foreach (TextRun run in runs)
            {
                nint gdiFont = CreateFont(run.Font);
                nint previousFont = Gdi32.SelectObject(dc, gdiFont);
                var rect = new RECT(run.X, run.Y, width, height);
                fixed (char* chars = run.Text)
                {
                    User32.DrawTextW(dc, chars, run.Text.Length, &rect, SingleLineNoPrefixNoClip);
                }

                Gdi32.SelectObject(dc, previousFont);
                Gdi32.DeleteObject(gdiFont);
            }

            Gdi32.GdiFlush();
            var pixels = new ReadOnlySpan<uint>(bits, width * height);
            for (int i = 0; i < pixels.Length; i++)
            {
                coverage[i] = (byte)(pixels[i] >> 8);
            }
        }
        finally
        {
            Gdi32.SelectObject(dc, previousBitmap);
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(dc);
        }

        return coverage;
    }

    private static nint CreateFont(FontSpec font) =>
        Gdi32.CreateFontW(-font.PixelHeight, 0, 0, 0, font.Weight, 0, 0, 0, DefaultCharset, OutTrueTypePrecision, 0, AntialiasedQuality, 0, font.Face);
}
