namespace HolidayLights.Core.Imaging;

/// <summary>A decoded GIF: every frame at the logical screen size.</summary>
/// <param name="Size">The logical screen size.</param>
/// <param name="Frames">Frames with straight alpha (0 or 255); transparent pixels have colour 0.</param>
/// <param name="DelaysMilliseconds">Frame delays from the GCE (standard decoding); all 0 for classic decoding, which ignores delays (5.4).</param>
public sealed record GifAnimation(SizeI Size, IReadOnlyList<Rgba32Image> Frames, IReadOnlyList<int> DelaysMilliseconds);

/// <summary>GIF decoding in the two modes of ARCHITECTURE 4.1. Owner: core-bulbs.</summary>
/// <remarks>Every member is thread-safe; the returned images belong to the caller.</remarks>
public static class GifDecoder
{
    /// <summary>
    /// The faithful 5.4 decoder (port of 5.4's <c>Gif_DecodeToStrip</c>): holes where a frame uses the
    /// transparent index, persistent local colour tables, the transparent palette entry forced to black, GDI nearest-colour
    /// re-mapping of older frames, disposal 2/3 filling the whole slot, no clipping, stop at the first unexpected block byte.
    /// Must match 5.4 bit for bit (golden <c>bul-frames.json.gz</c>).
    /// </summary>
    /// <param name="gif">The GIF file bytes.</param>
    /// <param name="firstFrameOnly">Decode only frame 0 (5.4 GIF backgrounds of the screen saver).</param>
    /// <returns>The frames.</returns>
    /// <exception cref="InvalidDataException">The data cannot be decoded at all.</exception>
    public static GifAnimation DecodeClassic(ReadOnlySpan<byte> gif, bool firstFrameOnly = false)
    {
        ClassicGifStrip strip = ClassicGifReader.Decode(gif, firstFrameOnly);
        return new GifAnimation(new SizeI(strip.Width, strip.Height), strip.ToImages(), new int[strip.FrameCount]);
    }

    /// <summary>A spec-correct GIF decoder (disposal methods, transparency over the previous frame, delays) for importing GIFs and the Bulb Editing "GIF tip".</summary>
    /// <remarks>
    /// Compositing follows current viewers: the canvas starts transparent and disposal 2 clears to transparent; graphic
    /// control blocks and local colour tables apply to one image only. Delays are the file's values (centiseconds x 10);
    /// 0 means the file gives none.
    /// </remarks>
    /// <param name="gif">The GIF file bytes.</param>
    /// <returns>The frames.</returns>
    /// <exception cref="InvalidDataException">The data cannot be decoded.</exception>
    public static GifAnimation DecodeStandard(ReadOnlySpan<byte> gif) => StandardGifReader.Decode(gif);

    /// <summary>Reads the logical screen size from the header without decoding (layout at start-up).</summary>
    /// <param name="gif">At least the first 10 bytes of a GIF file.</param>
    /// <returns>The logical screen size.</returns>
    /// <exception cref="InvalidDataException">The data is not a GIF.</exception>
    public static SizeI ReadLogicalScreenSize(ReadOnlySpan<byte> gif)
    {
        if (gif.Length < 10 || !HasSignature(gif))
        {
            throw new InvalidDataException("The data is not a GIF.");
        }

        return new SizeI(gif[6] | gif[7] << 8, gif[8] | gif[9] << 8);
    }

    /// <summary>The 5.4 signature test: <c>GIF87a</c> or <c>GIF89a</c>, letters in either case.</summary>
    /// <param name="gif">At least 6 bytes.</param>
    /// <returns>True for a GIF signature.</returns>
    internal static bool HasSignature(ReadOnlySpan<byte> gif) =>
        gif.Length >= 6 && (gif[0] | 0x20) == 'g' && (gif[1] | 0x20) == 'i' && (gif[2] | 0x20) == 'f' && gif[3] == '8'
        && (gif[4] is (byte)'7' or (byte)'9') && (gif[5] | 0x20) == 'a';
}
