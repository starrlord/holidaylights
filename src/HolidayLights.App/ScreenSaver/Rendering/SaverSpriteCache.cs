using System.Collections.Concurrent;
using HolidayLights.App.ScreenSaver.Simulation;
using HolidayLights.Core.Sprites;

namespace HolidayLights.App.ScreenSaver.Rendering;

/// <summary>
/// The animation sprites scaled for a screen (PRODUCT-SPEC 5.3.3: Smooth = MMPX, Crisp = nearest neighbour; sized with
/// <see cref="ArtScale"/>), each scaled once per run. In small previews a sprite smaller than a pixel keeps only its share
/// of the pixel (area averaging), so a miniature does not turn fine snow into a blizzard.
/// </summary>
/// <remarks>Thread-safe. Returned images are shared: callers must not modify them.</remarks>
internal sealed class SaverSpriteCache
{
    private readonly ConcurrentDictionary<(SpriteArt Art, int Frame, double Scale, SpriteStyle Style), PremultipliedImage> images = new();

    /// <summary>Returns a frame scaled to <c>ArtScale.Scale(size, scale)</c> pixels.</summary>
    /// <param name="art">The strip.</param>
    /// <param name="frame">The frame (reduced modulo the frame count).</param>
    /// <param name="scale">Target pixels per DIP.</param>
    /// <param name="style">The Look's pixel style.</param>
    /// <returns>The premultiplied sprite.</returns>
    public PremultipliedImage Get(SpriteArt art, int frame, double scale, SpriteStyle style)
    {
        ArgumentNullException.ThrowIfNull(art);
        int index = ((frame % art.FrameCount) + art.FrameCount) % art.FrameCount;
        return images.GetOrAdd((art, index, scale, style), key => Scale(key.Art.Frames[key.Frame], key.Scale, key.Style));
    }

    private static PremultipliedImage Scale(Rgba32Image frame, double scale, SpriteStyle style)
    {
        PremultipliedImage sprite = PixelArtScaler.Scale(frame, scale, style);
        double coverage = Math.Min(1, frame.Width * scale / Math.Max(1, sprite.Width)) * Math.Min(1, frame.Height * scale / Math.Max(1, sprite.Height));
        if (coverage < 1)
        {
            uint factor = (uint)Math.Round(coverage * 256);
            uint[] pixels = sprite.Pixels;
            for (int i = 0; i < pixels.Length; i++)
            {
                uint pixel = pixels[i];
                pixels[i] = ((pixel >> 8 & 0x00FF00FF) * factor & 0xFF00FF00) | ((pixel & 0x00FF00FF) * factor >> 8 & 0x00FF00FF);
            }
        }

        return sprite;
    }
}

/// <summary>
/// Where the simulation's DIP positions land in target pixels. Smooth Motion interpolates between the last two steps and
/// rounds to the nearest pixel; the 2003 look shows whole DIPs truncated like 5.4's <c>(int)</c> casts.
/// </summary>
/// <param name="Scale">Target pixels per DIP.</param>
/// <param name="Smooth">True for Smooth Motion.</param>
internal readonly record struct SaverPixels(double Scale, bool Smooth)
{
    /// <summary>The target pixel of a position.</summary>
    /// <param name="previous">The position before the step (DIPs).</param>
    /// <param name="current">The position after the step (DIPs).</param>
    /// <param name="alpha">How far the display is between the two (0-1).</param>
    /// <returns>The pixel coordinate.</returns>
    public int Position(float previous, float current, float alpha)
    {
        double dip = Smooth ? previous + (current - previous) * (double)alpha : Math.Truncate(current);
        return Snap(dip);
    }

    /// <summary>The target pixel of a DIP coordinate: <c>floor(dip x scale + 0.5)</c>.</summary>
    /// <param name="dip">The coordinate.</param>
    /// <returns>The pixel coordinate.</returns>
    public int Snap(double dip) => (int)Math.Floor(dip * Scale + 0.5);

    /// <summary>The pixel rectangle of a DIP rectangle (edges snapped independently, so neighbours tile without gaps).</summary>
    /// <param name="rect">The rectangle in DIPs.</param>
    /// <returns>The rectangle in target pixels.</returns>
    public RectI Snap(RectI rect) => new(Snap(rect.Left), Snap(rect.Top), Snap(rect.Right), Snap(rect.Bottom));
}
