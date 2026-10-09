using HolidayLights.Core.Imaging;

namespace HolidayLights.Core.Bulbs;

/// <summary>What the faithful decoder makes of one GIF entry: its size, frame count and kind, or that it is damaged.</summary>
/// <param name="Width">Frame width (the GIF logical screen; 32 for damaged art).</param>
/// <param name="Height">Frame height (32 for damaged art).</param>
/// <param name="FrameCount">Frames (1 for damaged art).</param>
/// <param name="Kind">Static, light bulb or animation (PRODUCT-SPEC 5.4).</param>
/// <param name="LitFrame">The lit frame of a light bulb, else 0.</param>
/// <param name="IsDamaged">True when the original decoder rejects the GIF (it is drawn as the WARNING picture).</param>
internal sealed record AnimationFacts(int Width, int Height, int FrameCount, BulbAnimationKind Kind, int LitFrame, bool IsDamaged)
{
    /// <summary>The facts of an undecodable entry: the 32 x 32 WARNING picture.</summary>
    public static AnimationFacts Damaged { get; } = new(32, 32, 1, BulbAnimationKind.Static, 0, true);

    /// <summary>The frame size.</summary>
    public SizeI Size => new(Width, Height);

    /// <summary>Decodes a GIF entry with the faithful decoder and classifies it, keeping only what classification needs.</summary>
    /// <param name="gif">The GIF bytes (empty for a missing entry).</param>
    /// <returns>The facts.</returns>
    public static AnimationFacts Analyze(ReadOnlySpan<byte> gif) => Analyze(gif, keepFrames: false, out _);

    /// <summary>Decodes a GIF entry with the faithful decoder and classifies it.</summary>
    /// <param name="gif">The GIF bytes (empty for a missing entry).</param>
    /// <param name="keepFrames">Convert every frame to pixels and return them.</param>
    /// <param name="frames">The decoded frames when <paramref name="keepFrames"/> is set and the entry decodes, else null.</param>
    /// <returns>The facts.</returns>
    public static AnimationFacts Analyze(ReadOnlySpan<byte> gif, bool keepFrames, out Rgba32Image[]? frames)
    {
        frames = null;
        if (gif.IsEmpty)
        {
            return Damaged;
        }

        ClassicGifStrip strip;
        try
        {
            strip = ClassicGifReader.Decode(gif, firstFrameOnly: false);
        }
        catch (InvalidDataException)
        {
            return Damaged;
        }

        if (keepFrames)
        {
            frames = strip.ToImages();
        }

        if (strip.FrameCount == 1)
        {
            return new AnimationFacts(strip.Width, strip.Height, 1, BulbAnimationKind.Static, 0, false);
        }

        int litFrame = 0;
        bool lightBulb = strip.FrameCount == 2
            && LightBulbClassifier.TryClassify(frames?[0] ?? strip.ToImage(0), frames?[1] ?? strip.ToImage(1), out litFrame);
        return lightBulb
            ? new AnimationFacts(strip.Width, strip.Height, 2, BulbAnimationKind.LightBulb, litFrame, false)
            : new AnimationFacts(strip.Width, strip.Height, strip.FrameCount, BulbAnimationKind.Animation, 0, false);
    }

    /// <summary>The facts as the <see cref="IBulb"/> API reports them.</summary>
    /// <returns>The animation info.</returns>
    public BulbAnimationInfo ToInfo() => new(Kind, FrameCount, LitFrame, Size);
}
