using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.Core.Imaging;

namespace HolidayLights.App.ScreenSaver.Pictures;

/// <summary>
/// Decodes a background picture at its own size (PRODUCT-SPEC 3.5.6, 6.2.2): BMP with the 5.4 rule (the second of two
/// concatenated bitmaps, as in the 11 bundled pictures), GIF's first frame with the 5.4 decoder, and JPEG, PNG and
/// anything else WIC reads, turned upright by its EXIF orientation.
/// </summary>
/// <remarks>Thread-safe: each call uses its own WIC objects.</remarks>
internal static class PictureDecoder
{
    /// <summary>Decodes a picture file.</summary>
    /// <param name="data">The file's bytes.</param>
    /// <returns>The picture (straight alpha).</returns>
    /// <exception cref="InvalidDataException">The data is not a picture.</exception>
    public static Rgba32Image Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M')
        {
            try
            {
                return BmpDecoder.DecodePicture(data);
            }
            catch (InvalidDataException)
            {
                // A variant the 5.4 reader does not know: WIC below.
            }
        }

        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
        {
            try
            {
                GifAnimation gif = GifDecoder.DecodeClassic(data, firstFrameOnly: true);
                if (gif.Frames.Count > 0 && !gif.Size.IsEmpty)
                {
                    return gif.Frames[0];
                }
            }
            catch (InvalidDataException)
            {
                // WIC below.
            }
        }

        return DecodeWithWic(data);
    }

    private static Rgba32Image DecodeWithWic(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapFrame frame = decoder.Frames[0];
            BitmapSource upright = Orient(frame, ReadOrientation(frame));
            var converted = new FormatConvertedBitmap(upright, PixelFormats.Bgra32, null, 0);
            var image = new Rgba32Image(converted.PixelWidth, converted.PixelHeight);
            converted.CopyPixels(image.Pixels, image.Width * 4, 0);
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidDataException("The file is not a picture Windows can read.", ex);
        }
    }

    /// <summary>The EXIF orientation (1-8), or 1 when the format has none.</summary>
    private static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort orientation
                ? orientation
                : 1;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return 1;
        }
    }

    /// <summary>Rotates and mirrors a photo upright (EXIF orientations 2-8: mirror first, then rotate clockwise).</summary>
    private static BitmapSource Orient(BitmapSource source, int orientation)
    {
        (double angle, bool mirror) = orientation switch
        {
            2 => (0, true),
            3 => (180, false),
            4 => (180, true),
            5 => (270, true),
            6 => (90, false),
            7 => (90, true),
            8 => (270, false),
            _ => (0, false),
        };
        if (angle == 0 && !mirror)
        {
            return source;
        }

        var transform = new TransformGroup();
        if (mirror)
        {
            transform.Children.Add(new ScaleTransform(-1, 1));
        }

        transform.Children.Add(new RotateTransform(angle));
        return new TransformedBitmap(source, transform);
    }
}
