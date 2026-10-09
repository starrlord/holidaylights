namespace HolidayLights.Core.Abstractions;

/// <summary>
/// A 32-bit BGRA pixel buffer (row-major, top-down, no padding): <c>Pixels[y * Width + x]</c>.
/// </summary>
/// <remarks>
/// Base of <see cref="Rgba32Image"/> (straight alpha, produced by decoders) and <see cref="PremultipliedImage"/>
/// (premultiplied alpha, consumed by renderers). Buffers are mutable and not thread-safe; share them between threads
/// only after they have been completely written.
/// </remarks>
public abstract class BgraPixelBuffer
{
    /// <summary>Creates a buffer that takes ownership of <paramref name="pixels"/>.</summary>
    /// <param name="width">Width in pixels (zero or more).</param>
    /// <param name="height">Height in pixels (zero or more).</param>
    /// <param name="pixels">Exactly <c>width * height</c> packed pixels (see <see cref="Bgra32"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    /// <exception cref="ArgumentException">The array length does not match the dimensions.</exception>
    protected BgraPixelBuffer(int width, int height, uint[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != (long)width * height)
        {
            throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The packed pixels, row-major and top-down.</summary>
    public uint[] Pixels { get; }

    /// <summary>The size of the buffer.</summary>
    public SizeI Size => new(Width, Height);

    /// <summary>Gets or sets one pixel.</summary>
    /// <param name="x">Column (0 to <see cref="Width"/> - 1).</param>
    /// <param name="y">Row (0 to <see cref="Height"/> - 1).</param>
    public uint this[int x, int y]
    {
        get => Pixels[CheckedIndex(x, y)];
        set => Pixels[CheckedIndex(x, y)] = value;
    }

    /// <summary>Returns one row of pixels.</summary>
    /// <param name="y">Row (0 to <see cref="Height"/> - 1).</param>
    /// <returns>A span over the row.</returns>
    public Span<uint> GetRow(int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        return Pixels.AsSpan(y * Width, Width);
    }

    /// <summary>Copies a rectangle of this buffer into a new pixel array.</summary>
    /// <param name="area">The rectangle; it must lie inside the buffer.</param>
    /// <returns>The copied pixels, row-major.</returns>
    protected uint[] CopyArea(RectI area)
    {
        if (area.Left < 0 || area.Top < 0 || area.Right > Width || area.Bottom > Height || area.Width < 0 || area.Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(area), $"{area} is outside the {Width} x {Height} image.");
        }

        var result = new uint[area.Width * area.Height];
        for (int row = 0; row < area.Height; row++)
        {
            Pixels.AsSpan((area.Top + row) * Width + area.Left, area.Width).CopyTo(result.AsSpan(row * area.Width));
        }

        return result;
    }

    /// <summary>Allocates a zeroed (fully transparent) pixel array after validating the dimensions.</summary>
    /// <param name="width">Width in pixels (zero or more).</param>
    /// <param name="height">Height in pixels (zero or more).</param>
    /// <returns>A new array of <c>width * height</c> pixels.</returns>
    protected static uint[] Allocate(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        return new uint[checked(width * height)];
    }

    private int CheckedIndex(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside the {Width} x {Height} image.");
        }

        return y * Width + x;
    }
}

/// <summary>
/// A decoded picture with <b>straight</b> (non-premultiplied) alpha in BGRA order. Decoders (BMP, GIF, the built-in
/// bulb sheets) produce it.
/// </summary>
/// <remarks>
/// Convention for bulb art: alpha is 0 or 255 (5.4 masks); transparent pixels have their colour channels set to 0, which
/// is also what the golden RGBA hashes expect (<c>tests/HolidayLights.Tests/Golden/README.md</c>).
/// </remarks>
public sealed class Rgba32Image : BgraPixelBuffer
{
    /// <summary>Creates a fully transparent image.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public Rgba32Image(int width, int height)
        : base(width, height, Allocate(width, height))
    {
    }

    /// <summary>Creates an image that takes ownership of <paramref name="pixels"/>.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="pixels">Exactly <c>width * height</c> straight-alpha BGRA pixels.</param>
    public Rgba32Image(int width, int height, uint[] pixels)
        : base(width, height, pixels)
    {
    }

    /// <summary>Returns a deep copy.</summary>
    /// <returns>A new image with copied pixels.</returns>
    public Rgba32Image Clone() => new(Width, Height, (uint[])Pixels.Clone());

    /// <summary>Copies a rectangle into a new image.</summary>
    /// <param name="area">The rectangle; it must lie inside the image.</param>
    /// <returns>The cropped image.</returns>
    public Rgba32Image Crop(RectI area) => new(area.Width, area.Height, CopyArea(area));

    /// <summary>Converts to premultiplied alpha with <see cref="Bgra32.Premultiply"/>.</summary>
    /// <returns>A new premultiplied image.</returns>
    public PremultipliedImage ToPremultiplied()
    {
        var result = new uint[Pixels.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Bgra32.Premultiply(Pixels[i]);
        }

        return new PremultipliedImage(Width, Height, result);
    }
}

/// <summary>
/// A picture with <b>premultiplied</b> alpha in BGRA order: what DirectComposition surfaces, the CPU compositor and WPF
/// <c>Pbgra32</c> bitmaps consume.
/// </summary>
/// <remarks>
/// Colour channels may exceed alpha. Such pixels add light: composited as <c>out = src + dst * (1 - a)</c>, a pixel with
/// alpha 0 and non-zero colour is purely additive. Glow sprites use this (PRODUCT-SPEC 5.9;
/// DWM composes layered windows this way).
/// </remarks>
public sealed class PremultipliedImage : BgraPixelBuffer
{
    /// <summary>Creates a fully transparent image.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public PremultipliedImage(int width, int height)
        : base(width, height, Allocate(width, height))
    {
    }

    /// <summary>Creates an image that takes ownership of <paramref name="pixels"/>.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="pixels">Exactly <c>width * height</c> premultiplied BGRA pixels.</param>
    public PremultipliedImage(int width, int height, uint[] pixels)
        : base(width, height, pixels)
    {
    }

    /// <summary>Returns a deep copy.</summary>
    /// <returns>A new image with copied pixels.</returns>
    public PremultipliedImage Clone() => new(Width, Height, (uint[])Pixels.Clone());

    /// <summary>Copies a rectangle into a new image.</summary>
    /// <param name="area">The rectangle; it must lie inside the image.</param>
    /// <returns>The cropped image.</returns>
    public PremultipliedImage Crop(RectI area) => new(area.Width, area.Height, CopyArea(area));
}
