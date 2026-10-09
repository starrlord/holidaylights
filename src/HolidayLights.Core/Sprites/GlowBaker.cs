namespace HolidayLights.Core.Sprites;

/// <summary>Glow halos (PRODUCT-SPEC 5.9 with product-owner decision 1 of the review round). Owner: core-sprites.</summary>
/// <remarks>
/// <para>A halo is the light of a bulb's emissive pixels (5.4: where the lit frame is at least 32 luma brighter than the
/// unlit frame, or 32 brighter in its brightest channel, so saturated reds and blues count like other colours) in their
/// lit colours, spread by a Gaussian blur and stored premultiplied with alpha 0: composited as
/// <c>src + dst x (1 - 0)</c> it purely adds light to whatever lies behind it. The
/// blur is energy-preserving (normalized kernel), so the halo carries exactly the emitted light; the renderers scale it
/// by the brightness and the glow intensity (Soft 0.55, Bright 1.0).</para>
/// <para>Pure and thread-safe.</para>
/// </remarks>
public static class GlowBaker
{
    /// <summary>The blur sigma relative to the shorter side of the scaled cell: <c>sigma = 0.22 x min(width, height)</c>.</summary>
    public const double SigmaFactor = 0.22;

    /// <summary>The largest blur sigma, in art pixels (36 output pixels at 150 %).</summary>
    public const double MaxSigmaArtPixels = 24;

    /// <summary>
    /// A pixel is emissive when <c>luma(lit) - luma(unlit)</c> (luma 0-255), or the difference of its brightest channel
    /// <c>max(R, G, B)</c>, reaches this value.
    /// </summary>
    public const int EmissiveLumaThreshold = 32;

    /// <summary>
    /// Returns the blur sigma of a bulb's halo (product-owner decision 1: the shorter side, capped, so the halos of long
    /// add-on strips stay a glow around the bulbs instead of a haze hundreds of pixels wide).
    /// </summary>
    /// <param name="spriteSize">The scaled cell size (the sprite size at S).</param>
    /// <param name="scale">S, the art-pixel to output-pixel factor.</param>
    /// <returns><c>min(0.22 x min(width, height), 24 x S)</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not a positive finite number.</exception>
    public static double SigmaFor(SizeI spriteSize, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale must be a positive finite number.");
        }

        return Math.Min(SigmaFactor * Math.Min(spriteSize.Width, spriteSize.Height), MaxSigmaArtPixels * scale);
    }

    /// <summary>Returns the margin a halo extends beyond its sprite on every side.</summary>
    /// <param name="sigma">The blur sigma.</param>
    /// <returns><c>ceil(3 sigma)</c> (computed to 6 decimals, so 3 x 0.22 x 50 is 33, not 34).</returns>
    public static int MarginFor(double sigma) => (int)Math.Ceiling(Math.Round(3 * sigma, 6));

    /// <summary>
    /// Bakes a halo: the pixels where luma(lit) - luma(unlit) &gt;= 32 (luma = 0.299 R + 0.587 G + 0.114 B) or
    /// max(R, G, B)(lit) - max(R, G, B)(unlit) &gt;= 32 in their lit colours, Gaussian-blurred, margin ceil(3 sigma), stored
    /// premultiplied with alpha 0 (additive light).
    /// </summary>
    /// <param name="lit">The scaled lit frame.</param>
    /// <param name="unlit">The scaled unlit frame (same size).</param>
    /// <param name="sigma">Blur sigma in pixels (bulbs: <see cref="SigmaFor"/>).</param>
    /// <returns>The halo, or null when no pixel is emissive.</returns>
    /// <exception cref="ArgumentException">The frames differ in size.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sigma"/> is not a positive finite number.</exception>
    public static GlowSprite? Bake(PremultipliedImage lit, PremultipliedImage unlit, double sigma)
    {
        ArgumentNullException.ThrowIfNull(lit);
        ArgumentNullException.ThrowIfNull(unlit);
        if (lit.Width != unlit.Width || lit.Height != unlit.Height)
        {
            throw new ArgumentException(
                $"The unlit frame is {unlit.Width} x {unlit.Height}; the lit frame is {lit.Width} x {lit.Height}.", nameof(unlit));
        }

        if (!double.IsFinite(sigma) || sigma <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma), sigma, "The blur sigma must be a positive finite number.");
        }

        int margin = MarginFor(sigma);
        var light = EmittedLight.Spread(lit, unlit, GaussianKernel(sigma, margin));
        return light is null ? null : new GlowSprite(light, -margin, -margin);
    }

    /// <summary>
    /// True when a pixel of the lit frame is at least <see cref="EmissiveLumaThreshold"/> brighter than the unlit one, in luma
    /// or in its brightest channel.
    /// </summary>
    /// <remarks>
    /// Luma weighs red by 0.299 and blue by 0.114, so a saturated colour that lights up strongly can stay under the luma
    /// threshold: the red glass of Standard Bulbs goes from (153, 51, 0) to (255, 51, 0), only 30.5 luma, and glowed a
    /// third as much as the other colours. Its brightest channel rises by 102.
    /// </remarks>
    /// <param name="lit">The lit pixel (premultiplied).</param>
    /// <param name="unlit">The unlit pixel (premultiplied).</param>
    /// <returns>True for an emissive pixel.</returns>
    internal static bool IsEmissive(uint lit, uint unlit) =>
        LumaTimes1000(lit) - LumaTimes1000(unlit) >= EmissiveLumaThreshold * 1000 ||
        BrightestChannel(lit) - BrightestChannel(unlit) >= EmissiveLumaThreshold;

    /// <summary>The normalized Gaussian of a radius (<c>2 radius + 1</c> taps summing to 1).</summary>
    /// <remarks>
    /// The weights are rounded to 9 decimals before normalizing: the C runtime's <c>exp</c> may differ in the last bit
    /// between processors, and the rounding makes halos (and the disk cache) identical on every machine.
    /// </remarks>
    /// <param name="sigma">The sigma.</param>
    /// <param name="radius">Taps on each side of the centre.</param>
    /// <returns>The kernel.</returns>
    internal static float[] GaussianKernel(double sigma, int radius)
    {
        var weights = new double[2 * radius + 1];
        double sum = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            double d = i - radius;
            weights[i] = Math.Round(Math.Exp(-d * d / (2 * sigma * sigma)), 9);
            sum += weights[i];
        }

        var kernel = new float[weights.Length];
        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] = (float)(weights[i] / sum);
        }

        return kernel;
    }

    /// <summary>max(R, G, B).</summary>
    private static int BrightestChannel(uint pixel) =>
        Math.Max((int)((pixel >> 16) & 0xFF), Math.Max((int)((pixel >> 8) & 0xFF), (int)(pixel & 0xFF)));

    /// <summary>0.299 R + 0.587 G + 0.114 B, times 1000 (exact integer arithmetic).</summary>
    private static int LumaTimes1000(uint pixel) =>
        299 * (int)((pixel >> 16) & 0xFF) + 587 * (int)((pixel >> 8) & 0xFF) + 114 * (int)(pixel & 0xFF);
}
