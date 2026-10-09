using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HolidayLights.Branding;

/// <summary>A straight-alpha BGRA image (<c>0xAARRGGBB</c> per pixel, rows top-down).</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Pixels">The pixels, <c>Width * Height</c> of them.</param>
internal sealed record PixelImage(int Width, int Height, uint[] Pixels)
{
    /// <summary>Creates a transparent image.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>The image.</returns>
    public static PixelImage Create(int width, int height) => new(width, height, new uint[width * height]);

    /// <summary>Converts the pixels to a frozen WPF bitmap (straight alpha).</summary>
    /// <param name="dpi">Resolution stored with the bitmap: 96 shows one pixel per DIP, 192 two pixels per DIP.</param>
    /// <returns>The bitmap.</returns>
    public BitmapSource ToBitmapSource(double dpi = 96)
    {
        BitmapSource source = BitmapSource.Create(Width, Height, dpi, dpi, PixelFormats.Bgra32, null, Pixels, Width * 4);
        source.Freeze();
        return source;
    }
}

/// <summary>Rendering, conversion and file helpers shared by the generators.</summary>
internal static class Raster
{
    /// <summary>Renders a drawing (in DIP coordinates of its own canvas) to a premultiplied bitmap.</summary>
    /// <param name="drawing">The drawing; content outside the canvas is clipped.</param>
    /// <param name="width">Pixel width of the result.</param>
    /// <param name="height">Pixel height of the result.</param>
    /// <param name="scale">Scale from drawing units to pixels.</param>
    /// <returns>A frozen <see cref="PixelFormats.Pbgra32"/> bitmap.</returns>
    public static BitmapSource Render(Drawing drawing, int width, int height, double scale = 1)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawDrawing(drawing);
            dc.Pop();
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>Renders a drawing to a straight-alpha image.</summary>
    /// <param name="drawing">The drawing.</param>
    /// <param name="width">Pixel width.</param>
    /// <param name="height">Pixel height.</param>
    /// <param name="scale">Scale from drawing units to pixels.</param>
    /// <returns>The image.</returns>
    public static PixelImage RenderImage(Drawing drawing, int width, int height, double scale = 1) =>
        ToImage(Render(drawing, width, height, scale));

    /// <summary>Converts any bitmap to a straight-alpha BGRA image.</summary>
    /// <param name="source">The bitmap.</param>
    /// <returns>The image.</returns>
    public static PixelImage ToImage(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new uint[converted.PixelWidth * converted.PixelHeight];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return new PixelImage(converted.PixelWidth, converted.PixelHeight, pixels);
    }

    /// <summary>Loads a BMP, PNG or GIF file as a straight-alpha image (first frame).</summary>
    /// <param name="path">The file.</param>
    /// <returns>The image.</returns>
    public static PixelImage Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        return ToImage(frame);
    }

    /// <summary>Encodes an image as PNG bytes.</summary>
    /// <param name="image">The image.</param>
    /// <param name="dpi">The resolution stored in the file (192 for pictures drawn at twice their display size).</param>
    /// <returns>The PNG file content.</returns>
    public static byte[] EncodePng(PixelImage image, double dpi = 96)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image.ToBitmapSource(dpi)));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }

    /// <summary>Writes an image as a PNG file, creating the folder when needed.</summary>
    /// <param name="image">The image.</param>
    /// <param name="path">The file.</param>
    /// <param name="dpi">The resolution stored in the file.</param>
    public static void SavePng(PixelImage image, string path, double dpi = 96)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, EncodePng(image, dpi));
    }

    /// <summary>Wraps an image in an <see cref="ImageDrawing"/> placed at a rectangle (for composing sheets).</summary>
    /// <param name="image">The image.</param>
    /// <param name="bounds">Where to draw it, in DIP.</param>
    /// <returns>The drawing.</returns>
    public static ImageDrawing AsDrawing(PixelImage image, Rect bounds) => new(image.ToBitmapSource(), bounds);

    /// <summary>Composites <paramref name="top"/> over <paramref name="bottom"/> at an offset (source-over, straight alpha).</summary>
    /// <param name="bottom">The destination, changed in place.</param>
    /// <param name="top">The source.</param>
    /// <param name="offsetX">X of the source in the destination.</param>
    /// <param name="offsetY">Y of the source in the destination.</param>
    public static void Over(PixelImage bottom, PixelImage top, int offsetX, int offsetY)
    {
        for (int y = 0; y < top.Height; y++)
        {
            int dy = y + offsetY;
            if ((uint)dy >= (uint)bottom.Height)
            {
                continue;
            }

            for (int x = 0; x < top.Width; x++)
            {
                int dx = x + offsetX;
                if ((uint)dx >= (uint)bottom.Width)
                {
                    continue;
                }

                int index = dy * bottom.Width + dx;
                bottom.Pixels[index] = Blend(bottom.Pixels[index], top.Pixels[y * top.Width + x]);
            }
        }
    }

    /// <summary>Source-over blend of two straight-alpha pixels.</summary>
    /// <param name="dst">The pixel below.</param>
    /// <param name="src">The pixel above.</param>
    /// <returns>The blended pixel.</returns>
    public static uint Blend(uint dst, uint src)
    {
        double sa = (src >> 24) / 255.0;
        if (sa <= 0)
        {
            return dst;
        }

        double da = (dst >> 24) / 255.0;
        double oa = sa + da * (1 - sa);
        uint Channel(int shift)
        {
            double s = (src >> shift) & 0xFF;
            double d = (dst >> shift) & 0xFF;
            return (uint)Math.Clamp(Math.Round((s * sa + d * da * (1 - sa)) / oa), 0, 255);
        }

        return ((uint)Math.Round(oa * 255) << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }
}
