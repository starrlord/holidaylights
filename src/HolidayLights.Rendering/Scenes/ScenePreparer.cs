using System.Diagnostics;

namespace HolidayLights.Rendering.Scenes;

/// <summary>What the preparation does with the flash sequencer.</summary>
/// <param name="CreateNew">Create a sequencer for the scene's layout and flash options (they changed).</param>
/// <param name="StartStep">The step the new sequencer is moved to before it is handed over.</param>
internal readonly record struct SequencerRequest(bool CreateNew, long StartStep);

/// <summary>
/// Prepares scenes on the thread pool (CONTRACTS 4: no decoding or scaling on the Lights thread): resolves every placement's
/// bulb, scales each frame it can show and its glow halo through <see cref="ISpriteProvider"/>, and creates the flash
/// sequencer when needed. A bulb that cannot be prepared is skipped and logged; it never stops the other lights.
/// </summary>
internal sealed class ScenePreparer
{
    private const string LogSource = "Rendering";

    private readonly IBulbResolver bulbs;
    private readonly ISpriteProvider sprites;
    private readonly IFlashEngine flash;
    private readonly IAppLog log;

    /// <summary>Creates the preparer.</summary>
    public ScenePreparer(IBulbResolver bulbs, ISpriteProvider sprites, IFlashEngine flash, IAppLog log)
    {
        this.bulbs = bulbs;
        this.sprites = sprites;
        this.flash = flash;
        this.log = log;
    }

    /// <summary>Prepares a scene on the thread pool.</summary>
    public Task<PreparedScene> PrepareAsync(LightsScene scene, SequencerRequest sequencer, CancellationToken cancellationToken) =>
        Task.Run(() => Prepare(scene, sequencer, cancellationToken), cancellationToken);

    /// <summary>Prepares a scene on the calling thread.</summary>
    internal PreparedScene Prepare(LightsScene scene, SequencerRequest sequencer, CancellationToken cancellationToken)
    {
        LightsLayout layout = scene.Layout;
        var scales = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (DisplayLayout display in layout.Displays)
        {
            scales[display.Target.DisplayId] = display.Target.Scale;
        }

        Prefetch(scene, scales, cancellationToken);

        var prepared = new PreparedBulb?[layout.Placements.Count];
        var images = new Dictionary<SpriteId, PremultipliedImage>();
        var glows = new Dictionary<SpriteId, GlowSprite?>();
        int skipped = 0;
        foreach (BulbPlacement placement in layout.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                prepared[placement.Ordinal] = PrepareBulb(scene, placement, scales, images, glows);
                if (prepared[placement.Ordinal] is null)
                {
                    skipped++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (skipped++ == 0)
                {
                    log.Warn(LogSource, $"A bulb could not be prepared ({placement.BulbId}); it is left out.", exception);
                }
            }
        }

        IFlashSequencer? newSequencer = null;
        if (sequencer.CreateNew)
        {
            newSequencer = flash.CreateSequencer(layout, bulbs, scene.Flash);
            newSequencer.MoveTo(Math.Max(0, sequencer.StartStep), Stopwatch.GetTimestamp());
        }

        return new PreparedScene(scene, newSequencer, prepared, images, skipped);
    }

    private PreparedBulb? PrepareBulb(
        LightsScene scene,
        BulbPlacement placement,
        Dictionary<string, double> scales,
        Dictionary<SpriteId, PremultipliedImage> images,
        Dictionary<SpriteId, GlowSprite?> glows)
    {
        if (!scales.TryGetValue(placement.DisplayId, out double scale) || !bulbs.TryGetBulb(placement.BulbId, out IBulb? bulb))
        {
            return null;
        }

        SpriteStyle style = scene.Effects.Pixels;
        BulbAnimationInfo info = bulb.GetAnimation(placement.Slot, placement.Flavor);
        int flavor = ReducedFlavor(bulb, placement);
        var frames = new PlacedSprite[Math.Max(1, info.FrameCount)];
        for (int frame = 0; frame < frames.Length; frame++)
        {
            var id = new SpriteId(bulb.ContentKey, placement.Slot, flavor, frame, scale, style);
            if (!images.TryGetValue(id, out PremultipliedImage? image))
            {
                image = sprites.GetSprite(bulb, placement.Slot, flavor, frame, scale, style);
                images[id] = image;
            }

            frames[frame] = new PlacedSprite(id, SpriteAnchors.Position(placement.Slot, placement.Bounds, image.Size), image.Size);
        }

        PlacedSprite? glow = null;
        if (scene.Effects.Glow != GlowLevel.Off && info.Kind == BulbAnimationKind.LightBulb && info.LitFrame < frames.Length)
        {
            var glowId = new SpriteId(bulb.ContentKey, placement.Slot, flavor, -1, scale, SpriteStyle.Smooth);
            if (!glows.TryGetValue(glowId, out GlowSprite? halo))
            {
                halo = sprites.GetGlow(bulb, placement.Slot, flavor, scale);
                glows[glowId] = halo;
                if (halo is not null)
                {
                    images[glowId] = halo.Image;
                }
            }

            if (halo is not null)
            {
                PointI lit = frames[info.LitFrame].Position;
                glow = new PlacedSprite(glowId, new PointI(lit.X + halo.OffsetX, lit.Y + halo.OffsetY), halo.Image.Size);
            }
        }

        return new PreparedBulb(placement, info.Kind, info.LitFrame, frames, glow);
    }

    /// <summary>Asks the sprite provider to scale everything the scene shows in parallel before it is read one by one.</summary>
    private void Prefetch(LightsScene scene, Dictionary<string, double> scales, CancellationToken cancellationToken)
    {
        var requests = new List<SpriteRequest>();
        var seen = new HashSet<(string, CellSlot, int, double)>();
        foreach (BulbPlacement placement in scene.Layout.Placements)
        {
            if (!scales.TryGetValue(placement.DisplayId, out double scale) || !bulbs.TryGetBulb(placement.BulbId, out IBulb? bulb))
            {
                continue;
            }

            int flavor = ReducedFlavor(bulb, placement);
            if (!seen.Add((bulb.ContentKey, placement.Slot, flavor, scale)))
            {
                continue;
            }

            try
            {
                int frames = Math.Max(1, bulb.GetAnimation(placement.Slot, placement.Flavor).FrameCount);
                for (int frame = 0; frame < frames; frame++)
                {
                    requests.Add(new SpriteRequest(bulb, placement.Slot, flavor, frame, scale, scene.Effects.Pixels));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The same bulb is reported (once) when it is prepared.
            }
        }

        try
        {
            sprites.PrefetchAsync(requests, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.Write(AppLogLevel.Debug, LogSource, "Sprite prefetch failed; sprites are scaled one by one.", exception);
        }
    }

    private static int ReducedFlavor(IBulb bulb, BulbPlacement placement) =>
        placement.Slot.IsSide() ? placement.Flavor % Math.Max(1, bulb.GetFlavorCount(placement.Slot.ToSide())) : 0;
}
