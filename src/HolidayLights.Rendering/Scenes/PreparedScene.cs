namespace HolidayLights.Rendering.Scenes;

/// <summary>Identifies one uploaded picture: a scaled bulb frame or a glow halo.</summary>
/// <param name="ContentKey">The bulb's <see cref="IBulb.ContentKey"/>.</param>
/// <param name="Slot">The art slot.</param>
/// <param name="Flavor">The flavor reduced modulo the side's flavor count (0 for corners).</param>
/// <param name="Frame">The frame (-1 for a glow halo).</param>
/// <param name="Scale">S.</param>
/// <param name="Style">Smooth or Crisp (glow halos use Smooth).</param>
internal readonly record struct SpriteId(string ContentKey, CellSlot Slot, int Flavor, int Frame, double Scale, SpriteStyle Style);

/// <summary>A picture placed on the screen: which sprite and where its top-left lies (virtual-screen physical pixels).</summary>
internal readonly record struct PlacedSprite(SpriteId Id, PointI Position, SizeI Size);

/// <summary>Everything the Lights thread needs to draw one bulb placement.</summary>
/// <param name="Placement">The placement.</param>
/// <param name="Kind">Static, light bulb or animation.</param>
/// <param name="LitFrame">The lit frame of a light bulb (0 or 1).</param>
/// <param name="Frames">One sprite per frame.</param>
/// <param name="Glow">The glow halo of a light bulb (null without glow).</param>
internal sealed record PreparedBulb(BulbPlacement Placement, BulbAnimationKind Kind, int LitFrame, IReadOnlyList<PlacedSprite> Frames, PlacedSprite? Glow)
{
    /// <summary>The unlit frame of a light bulb (the frame shown underneath the lit visual).</summary>
    public int UnlitFrame => Kind == BulbAnimationKind.LightBulb ? 1 - LitFrame : LitFrame;
}

/// <summary>
/// A scene made ready on the thread pool: the per-bulb sprites (already scaled), their pixels, and a new flash sequencer when
/// the layout or flash options changed. Handed to the Lights thread, which only uploads and composes.
/// </summary>
internal sealed class PreparedScene
{
    /// <summary>Creates the prepared scene.</summary>
    public PreparedScene(LightsScene scene, IFlashSequencer? newSequencer, IReadOnlyList<PreparedBulb?> bulbs, IReadOnlyDictionary<SpriteId, PremultipliedImage> images, int skippedBulbs)
    {
        Scene = scene;
        NewSequencer = newSequencer;
        Bulbs = bulbs;
        Images = images;
        SkippedBulbs = skippedBulbs;
    }

    /// <summary>The scene.</summary>
    public LightsScene Scene { get; }

    /// <summary>A sequencer for the new layout or flash options, already moved near the current step; null to keep the current one.</summary>
    public IFlashSequencer? NewSequencer { get; }

    /// <summary>One entry per placement ordinal (null when the bulb could not be resolved or drawn).</summary>
    public IReadOnlyList<PreparedBulb?> Bulbs { get; }

    /// <summary>The pixels of every sprite the scene uses.</summary>
    public IReadOnlyDictionary<SpriteId, PremultipliedImage> Images { get; }

    /// <summary>Placements that could not be prepared (missing or damaged bulbs).</summary>
    public int SkippedBulbs { get; }

    /// <summary>The prepared bulbs drawn on one display.</summary>
    public IEnumerable<PreparedBulb> BulbsOn(string displayId)
    {
        foreach (PreparedBulb? bulb in Bulbs)
        {
            if (bulb is not null && string.Equals(bulb.Placement.DisplayId, displayId, StringComparison.Ordinal))
            {
                yield return bulb;
            }
        }
    }
}
