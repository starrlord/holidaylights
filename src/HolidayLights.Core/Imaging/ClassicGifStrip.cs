namespace HolidayLights.Core.Imaging;

/// <summary>
/// The result of the faithful 5.4 decoder before conversion to pixels: the frames side by side as they would sit in
/// the original's strip bitmaps (8-bit colour indices and a 1-bit mask per frame slot) plus the colour table of the final
/// strip, which every earlier frame was translated into.
/// </summary>
internal sealed class ClassicGifStrip
{
    private readonly IReadOnlyList<byte[]> colorSlots;
    private readonly IReadOnlyList<byte[]> maskSlots;
    private readonly uint[] palette;

    /// <summary>Creates the strip.</summary>
    /// <param name="width">Logical screen width (= frame width).</param>
    /// <param name="height">Logical screen height (= frame height).</param>
    /// <param name="colorSlots">Per frame, <c>width * height</c> colour indices into <paramref name="palette"/>.</param>
    /// <param name="maskSlots">Per frame, <c>width * height</c> mask values (1 = transparent).</param>
    /// <param name="palette">The final 256-entry colour table as <c>0x00RRGGBB</c>.</param>
    public ClassicGifStrip(int width, int height, IReadOnlyList<byte[]> colorSlots, IReadOnlyList<byte[]> maskSlots, uint[] palette)
    {
        Width = width;
        Height = height;
        this.colorSlots = colorSlots;
        this.maskSlots = maskSlots;
        this.palette = palette;
    }

    /// <summary>Frame width.</summary>
    public int Width { get; }

    /// <summary>Frame height.</summary>
    public int Height { get; }

    /// <summary>Number of decoded frames (the value the original decoder returns).</summary>
    public int FrameCount => colorSlots.Count;

    /// <summary>Converts one frame to straight-alpha pixels: alpha 0 and colour 0 where the mask is set, else opaque.</summary>
    /// <param name="frame">The frame (0-based).</param>
    /// <returns>A new image.</returns>
    public Rgba32Image ToImage(int frame)
    {
        byte[] colors = colorSlots[frame];
        byte[] mask = maskSlots[frame];
        var pixels = new uint[colors.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            if (mask[i] == 0)
            {
                pixels[i] = 0xFF000000 | palette[colors[i]];
            }
        }

        return new Rgba32Image(Width, Height, pixels);
    }

    /// <summary>Converts every frame.</summary>
    /// <returns>The frames in order.</returns>
    public Rgba32Image[] ToImages()
    {
        var frames = new Rgba32Image[FrameCount];
        for (int k = 0; k < frames.Length; k++)
        {
            frames[k] = ToImage(k);
        }

        return frames;
    }
}
