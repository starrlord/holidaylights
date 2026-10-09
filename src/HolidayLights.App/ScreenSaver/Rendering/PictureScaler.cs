using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Core.Sprites;

namespace HolidayLights.App.ScreenSaver.Rendering;

/// <summary>
/// Scales the background picture to its size on screen (PRODUCT-SPEC 6.2.2): the bundled pixel-art pictures with the Look's
/// pixel-art scaler (MMPX for Smooth), photos with WIC's high-quality Fant resampling.
/// </summary>
/// <remarks>Thread-safe (frozen WIC bitmaps only).</remarks>
internal static class PictureScaler
{
    /// <summary>Picture pixels kept around a region before scaling it, so the filters see the same neighbours there.</summary>
    private const int SourceMargin = 2;

    /// <summary>
    /// The pixels of one region of a picture scaled to <paramref name="size"/>, scaling only the picture pixels under that
    /// region (plus a small margin for the filters): a panorama or a 200-megapixel photo costs no more than the part of it on
    /// screen, and the result is never larger than the region.
    /// </summary>
    /// <param name="picture">The picture (straight alpha).</param>
    /// <param name="size">The size of the whole picture on screen in pixels.</param>
    /// <param name="region">The part wanted, in pixels of the scaled picture (clipped to it).</param>
    /// <param name="pixelArt">True for the bundled pictures.</param>
    /// <param name="style">The Look's pixel style (pixel art only).</param>
    /// <returns>The premultiplied part, exactly the size of the clipped region.</returns>
    public static PremultipliedImage ScaleRegion(Rgba32Image picture, SizeI size, RectI region, bool pixelArt, SpriteStyle style)
    {
        ArgumentNullException.ThrowIfNull(picture);
        var whole = new RectI(0, 0, Math.Max(0, size.Width), Math.Max(0, size.Height));
        region = region.Intersect(whole);
        if (region.IsEmpty || picture.Size.IsEmpty)
        {
            return new PremultipliedImage(region.Width, region.Height);
        }

        if (region == whole)
        {
            return Scale(picture, size, pixelArt, style);
        }

        double scaleX = (double)size.Width / picture.Width;
        double scaleY = (double)size.Height / picture.Height;
        var source = new RectI(
            Math.Max(0, (int)Math.Floor(region.Left / scaleX) - SourceMargin),
            Math.Max(0, (int)Math.Floor(region.Top / scaleY) - SourceMargin),
            Math.Min(picture.Width, (int)Math.Ceiling(region.Right / scaleX) + SourceMargin),
            Math.Min(picture.Height, (int)Math.Ceiling(region.Bottom / scaleY) + SourceMargin));
        var placed = new RectI(
            (int)Math.Round(source.Left * scaleX, MidpointRounding.AwayFromZero),
            (int)Math.Round(source.Top * scaleY, MidpointRounding.AwayFromZero),
            (int)Math.Round(source.Right * scaleX, MidpointRounding.AwayFromZero),
            (int)Math.Round(source.Bottom * scaleY, MidpointRounding.AwayFromZero));
        PremultipliedImage part = Scale(picture.Crop(source), placed.Size, pixelArt, style);
        return CopyClamped(part, region.Offset(-placed.Left, -placed.Top));
    }

    /// <summary>Scales a picture to an exact size in pixels.</summary>
    /// <param name="picture">The picture (straight alpha).</param>
    /// <param name="size">The size on screen in pixels.</param>
    /// <param name="pixelArt">True for the bundled pictures.</param>
    /// <param name="style">The Look's pixel style (pixel art only).</param>
    /// <returns>The premultiplied picture, exactly <paramref name="size"/>.</returns>
    public static PremultipliedImage Scale(Rgba32Image picture, SizeI size, bool pixelArt, SpriteStyle style)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (size.IsEmpty || picture.Size.IsEmpty)
        {
            return new PremultipliedImage(Math.Max(0, size.Width), Math.Max(0, size.Height));
        }

        if (size == picture.Size)
        {
            return picture.ToPremultiplied();
        }

        if (!pixelArt)
        {
            return Resample(picture.Pixels, picture.Size, PixelFormats.Bgra32, size);
        }

        // The scaler rounds each side on its own, so the art covers the rectangle and may pass it by a pixel: cropping that
        // pixel keeps the pixels crisp; only a stretched picture needs resampling.
        double factor = Math.Max((double)size.Width / picture.Width, (double)size.Height / picture.Height);
        PremultipliedImage art = PixelArtScaler.Scale(picture, factor, style);
        if (art.Size == size)
        {
            return art;
        }

        return art.Width - size.Width is 0 or 1 && art.Height - size.Height is 0 or 1
            ? art.Crop(new RectI(0, 0, size.Width, size.Height))
            : Resample(art.Pixels, art.Size, PixelFormats.Pbgra32, size);
    }

    /// <summary>WIC Fant resampling (WPF <see cref="TransformedBitmap"/>) to exactly <paramref name="size"/>.</summary>
    private static PremultipliedImage Resample(uint[] pixels, SizeI from, PixelFormat format, SizeI size)
    {
        BitmapSource source = BitmapSource.Create(from.Width, from.Height, 96, 96, format, null, pixels, from.Width * 4);
        var scaled = new TransformedBitmap(source, new ScaleTransform((double)size.Width / from.Width, (double)size.Height / from.Height));
        var premultiplied = new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);
        int width = premultiplied.PixelWidth;
        int height = premultiplied.PixelHeight;
        var result = new uint[width * height];
        premultiplied.CopyPixels(result, width * 4, 0);
        return width == size.Width && height == size.Height ? new PremultipliedImage(width, height, result) : Fit(result, width, height, size);
    }

    /// <summary>Copies into exactly <paramref name="size"/> when WIC rounded a dimension the other way (one pixel at most).</summary>
    private static PremultipliedImage Fit(uint[] pixels, int width, int height, SizeI size)
    {
        var image = new PremultipliedImage(size.Width, size.Height);
        for (int y = 0; y < size.Height; y++)
        {
            int sourceY = Math.Min(y, height - 1);
            for (int x = 0; x < size.Width; x++)
            {
                image.Pixels[y * size.Width + x] = pixels[sourceY * width + Math.Min(x, width - 1)];
            }
        }

        return image;
    }

    /// <summary>Copies an area of an image; rows and columns past its edges repeat the nearest edge pixel (rounding slack).</summary>
    private static PremultipliedImage CopyClamped(PremultipliedImage image, RectI area)
    {
        var copy = new PremultipliedImage(area.Width, area.Height);
        if (image.Width == 0 || image.Height == 0)
        {
            return copy;
        }

        for (int y = 0; y < area.Height; y++)
        {
            int sourceY = Math.Clamp(area.Top + y, 0, image.Height - 1);
            for (int x = 0; x < area.Width; x++)
            {
                copy.Pixels[y * area.Width + x] = image.Pixels[sourceY * image.Width + Math.Clamp(area.Left + x, 0, image.Width - 1)];
            }
        }

        return copy;
    }
}
