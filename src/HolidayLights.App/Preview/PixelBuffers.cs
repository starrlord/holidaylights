using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Core.Sprites;

namespace HolidayLights.App.Preview;

/// <summary>Pixel helpers of the previews: backdrop fills, opaque blits, dimming, and the hand-over to WPF bitmaps.</summary>
public static class PixelBuffers
{
    /// <summary>Copies opaque pixels into a target at a position (clipped to the target and to <paramref name="clip"/>).</summary>
    /// <param name="target">The target.</param>
    /// <param name="source">Packed premultiplied pixels, row-major.</param>
    /// <param name="sourceWidth">Width of <paramref name="source"/>.</param>
    /// <param name="sourceHeight">Height of <paramref name="source"/>.</param>
    /// <param name="x">Target x of the source's left edge.</param>
    /// <param name="y">Target y of the source's top edge.</param>
    /// <param name="clip">The part of the target that may change.</param>
    public static void Blit(PremultipliedImage target, uint[] source, int sourceWidth, int sourceHeight, int x, int y, RectI clip)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        RectI area = new RectI(x, y, x + sourceWidth, y + sourceHeight)
            .Intersect(clip)
            .Intersect(new RectI(0, 0, target.Width, target.Height));
        if (area.IsEmpty)
        {
            return;
        }

        for (int row = area.Top; row < area.Bottom; row++)
        {
            source.AsSpan((row - y) * sourceWidth + (area.Left - x), area.Width)
                .CopyTo(target.Pixels.AsSpan(row * target.Width + area.Left, area.Width));
        }
    }

    /// <summary>Fills an area with the night gradient (#0B1530 at the top to #1D3466 at the bottom; PRODUCT-SPEC 4.2).</summary>
    /// <param name="target">The target.</param>
    /// <param name="area">The area (one display of a stage).</param>
    public static void FillNightGradient(PremultipliedImage target, RectI area)
    {
        ArgumentNullException.ThrowIfNull(target);
        RectI visible = area.Intersect(new RectI(0, 0, target.Width, target.Height));
        for (int row = visible.Top; row < visible.Bottom; row++)
        {
            target.Pixels.AsSpan(row * target.Width + visible.Left, visible.Width).Fill(PreviewPalette.NightGradientAt(row - area.Top, area.Height));
        }
    }

    /// <summary>Fills an area with an opaque colour.</summary>
    /// <param name="target">The target.</param>
    /// <param name="area">The area.</param>
    /// <param name="color">The colour.</param>
    public static void Fill(PremultipliedImage target, RectI area, RgbColor color)
    {
        ArgumentNullException.ThrowIfNull(target);
        RectI visible = area.Intersect(new RectI(0, 0, target.Width, target.Height));
        uint pixel = color.ToBgra32();
        for (int row = visible.Top; row < visible.Bottom; row++)
        {
            target.Pixels.AsSpan(row * target.Width + visible.Left, visible.Width).Fill(pixel);
        }
    }

    /// <summary>Scales the colour and alpha of an area (the 50 % dimming of a stage whose lights are off).</summary>
    /// <param name="target">The target.</param>
    /// <param name="area">The area.</param>
    /// <param name="factor">0-1.</param>
    public static void Dim(PremultipliedImage target, RectI area, float factor)
    {
        ArgumentNullException.ThrowIfNull(target);
        RectI visible = area.Intersect(new RectI(0, 0, target.Width, target.Height));
        uint f = (uint)Math.Round(Math.Clamp(factor, 0, 1) * 256);
        for (int row = visible.Top; row < visible.Bottom; row++)
        {
            Span<uint> line = target.Pixels.AsSpan(row * target.Width + visible.Left, visible.Width);
            for (int i = 0; i < line.Length; i++)
            {
                uint p = line[i];
                uint a = (p >> 24) & 0xFF;
                uint r = (p >> 16) & 0xFF;
                uint g = (p >> 8) & 0xFF;
                uint b = p & 0xFF;
                line[i] = (a << 24) | ((r * f >> 8) << 16) | ((g * f >> 8) << 8) | (b * f >> 8);
            }
        }
    }

    /// <summary>Copies a buffer into a WriteableBitmap of the same size, creating or resizing the bitmap when needed.</summary>
    /// <param name="bitmap">The bitmap, or null.</param>
    /// <param name="image">The buffer.</param>
    /// <param name="dpi">The DPI the bitmap reports (96 x pixels per DIP).</param>
    /// <returns>The bitmap that now holds the pixels.</returns>
    public static WriteableBitmap Present(WriteableBitmap? bitmap, PremultipliedImage image, DpiScale dpi)
    {
        ArgumentNullException.ThrowIfNull(image);
        int width = Math.Max(1, image.Width);
        int height = Math.Max(1, image.Height);
        if (bitmap is null || bitmap.PixelWidth != width || bitmap.PixelHeight != height
            || Math.Abs(bitmap.DpiX - dpi.PixelsPerInchX) > 0.01 || Math.Abs(bitmap.DpiY - dpi.PixelsPerInchY) > 0.01)
        {
            bitmap = new WriteableBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
        }

        if (image.Width > 0 && image.Height > 0)
        {
            bitmap.WritePixels(new Int32Rect(0, 0, image.Width, image.Height), image.Pixels, image.Width * 4, 0);
        }

        return bitmap;
    }

    /// <summary>
    /// Copies parts of a buffer into a WriteableBitmap of the same size (the bands of a stage where bulbs changed), so
    /// only those parts are copied and sent to the render thread.
    /// </summary>
    /// <param name="bitmap">The bitmap, the size of <paramref name="image"/>.</param>
    /// <param name="image">The buffer.</param>
    /// <param name="areas">The parts that changed, in pixels.</param>
    public static void PresentAreas(WriteableBitmap bitmap, PremultipliedImage image, IReadOnlyList<RectI> areas)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(areas);
        var all = new RectI(0, 0, Math.Min(bitmap.PixelWidth, image.Width), Math.Min(bitmap.PixelHeight, image.Height));
        foreach (RectI area in areas)
        {
            RectI part = area.Intersect(all);
            if (!part.IsEmpty)
            {
                bitmap.WritePixels(new Int32Rect(part.Left, part.Top, part.Width, part.Height), image.Pixels, image.Width * 4, part.Left, part.Top);
            }
        }
    }

    /// <summary>Creates a frozen bitmap from a buffer (cached sprites of thumbnails).</summary>
    /// <param name="image">The buffer.</param>
    /// <param name="dpi">The DPI the bitmap reports.</param>
    /// <returns>The frozen bitmap.</returns>
    public static BitmapSource ToFrozenBitmap(PremultipliedImage image, DpiScale dpi)
    {
        ArgumentNullException.ThrowIfNull(image);
        BitmapSource bitmap = BitmapSource.Create(
            Math.Max(1, image.Width), Math.Max(1, image.Height), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null,
            image.Width > 0 && image.Height > 0 ? image.Pixels : new uint[1], Math.Max(1, image.Width) * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
