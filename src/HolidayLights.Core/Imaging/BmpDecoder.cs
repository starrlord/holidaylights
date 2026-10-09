namespace HolidayLights.Core.Imaging;

/// <summary>
/// Decodes the uncompressed Windows BMP files of 5.4: RT_BITMAP sheets and masks (1, 4, 8 bpp), screen saver pictures
/// (two concatenated BMPs) and user BMPs (24, 32 bpp). Owner: core-bulbs.
/// </summary>
/// <remarks>
/// Also reads the other common BMP variants a user picture may come in (RLE4/RLE8, 16 bpp, bit fields, top-down rows,
/// V4/V5 headers). Every member is thread-safe.
/// </remarks>
public static class BmpDecoder
{
    /// <summary>Decodes the first bitmap of a BMP file as an opaque image.</summary>
    /// <param name="file">The whole file (BITMAPFILEHEADER + BITMAPINFOHEADER + colour table + bits; bottom-up or top-down).</param>
    /// <returns>The image (alpha 255 everywhere).</returns>
    /// <exception cref="InvalidDataException">The data is not a supported BMP.</exception>
    public static Rgba32Image Decode(ReadOnlySpan<byte> file) => BmpReader.Read(file);

    /// <summary>
    /// Decodes 5.4 masked art: where the 1 bpp mask is black (colour index 1) the pixel is opaque with the art colour,
    /// elsewhere transparent with colour 0. A mask smaller than the art (Sun: 136 x 30 for 136 x 32) leaves the rest transparent.
    /// </summary>
    /// <remarks>
    /// A mask pixel counts as black when its colour (through the mask's own colour table) has a luma below 128, the rule
    /// the reference exporter applies; for the 5.4 masks this is colour index 1.
    /// </remarks>
    /// <param name="art">The art BMP file.</param>
    /// <param name="mask">The mask BMP file (colour table {0: white, 1: black}).</param>
    /// <returns>The image with alpha 0 or 255.</returns>
    /// <exception cref="InvalidDataException">Either file is not a supported BMP.</exception>
    public static Rgba32Image DecodeMasked(ReadOnlySpan<byte> art, ReadOnlySpan<byte> mask)
    {
        Rgba32Image colors = BmpReader.Read(art);
        Rgba32Image shape = BmpReader.Read(mask);
        var pixels = new uint[colors.Pixels.Length];
        int rows = Math.Min(colors.Height, shape.Height);
        int columns = Math.Min(colors.Width, shape.Width);
        for (int y = 0; y < rows; y++)
        {
            ReadOnlySpan<uint> artRow = colors.Pixels.AsSpan(y * colors.Width, colors.Width);
            ReadOnlySpan<uint> maskRow = shape.Pixels.AsSpan(y * shape.Width, shape.Width);
            Span<uint> outRow = pixels.AsSpan(y * colors.Width, colors.Width);
            for (int x = 0; x < columns; x++)
            {
                if (IsDark(maskRow[x]))
                {
                    outRow[x] = artRow[x];
                }
            }
        }

        return new Rgba32Image(colors.Width, colors.Height, pixels);
    }

    /// <summary>
    /// The 5.4 picture rule: when the file holds two concatenated BMPs (the shipped
    /// pictures: a licence notice, then the picture) the second is decoded, otherwise the first.
    /// </summary>
    /// <param name="file">The whole file.</param>
    /// <returns>The picture (opaque).</returns>
    /// <exception cref="InvalidDataException">The data is not a supported BMP.</exception>
    public static Rgba32Image DecodePicture(ReadOnlySpan<byte> file)
    {
        // The 5.4 test: first bfSize + 6 < file size, "BM" at bfSize, and bfSize + second bfSize == file size.
        long first = BmpReader.ReadClaimedSize(file);
        if (first > 0 && first + 6 < file.Length)
        {
            ReadOnlySpan<byte> rest = file[(int)first..];
            long second = BmpReader.ReadClaimedSize(rest);
            if (second > 0 && first + second == file.Length)
            {
                return BmpReader.Read(rest);
            }
        }

        return BmpReader.Read(file);
    }

    /// <summary>The luma test of the reference exporter (Pillow's "L" conversion, then below 128).</summary>
    private static bool IsDark(uint pixel)
    {
        uint luma = ((pixel >> 16 & 0xFF) * 19595 + (pixel >> 8 & 0xFF) * 38470 + (pixel & 0xFF) * 7471 + 0x8000) >> 16;
        return luma < 128;
    }
}
