namespace HolidayLights.Core.Abstractions;

/// <summary>
/// Packing helpers for the 32-bit BGRA pixels used by <see cref="Rgba32Image"/> and <see cref="PremultipliedImage"/>.
/// </summary>
/// <remarks>
/// A pixel is a <see cref="uint"/> laid out as <c>0xAARRGGBB</c>, which is the byte order B, G, R, A in memory on a
/// little-endian machine (DXGI <c>B8G8R8A8</c>, WPF <c>Bgra32</c>/<c>Pbgra32</c>).
/// </remarks>
public static class Bgra32
{
    /// <summary>The fully transparent pixel (all channels zero).</summary>
    public const uint Transparent = 0;

    /// <summary>Packs four channels into one pixel.</summary>
    /// <param name="r">Red.</param>
    /// <param name="g">Green.</param>
    /// <param name="b">Blue.</param>
    /// <param name="a">Alpha (255 = opaque).</param>
    /// <returns>The packed pixel.</returns>
    public static uint Pack(byte r, byte g, byte b, byte a) => (uint)(a << 24 | r << 16 | g << 8 | b);

    /// <summary>Returns the alpha channel.</summary>
    /// <param name="pixel">A packed pixel.</param>
    /// <returns>Alpha (0-255).</returns>
    public static byte A(uint pixel) => (byte)(pixel >> 24);

    /// <summary>Returns the red channel.</summary>
    /// <param name="pixel">A packed pixel.</param>
    /// <returns>Red (0-255).</returns>
    public static byte R(uint pixel) => (byte)(pixel >> 16);

    /// <summary>Returns the green channel.</summary>
    /// <param name="pixel">A packed pixel.</param>
    /// <returns>Green (0-255).</returns>
    public static byte G(uint pixel) => (byte)(pixel >> 8);

    /// <summary>Returns the blue channel.</summary>
    /// <param name="pixel">A packed pixel.</param>
    /// <returns>Blue (0-255).</returns>
    public static byte B(uint pixel) => (byte)pixel;

    /// <summary>Converts a straight-alpha pixel to premultiplied alpha.</summary>
    /// <remarks>Each colour channel becomes <c>(c * a + 127) / 255</c>; a transparent pixel becomes <see cref="Transparent"/>.</remarks>
    /// <param name="straight">A straight-alpha pixel.</param>
    /// <returns>The premultiplied pixel.</returns>
    public static uint Premultiply(uint straight)
    {
        uint a = straight >> 24;
        if (a == 255)
        {
            return straight;
        }

        if (a == 0)
        {
            return Transparent;
        }

        uint r = (((straight >> 16) & 0xFF) * a + 127) / 255;
        uint g = (((straight >> 8) & 0xFF) * a + 127) / 255;
        uint b = ((straight & 0xFF) * a + 127) / 255;
        return a << 24 | r << 16 | g << 8 | b;
    }

    /// <summary>Converts a premultiplied pixel back to straight alpha.</summary>
    /// <remarks>
    /// Each colour channel becomes <c>min(255, (c * 255 + a / 2) / a)</c>. A pixel with alpha 0 (including additive
    /// light, whose colour channels may be non-zero) becomes <see cref="Transparent"/>.
    /// </remarks>
    /// <param name="premultiplied">A premultiplied pixel.</param>
    /// <returns>The straight-alpha pixel.</returns>
    public static uint Unpremultiply(uint premultiplied)
    {
        uint a = premultiplied >> 24;
        if (a == 255)
        {
            return premultiplied;
        }

        if (a == 0)
        {
            return Transparent;
        }

        uint r = Math.Min(255u, (((premultiplied >> 16) & 0xFF) * 255 + a / 2) / a);
        uint g = Math.Min(255u, (((premultiplied >> 8) & 0xFF) * 255 + a / 2) / a);
        uint b = Math.Min(255u, ((premultiplied & 0xFF) * 255 + a / 2) / a);
        return a << 24 | r << 16 | g << 8 | b;
    }
}
