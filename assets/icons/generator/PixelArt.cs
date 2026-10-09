namespace HolidayLights.Branding;

/// <summary>
/// The scaling and glow rules of PRODUCT-SPEC 5.3.3 and 5.9, for pictures of real 2003 bulb art in the help: Smooth (MMPX,
/// then area averaging), Crisp (nearest neighbour, then area averaging) and the additive glow of lit pixels.
/// </summary>
internal static class PixelArt
{
    /// <summary>Enlarges an image by an integer factor without smoothing.</summary>
    /// <param name="image">The image.</param>
    /// <param name="factor">The factor.</param>
    /// <returns>The enlarged image.</returns>
    public static PixelImage Nearest(PixelImage image, int factor)
    {
        PixelImage result = PixelImage.Create(image.Width * factor, image.Height * factor);
        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                result.Pixels[y * result.Width + x] = image.Pixels[y / factor * image.Width + x / factor];
            }
        }

        return result;
    }

    /// <summary>"Smooth": MMPX to the next power of two at or above <paramref name="scale"/>, then area-average down.</summary>
    /// <param name="art">Bulb art (alpha 0 or 255).</param>
    /// <param name="scale">The scale.</param>
    /// <returns>The scaled sprite.</returns>
    public static PixelImage Smooth(PixelImage art, double scale)
    {
        PixelImage big = art;
        int factor = 1;
        while (factor < scale)
        {
            big = Mmpx.Scale2x(big);
            factor *= 2;
        }

        return Box(big, (int)Math.Round(art.Width * scale), (int)Math.Round(art.Height * scale));
    }

    /// <summary>"Crisp": nearest neighbour to the next whole factor, then area-average down (Classic 2003).</summary>
    /// <param name="art">Bulb art.</param>
    /// <param name="scale">The scale.</param>
    /// <returns>The scaled sprite.</returns>
    public static PixelImage Crisp(PixelImage art, double scale) =>
        Box(Nearest(art, (int)Math.Ceiling(scale)), (int)Math.Round(art.Width * scale), (int)Math.Round(art.Height * scale));

    /// <summary>Area-average resample, accumulated in premultiplied alpha so transparent pixels add no colour.</summary>
    /// <param name="source">The source.</param>
    /// <param name="width">Target width.</param>
    /// <param name="height">Target height.</param>
    /// <returns>The resampled image (straight alpha).</returns>
    public static PixelImage Box(PixelImage source, int width, int height)
    {
        PixelImage result = PixelImage.Create(width, height);
        double fx = (double)source.Width / width;
        double fy = (double)source.Height / height;
        for (int y = 0; y < height; y++)
        {
            double y0 = y * fy, y1 = (y + 1) * fy;
            for (int x = 0; x < width; x++)
            {
                double x0 = x * fx, x1 = (x + 1) * fx;
                double a = 0, r = 0, g = 0, b = 0, area = 0;
                for (int sy = (int)y0; sy < Math.Min(source.Height, (int)Math.Ceiling(y1)); sy++)
                {
                    double wy = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
                    for (int sx = (int)x0; sx < Math.Min(source.Width, (int)Math.Ceiling(x1)); sx++)
                    {
                        double weight = wy * (Math.Min(x1, sx + 1) - Math.Max(x0, sx));
                        uint p = source.Pixels[sy * source.Width + sx];
                        double pa = (p >> 24) / 255.0;
                        a += weight * pa;
                        r += weight * pa * ((p >> 16) & 0xFF);
                        g += weight * pa * ((p >> 8) & 0xFF);
                        b += weight * pa * (p & 0xFF);
                        area += weight;
                    }
                }

                if (a <= 0)
                {
                    continue;
                }

                uint A(double v) => (uint)Math.Clamp(Math.Round(v), 0, 255);
                result.Pixels[y * width + x] = (A(a / area * 255) << 24) | (A(r / a) << 16) | (A(g / a) << 8) | A(b / a);
            }
        }

        return result;
    }

    /// <summary>
    /// The glow of a light bulb (PRODUCT-SPEC 5.9): its emissive pixels (luma lit - luma unlit >= 32) in their lit colours,
    /// scaled like the sprite, blurred with sigma = 0.22 x the larger cell side, times the intensity.
    /// </summary>
    /// <param name="lit">The lit frame.</param>
    /// <param name="unlit">The unlit frame.</param>
    /// <param name="scale">The sprite scale.</param>
    /// <param name="intensity">0.55 (Soft) or 1.0 (Bright).</param>
    /// <returns>Additive light (rgb) with a margin of three sigma on every side.</returns>
    public static (float[] Light, int Width, int Height, int Margin) Glow(PixelImage lit, PixelImage unlit, double scale, double intensity)
    {
        PixelImage emissive = PixelImage.Create(lit.Width, lit.Height);
        for (int i = 0; i < lit.Pixels.Length; i++)
        {
            uint l = lit.Pixels[i];
            if (l >> 24 != 0 && Luma(l) - Luma(unlit.Pixels[i]) >= 32)
            {
                emissive.Pixels[i] = l;
            }
        }

        PixelImage sprite = Smooth(emissive, scale);
        double sigma = 0.22 * Math.Max(sprite.Width, sprite.Height);
        int margin = (int)Math.Ceiling(3 * sigma);
        int width = sprite.Width + 2 * margin;
        int height = sprite.Height + 2 * margin;
        var light = new float[width * height * 3];
        for (int y = 0; y < sprite.Height; y++)
        {
            for (int x = 0; x < sprite.Width; x++)
            {
                uint p = sprite.Pixels[y * sprite.Width + x];
                float a = (p >> 24) / 255f;
                int index = ((y + margin) * width + x + margin) * 3;
                light[index] = a * ((p >> 16) & 0xFF);
                light[index + 1] = a * ((p >> 8) & 0xFF);
                light[index + 2] = a * (p & 0xFF);
            }
        }

        BlurInPlace(light, width, height, sigma);
        for (int i = 0; i < light.Length; i++)
        {
            light[i] *= (float)intensity;
        }

        return (light, width, height, margin);
    }

    /// <summary>Adds light to an opaque image (additive blending, clamped).</summary>
    /// <param name="target">The image, changed in place.</param>
    /// <param name="light">Light from <see cref="Glow"/>.</param>
    /// <param name="width">Width of the light buffer.</param>
    /// <param name="height">Height of the light buffer.</param>
    /// <param name="left">X of the light buffer in the target.</param>
    /// <param name="top">Y of the light buffer in the target.</param>
    public static void AddLight(PixelImage target, float[] light, int width, int height, int left, int top)
    {
        for (int y = 0; y < height; y++)
        {
            int ty = top + y;
            if ((uint)ty >= (uint)target.Height)
            {
                continue;
            }

            for (int x = 0; x < width; x++)
            {
                int tx = left + x;
                if ((uint)tx >= (uint)target.Width)
                {
                    continue;
                }

                int i = (y * width + x) * 3;
                uint p = target.Pixels[ty * target.Width + tx];
                uint Add(int shift, float value) => (uint)Math.Min(255, ((p >> shift) & 0xFF) + (int)Math.Round(value));
                target.Pixels[ty * target.Width + tx] = (p & 0xFF000000u) | (Add(16, light[i]) << 16) | (Add(8, light[i + 1]) << 8) | Add(0, light[i + 2]);
            }
        }
    }

    private static int Luma(uint c) => (int)Math.Round(0.299 * ((c >> 16) & 0xFF) + 0.587 * ((c >> 8) & 0xFF) + 0.114 * (c & 0xFF));

    private static void BlurInPlace(float[] rgb, int width, int height, double sigma)
    {
        int radius = (int)Math.Ceiling(3 * sigma);
        var kernel = new float[2 * radius + 1];
        double sum = 0;
        for (int i = -radius; i <= radius; i++)
        {
            kernel[i + radius] = (float)Math.Exp(-(i * i) / (2 * sigma * sigma));
            sum += kernel[i + radius];
        }

        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= (float)sum;
        }

        var temp = new float[rgb.Length];
        Pass(rgb, temp, width, height, kernel, radius, horizontal: true);
        Pass(temp, rgb, width, height, kernel, radius, horizontal: false);
    }

    private static void Pass(float[] source, float[] target, int width, int height, float[] kernel, int radius, bool horizontal)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float r = 0, g = 0, b = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = horizontal ? x + k : x;
                    int sy = horizontal ? y : y + k;
                    if ((uint)sx >= (uint)width || (uint)sy >= (uint)height)
                    {
                        continue;
                    }

                    int i = (sy * width + sx) * 3;
                    float w = kernel[k + radius];
                    r += source[i] * w;
                    g += source[i + 1] * w;
                    b += source[i + 2] * w;
                }

                int o = (y * width + x) * 3;
                target[o] = r;
                target[o + 1] = g;
                target[o + 2] = b;
            }
        }
    }
}
