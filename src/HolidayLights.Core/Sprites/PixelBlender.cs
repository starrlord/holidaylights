using System.Runtime.Intrinsics;

namespace HolidayLights.Core.Sprites;

/// <summary>
/// Row blending of premultiplied BGRA pixels with 8-bit integer arithmetic (the math of <see cref="CompositeMode"/>).
/// </summary>
/// <remarks>
/// <para>Opacity is an 8-bit factor <c>o</c> (255 = 1). Products are divided by 255 with exact rounding
/// (<see cref="Div255(uint)"/>). Source-over: every source channel is first scaled to <c>s x o</c>, then
/// <c>out = min(255, s + dst x (255 - s.a) / 255)</c> per channel, so colour above alpha adds light. Additive:
/// <c>out.rgb = min(255, dst.rgb + s.rgb x o)</c>, alpha unchanged.</para>
/// <para>The 128-bit vector path processes four pixels at a time and gives results identical to the scalar path.</para>
/// </remarks>
internal static class PixelBlender
{
    private const int VectorPixels = 4;

    /// <summary>Exact <c>round(x / 255)</c> for <c>0 &lt;= x &lt;= 65025</c> (products of two bytes).</summary>
    /// <param name="x">The product.</param>
    /// <returns>The rounded quotient.</returns>
    public static uint Div255(uint x)
    {
        x += 128;
        return (x + (x >> 8)) >> 8;
    }

    /// <summary>Converts an opacity 0-1 to the 8-bit factor (values outside 0-1 are clamped).</summary>
    /// <param name="opacity">The opacity.</param>
    /// <returns>0-255.</returns>
    public static uint OpacityToByte(float opacity) => (uint)MathF.Round(Math.Clamp(opacity, 0f, 1f) * 255f);

    /// <summary>Premultiplied source-over of a row.</summary>
    /// <param name="source">Source pixels.</param>
    /// <param name="destination">Destination pixels (same length).</param>
    /// <param name="opacity">The 8-bit opacity (1-255).</param>
    public static void SourceOver(ReadOnlySpan<uint> source, Span<uint> destination, uint opacity)
    {
        int i = Vector128.IsHardwareAccelerated ? SourceOverVector(source, destination, opacity) : 0;
        SourceOverScalar(source[i..], destination[i..], opacity);
    }

    /// <summary>Additive blending of a row.</summary>
    /// <param name="source">Source pixels (their alpha is ignored).</param>
    /// <param name="destination">Destination pixels (same length).</param>
    /// <param name="opacity">The 8-bit opacity (1-255).</param>
    public static void Additive(ReadOnlySpan<uint> source, Span<uint> destination, uint opacity)
    {
        int i = Vector128.IsHardwareAccelerated ? AdditiveVector(source, destination, opacity) : 0;
        AdditiveScalar(source[i..], destination[i..], opacity);
    }

    /// <summary>The scalar path of <see cref="SourceOver"/> (also the reference for the vector path).</summary>
    internal static void SourceOverScalar(ReadOnlySpan<uint> source, Span<uint> destination, uint opacity)
    {
        for (int i = 0; i < source.Length; i++)
        {
            uint s = source[i];
            if (s == 0)
            {
                continue;
            }

            if (opacity != 255)
            {
                s = Scale(s, opacity);
            }

            uint sa = s >> 24;
            if (sa == 255)
            {
                destination[i] = s;
                continue;
            }

            uint d = destination[i];
            uint inverse = 255 - sa;
            uint b = Math.Min(255u, (s & 0xFF) + Div255((d & 0xFF) * inverse));
            uint g = Math.Min(255u, ((s >> 8) & 0xFF) + Div255(((d >> 8) & 0xFF) * inverse));
            uint r = Math.Min(255u, ((s >> 16) & 0xFF) + Div255(((d >> 16) & 0xFF) * inverse));
            uint a = sa + Div255((d >> 24) * inverse);
            destination[i] = a << 24 | r << 16 | g << 8 | b;
        }
    }

    /// <summary>The scalar path of <see cref="Additive"/> (also the reference for the vector path).</summary>
    internal static void AdditiveScalar(ReadOnlySpan<uint> source, Span<uint> destination, uint opacity)
    {
        for (int i = 0; i < source.Length; i++)
        {
            uint s = source[i] & 0x00FFFFFF;
            if (s == 0)
            {
                continue;
            }

            if (opacity != 255)
            {
                s = Scale(s, opacity);
            }

            uint d = destination[i];
            uint b = Math.Min(255u, (d & 0xFF) + (s & 0xFF));
            uint g = Math.Min(255u, ((d >> 8) & 0xFF) + ((s >> 8) & 0xFF));
            uint r = Math.Min(255u, ((d >> 16) & 0xFF) + ((s >> 16) & 0xFF));
            destination[i] = (d & 0xFF000000) | r << 16 | g << 8 | b;
        }
    }

    /// <summary>Multiplies every channel of a pixel by an 8-bit factor.</summary>
    private static uint Scale(uint pixel, uint factor) =>
        Div255((pixel >> 24) * factor) << 24 |
        Div255(((pixel >> 16) & 0xFF) * factor) << 16 |
        Div255(((pixel >> 8) & 0xFF) * factor) << 8 |
        Div255((pixel & 0xFF) * factor);

    /// <summary>Blends whole groups of four pixels; returns the number of pixels done.</summary>
    private static int SourceOverVector(ReadOnlySpan<uint> source, Span<uint> destination, uint opacity)
    {
        var alphaOfEachPixel = Vector128.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15);
        var factor = Vector128.Create((ushort)opacity);
        int i = 0;
        for (; i <= source.Length - VectorPixels; i += VectorPixels)
        {
            Vector128<byte> s = Vector128.Create(source.Slice(i, VectorPixels)).AsByte();
            if (opacity != 255)
            {
                s = ScaleBytes(s, factor);
            }

            Vector128<byte> inverse = ~Vector128.Shuffle(s, alphaOfEachPixel);
            (Vector128<ushort> sLow, Vector128<ushort> sHigh) = Vector128.Widen(s);
            (Vector128<ushort> dLow, Vector128<ushort> dHigh) = Vector128.Widen(Vector128.Create((ReadOnlySpan<uint>)destination.Slice(i, VectorPixels)).AsByte());
            (Vector128<ushort> iLow, Vector128<ushort> iHigh) = Vector128.Widen(inverse);
            Vector128<ushort> low = Vector128.Min(sLow + Div255(dLow * iLow), Vector128.Create((ushort)255));
            Vector128<ushort> high = Vector128.Min(sHigh + Div255(dHigh * iHigh), Vector128.Create((ushort)255));
            Vector128.Narrow(low, high).AsUInt32().CopyTo(destination.Slice(i, VectorPixels));
        }

        return i;
    }

    /// <summary>Adds whole groups of four pixels; returns the number of pixels done.</summary>
    private static int AdditiveVector(ReadOnlySpan<uint> source, Span<uint> destination, uint opacity)
    {
        var colourOnly = Vector128.Create(0x00FFFFFFu).AsByte();
        var factor = Vector128.Create((ushort)opacity);
        int i = 0;
        for (; i <= source.Length - VectorPixels; i += VectorPixels)
        {
            Vector128<byte> s = Vector128.Create(source.Slice(i, VectorPixels)).AsByte() & colourOnly;
            if (opacity != 255)
            {
                s = ScaleBytes(s, factor);
            }

            (Vector128<ushort> sLow, Vector128<ushort> sHigh) = Vector128.Widen(s);
            (Vector128<ushort> dLow, Vector128<ushort> dHigh) = Vector128.Widen(Vector128.Create((ReadOnlySpan<uint>)destination.Slice(i, VectorPixels)).AsByte());
            Vector128<ushort> low = Vector128.Min(dLow + sLow, Vector128.Create((ushort)255));
            Vector128<ushort> high = Vector128.Min(dHigh + sHigh, Vector128.Create((ushort)255));
            Vector128.Narrow(low, high).AsUInt32().CopyTo(destination.Slice(i, VectorPixels));
        }

        return i;
    }

    private static Vector128<byte> ScaleBytes(Vector128<byte> pixels, Vector128<ushort> factor)
    {
        (Vector128<ushort> low, Vector128<ushort> high) = Vector128.Widen(pixels);
        return Vector128.Narrow(Div255(low * factor), Div255(high * factor));
    }

    private static Vector128<ushort> Div255(Vector128<ushort> x)
    {
        x += Vector128.Create((ushort)128);
        return Vector128.ShiftRightLogical(x + Vector128.ShiftRightLogical(x, 8), 8);
    }
}
