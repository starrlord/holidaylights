using System.Numerics;

namespace HolidayLights.Core.Sprites;

/// <summary>
/// Spreads the emissive pixels of a light bulb with a separable Gaussian (the work behind <see cref="GlowBaker.Bake"/>).
/// </summary>
/// <remarks>
/// Each colour channel is a float plane. The horizontal pass scatters every emissive pixel's colour along its row (only
/// emissive pixels cost anything); the vertical pass adds each filtered row into the rows it reaches. Both passes are
/// "add a scaled run of floats", done with <see cref="Vector{T}"/> where the hardware allows; the arithmetic per element
/// is the same multiply-then-add either way, so results do not depend on the machine.
/// </remarks>
internal static class EmittedLight
{
    /// <summary>Blurs the emissive pixels of <paramref name="lit"/> into an additive halo with a margin of the kernel radius.</summary>
    /// <param name="lit">The lit frame (premultiplied).</param>
    /// <param name="unlit">The unlit frame (premultiplied, same size).</param>
    /// <param name="kernel">The normalized Gaussian kernel (odd length; its radius is the margin).</param>
    /// <returns>The halo (alpha 0, colour = light), or null when no pixel is emissive.</returns>
    public static PremultipliedImage? Spread(PremultipliedImage lit, PremultipliedImage unlit, float[] kernel)
    {
        int width = lit.Width;
        int height = lit.Height;
        int margin = kernel.Length / 2;
        int outWidth = width + 2 * margin;
        int outHeight = height + 2 * margin;

        // Horizontal pass: one row of outWidth floats per source row and channel.
        var rows = new Planes(outWidth * height);
        var rowHasLight = new bool[height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint l = lit.Pixels[y * width + x];
                if (!GlowBaker.IsEmissive(l, unlit.Pixels[y * width + x]))
                {
                    continue;
                }

                rowHasLight[y] = true;
                int start = y * outWidth + x;
                MultiplyAdd(rows.Red.AsSpan(start, kernel.Length), kernel, (l >> 16) & 0xFF);
                MultiplyAdd(rows.Green.AsSpan(start, kernel.Length), kernel, (l >> 8) & 0xFF);
                MultiplyAdd(rows.Blue.AsSpan(start, kernel.Length), kernel, l & 0xFF);
            }
        }

        if (Array.IndexOf(rowHasLight, true) < 0)
        {
            return null;
        }

        // Vertical pass: source row y reaches output rows y .. y + 2 margin.
        var output = new Planes(outWidth * outHeight);
        for (int y = 0; y < height; y++)
        {
            if (!rowHasLight[y])
            {
                continue;
            }

            int source = y * outWidth;
            for (int k = 0; k < kernel.Length; k++)
            {
                int target = (y + k) * outWidth;
                MultiplyAdd(output.Red.AsSpan(target, outWidth), rows.Red.AsSpan(source, outWidth), kernel[k]);
                MultiplyAdd(output.Green.AsSpan(target, outWidth), rows.Green.AsSpan(source, outWidth), kernel[k]);
                MultiplyAdd(output.Blue.AsSpan(target, outWidth), rows.Blue.AsSpan(source, outWidth), kernel[k]);
            }
        }

        var pixels = new uint[outWidth * outHeight];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = ToByte(output.Red[i]) << 16 | ToByte(output.Green[i]) << 8 | ToByte(output.Blue[i]);
        }

        return new PremultipliedImage(outWidth, outHeight, pixels);
    }

    /// <summary><c>destination[i] += source[i] x factor</c>.</summary>
    private static void MultiplyAdd(Span<float> destination, ReadOnlySpan<float> source, float factor)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated && source.Length >= Vector<float>.Count)
        {
            var f = new Vector<float>(factor);
            for (; i <= source.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                Span<float> d = destination.Slice(i);
                (new Vector<float>(d) + new Vector<float>(source.Slice(i)) * f).CopyTo(d);
            }
        }

        for (; i < source.Length; i++)
        {
            destination[i] += source[i] * factor;
        }
    }

    private static uint ToByte(float value) => (uint)Math.Min(255, (int)(value + 0.5f));

    /// <summary>Three float planes (red, green, blue) of one size.</summary>
    private sealed class Planes(int length)
    {
        public float[] Red { get; } = new float[length];

        public float[] Green { get; } = new float[length];

        public float[] Blue { get; } = new float[length];
    }
}
