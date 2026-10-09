namespace HolidayLights.Core.Sprites;

/// <summary>Less common choices of <see cref="PixelArtScaler"/>.</summary>
public sealed record PixelArtScaleOptions
{
    /// <summary>The options used for bulb sprites: MMPX clamps at the frame edge, no alpha threshold.</summary>
    public static PixelArtScaleOptions Default { get; } = new();

    /// <summary>How MMPX reads pixels outside the image (Smooth only). Bulb frames clamp so cords continue into neighbours.</summary>
    public MmpxEdge Edge { get; init; } = MmpxEdge.Clamp;

    /// <summary>
    /// Null (default) keeps the anti-aliased alpha of area averaging. A value makes every pixel whose alpha reaches it
    /// opaque (with its un-premultiplied colour) and every other pixel transparent: a crisp 1-bit edge for colour-key
    /// drawing (128 is the 50 % threshold).
    /// </summary>
    public byte? AlphaThreshold { get; init; }
}
