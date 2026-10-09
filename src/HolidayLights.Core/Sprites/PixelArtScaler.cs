namespace HolidayLights.Core.Sprites;

/// <summary>
/// Pixel-art scaling (PRODUCT-SPEC 5.3.3): MMPX (exact port of the upscaling prototype) then area averaging in
/// premultiplied alpha (Smooth), or nearest neighbour to ceil(S) then area averaging (Crisp). Also used for the About
/// banner and the bundled screen saver pictures. Owner: core-sprites.
/// </summary>
/// <remarks>
/// <para>Smooth: transparent pixels are canonicalized to 0, MMPX 2x is applied to reach the smallest power of two at or
/// above S (twice for 4x; nearest-neighbour doublings beyond), then the picture is area-averaged to the exact size.</para>
/// <para>Crisp: nearest neighbour to ceil(S), then area averaging: blocky pixels with at most one soft seam pixel at
/// fractional scales, pure nearest neighbour at integer scales.</para>
/// <para>The averaging is the prototype's <c>BoxResample</c>, so for the same recipe the
/// result is the premultiplied form of the prototype's output, bit for bit. Nearest-neighbour magnification is folded into the
/// averaging instead of being built in memory. The size is always <see cref="ArtScale.Scale(SizeI, double)"/>, so
/// sprites fit the layout to the pixel. Pure and thread-safe.</para>
/// </remarks>
public static class PixelArtScaler
{
    /// <summary>The largest magnification MMPX is applied for; Smooth continues with nearest-neighbour doublings.</summary>
    private const int MaxMmpxFactor = 4;

    /// <summary>The largest result accepted (64 Mpixels, far beyond any display or preview).</summary>
    private const long MaxResultPixels = 1L << 26;

    /// <summary>Scales art to <c>ArtScale.Scale(size, scale)</c> exactly; at scale 1 both styles return the original pixels.</summary>
    /// <param name="source">Straight-alpha art (transparent pixels are canonicalized to 0 first).</param>
    /// <param name="scale">S (any positive value).</param>
    /// <param name="style">Smooth or Crisp.</param>
    /// <returns>The premultiplied result.</returns>
    public static PremultipliedImage Scale(Rgba32Image source, double scale, SpriteStyle style) =>
        Scale(source, scale, style, PixelArtScaleOptions.Default);

    /// <summary>Scales art to <c>ArtScale.Scale(size, scale)</c> exactly, with explicit options.</summary>
    /// <param name="source">Straight-alpha art (transparent pixels are canonicalized to 0 first).</param>
    /// <param name="scale">S (any positive value).</param>
    /// <param name="style">Smooth or Crisp.</param>
    /// <param name="options">MMPX edge handling and the optional alpha threshold.</param>
    /// <returns>The premultiplied result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="scale"/> is not a positive finite number or makes the result larger than 64 Mpixels, or the style is unknown.
    /// </exception>
    public static PremultipliedImage Scale(Rgba32Image source, double scale, SpriteStyle style, PixelArtScaleOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale must be a positive finite number.");
        }

        if (style is not (SpriteStyle.Smooth or SpriteStyle.Crisp))
        {
            throw new ArgumentOutOfRangeException(nameof(style), style, null);
        }

        if (source.Width * scale * source.Height * scale > MaxResultPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scaled picture would be too large.");
        }

        SizeI size = ArtScale.Scale(source.Size, scale);
        if (size.IsEmpty)
        {
            return new PremultipliedImage(size.Width, size.Height);
        }

        uint[] pixels = Canonical(source.Pixels);
        int width = source.Width;
        int height = source.Height;
        int nearest;
        if (style == SpriteStyle.Smooth)
        {
            int magnification = MagnificationFor(scale);
            for (int factor = 1; factor < Math.Min(magnification, MaxMmpxFactor); factor *= 2)
            {
                pixels = Mmpx.Scale2x(pixels, width, height, options.Edge);
                width *= 2;
                height *= 2;
            }

            nearest = Math.Max(1, magnification / MaxMmpxFactor);
        }
        else
        {
            nearest = (int)Math.Ceiling(scale);
        }

        pixels = size.Width % width == 0 && size.Height % height == 0
            ? Replicate(pixels, width, height, size.Width / width, size.Height / height)
            : AreaResampler.Resample(pixels, width, height, nearest, size.Width, size.Height);
        return Finish(pixels, size, options.AlphaThreshold);
    }

    /// <summary>The magnification Smooth reaches before averaging: the smallest power of two at or above S (at least 1).</summary>
    /// <param name="scale">S.</param>
    /// <returns>1, 2, 4, 8, ...; MMPX provides up to 4x, nearest-neighbour doublings the rest.</returns>
    internal static int MagnificationFor(double scale)
    {
        int factor = 1;
        while (factor < scale)
        {
            factor *= 2;
        }

        return factor;
    }

    /// <summary>Copies straight-alpha pixels with every transparent pixel set to the canonical 0 that MMPX compares against.</summary>
    private static uint[] Canonical(uint[] straight)
    {
        var result = new uint[straight.Length];
        for (int i = 0; i < result.Length; i++)
        {
            uint p = straight[i];
            result[i] = p >> 24 == 0 ? Bgra32.Transparent : p;
        }

        return result;
    }

    /// <summary>Nearest-neighbour magnification by integer factors (the area average of an integer enlargement).</summary>
    private static uint[] Replicate(uint[] pixels, int width, int height, int factorX, int factorY)
    {
        if (factorX == 1 && factorY == 1)
        {
            return pixels;
        }

        int outWidth = width * factorX;
        var result = new uint[outWidth * height * factorY];
        for (int y = 0; y < height; y++)
        {
            Span<uint> row = result.AsSpan(y * factorY * outWidth, outWidth);
            for (int x = 0; x < width; x++)
            {
                row.Slice(x * factorX, factorX).Fill(pixels[y * width + x]);
            }

            for (int copy = 1; copy < factorY; copy++)
            {
                row.CopyTo(result.AsSpan((y * factorY + copy) * outWidth, outWidth));
            }
        }

        return result;
    }

    /// <summary>Premultiplies straight-alpha pixels in place, or applies the alpha threshold (the prototype's <c>Threshold</c>).</summary>
    private static PremultipliedImage Finish(uint[] straight, SizeI size, byte? alphaThreshold)
    {
        for (int i = 0; i < straight.Length; i++)
        {
            uint p = straight[i];
            uint alpha = p >> 24;
            straight[i] = alphaThreshold is byte threshold
                ? alpha > 0 && alpha >= threshold ? p | 0xFF000000u : Bgra32.Transparent
                : Bgra32.Premultiply(p);
        }

        return new PremultipliedImage(size.Width, size.Height, straight);
    }
}
