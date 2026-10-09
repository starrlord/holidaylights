namespace HolidayLights.Core.Bulbs;

/// <summary>
/// Tells a lit/unlit pair from other two-frame animations (PRODUCT-SPEC 5.4): identical masks, the brighter frame's mean
/// luma at least 12 % above the other's, and at least 5 % of the opaque pixels differing in luma by 32 or more. The
/// brighter frame is the lit one.
/// </summary>
internal static class LightBulbClassifier
{
    private const double MinimumBrightnessRatio = 1.12;
    private const double SignificantLumaDifference = 32;
    private const double MinimumChangedShare = 0.05;

    /// <summary>Classifies two frames.</summary>
    /// <param name="first">Frame 0.</param>
    /// <param name="second">Frame 1.</param>
    /// <param name="litFrame">The brighter frame (0 or 1) when the pair is a light bulb.</param>
    /// <returns>True for a light bulb.</returns>
    public static bool TryClassify(Rgba32Image first, Rgba32Image second, out int litFrame)
    {
        litFrame = 0;
        if (first.Width != second.Width || first.Height != second.Height)
        {
            return false;
        }

        long opaque = 0;
        long changed = 0;
        double firstSum = 0;
        double secondSum = 0;
        uint[] a = first.Pixels;
        uint[] b = second.Pixels;
        for (int i = 0; i < a.Length; i++)
        {
            bool aOpaque = Bgra32.A(a[i]) != 0;
            if (aOpaque != (Bgra32.A(b[i]) != 0))
            {
                return false;
            }

            if (!aOpaque)
            {
                continue;
            }

            double la = Luma(a[i]);
            double lb = Luma(b[i]);
            opaque++;
            firstSum += la;
            secondSum += lb;
            if (Math.Abs(la - lb) >= SignificantLumaDifference)
            {
                changed++;
            }
        }

        if (opaque == 0 || changed < MinimumChangedShare * opaque)
        {
            return false;
        }

        double bright = Math.Max(firstSum, secondSum);
        double dim = Math.Min(firstSum, secondSum);
        if (bright <= dim || bright < dim * MinimumBrightnessRatio)
        {
            return false;
        }

        litFrame = firstSum >= secondSum ? 0 : 1;
        return true;
    }

    /// <summary>Luma 0.299 R + 0.587 G + 0.114 B (0-255).</summary>
    /// <param name="pixel">A packed pixel.</param>
    /// <returns>The luma.</returns>
    public static double Luma(uint pixel) => 0.299 * Bgra32.R(pixel) + 0.587 * Bgra32.G(pixel) + 0.114 * Bgra32.B(pixel);
}
