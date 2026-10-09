namespace HolidayLights.Core.Sprites;

/// <summary>
/// Area averaging (a box filter with fractional coverage) to an exact size: a port of <c>BoxResample</c> of the upscaling
/// prototype with the same double-precision arithmetic in the same order, so results equal its reference images
/// (<c>tests/HolidayLights.Tests/Sprites/Golden/upscale</c>) bit for bit, including how exact half-coverage ties round.
/// </summary>
/// <remarks>
/// <para>Colour is accumulated weighted by alpha (<c>sum w a rgb</c>, <c>sum w a</c>), so transparent pixels contribute no
/// colour and edges get clean anti-aliased alpha without dark fringes. Input and output are straight alpha; callers
/// premultiply the result.</para>
/// <para>The source can be magnified by an integer factor with nearest neighbour first ("Crisp", and Smooth's doublings
/// beyond 4x). The magnified picture is never built: footprints index the original pixels directly, which is the same
/// arithmetic without the memory.</para>
/// </remarks>
internal static class AreaResampler
{
    private static readonly double[] AlphaFraction = [.. Enumerable.Range(0, 256).Select(a => a / 255.0)];

    /// <summary>Resamples straight-alpha pixels.</summary>
    /// <param name="source">Straight-alpha source pixels, row-major, transparent pixels canonical (0).</param>
    /// <param name="sourceWidth">Source width (at least 1).</param>
    /// <param name="sourceHeight">Source height (at least 1).</param>
    /// <param name="magnification">Nearest-neighbour factor applied to the source first (1 = none).</param>
    /// <param name="width">Output width (at least 1).</param>
    /// <param name="height">Output height (at least 1).</param>
    /// <returns>Straight-alpha output pixels, row-major.</returns>
    public static uint[] Resample(uint[] source, int sourceWidth, int sourceHeight, int magnification, int width, int height)
    {
        Footprints columns = Footprints.Create(sourceWidth, magnification, width);
        Footprints rows = Footprints.Create(sourceHeight, magnification, height);
        var result = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            int rowTapsEnd = rows.First[y + 1];
            for (int x = 0; x < width; x++)
            {
                double a = 0, r = 0, g = 0, b = 0, area = 0;
                int columnTapsEnd = columns.First[x + 1];
                for (int j = rows.First[y]; j < rowTapsEnd; j++)
                {
                    double wy = rows.Weight[j];
                    int sourceRow = rows.Source[j] * sourceWidth;
                    for (int i = columns.First[x]; i < columnTapsEnd; i++)
                    {
                        double weight = wy * columns.Weight[i];
                        area += weight;
                        uint p = source[sourceRow + columns.Source[i]];
                        if (p != 0)
                        {
                            double alphaWeight = weight * AlphaFraction[p >> 24];
                            a += alphaWeight;
                            r += alphaWeight * ((p >> 16) & 255);
                            g += alphaWeight * ((p >> 8) & 255);
                            b += alphaWeight * (p & 255);
                        }
                    }
                }

                a /= area;
                if (a <= 0)
                {
                    continue;
                }

                uint alpha = (uint)Math.Round(a * 255);
                uint red = Math.Min(255u, (uint)Math.Round(r / area / a));
                uint green = Math.Min(255u, (uint)Math.Round(g / area / a));
                uint blue = Math.Min(255u, (uint)Math.Round(b / area / a));
                result[y * width + x] = alpha << 24 | red << 16 | green << 8 | blue;
            }
        }

        return result;
    }

    /// <summary>
    /// The source pixels each output pixel of one axis covers ("taps"), with the prototype's coverage weights: output pixel
    /// <c>i</c> spans <c>[i f, (i + 1) f)</c> of the (magnified) source, <c>f = source / output</c>.
    /// </summary>
    private sealed class Footprints
    {
        private Footprints(int[] first, int[] source, double[] weight)
        {
            First = first;
            Source = source;
            Weight = weight;
        }

        /// <summary>Index of each output pixel's first tap; <c>First[n]</c> ends the taps of pixel <c>n - 1</c>.</summary>
        public int[] First { get; }

        /// <summary>The original (unmagnified) source index of each tap.</summary>
        public int[] Source { get; }

        /// <summary>The coverage of each tap.</summary>
        public double[] Weight { get; }

        public static Footprints Create(int sourceLength, int magnification, int length)
        {
            int magnified = sourceLength * magnification;
            double factor = (double)magnified / length;
            var first = new int[length + 1];
            var source = new List<int>();
            var weight = new List<double>();
            for (int i = 0; i < length; i++)
            {
                double x0 = i * factor;
                double x1 = (i + 1) * factor;
                first[i] = source.Count;
                for (int s = (int)x0; s < Math.Min(magnified, (int)Math.Ceiling(x1)); s++)
                {
                    source.Add(s / magnification);
                    weight.Add(Math.Min(x1, s + 1) - Math.Max(x0, s));
                }
            }

            first[length] = source.Count;
            return new Footprints(first, [.. source], [.. weight]);
        }
    }
}
