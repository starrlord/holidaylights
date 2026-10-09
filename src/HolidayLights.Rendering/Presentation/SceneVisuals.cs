using System.Diagnostics;
using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Scenes;
using Vortice.DirectComposition;

namespace HolidayLights.Rendering.Presentation;

/// <summary>
/// The bulbs of one scene on one display: a scene root (its own opacity for theme transitions and the power-up's fade-in)
/// with a glow layer beneath a bulb layer. Bulbs are keyed by <see cref="PlacementKey"/> so an edit keeps unchanged bulbs.
/// </summary>
internal sealed class SceneVisuals : IDisposable
{
    private readonly GroupOpacity opacity;
    private readonly IDCompositionVisual glowLayer;
    private readonly GroupOpacity glowOpacity;
    private readonly IDCompositionVisual bulbLayer;
    private readonly Dictionary<PlacementKey, BulbVisuals> bulbs = [];
    private readonly List<(BulbVisuals Bulb, long RemoveAt)> leaving = [];
    private readonly HashSet<BulbVisuals> fading = [];

    /// <summary>Creates an empty scene root.</summary>
    public SceneVisuals(CompositionDevice device, PointI origin)
    {
        Origin = origin;
        Root = device.CreateVisual();
        opacity = new GroupOpacity(device, Root);
        glowLayer = device.CreateVisual();
        glowOpacity = new GroupOpacity(device, glowLayer);
        bulbLayer = device.CreateVisual();
        Root.AddInFront(glowLayer);
        Root.AddInFrontOf(bulbLayer, glowLayer);
    }

    /// <summary>The factor on every glow of the display (half on a display in HDR, else 1).</summary>
    public float GlowScale { get; private set; } = 1f;

    /// <summary>Scales every glow of the display (1 composes the glow layer directly, without an effect).</summary>
    public void SetGlowScale(float scale, AnimationBatch batch)
    {
        if (scale == GlowScale)
        {
            return;
        }

        GlowScale = scale;
        glowOpacity.Set(OpacityCurve.Constant(scale), batch);
    }

    /// <summary>The scene root (a child of the display's window root).</summary>
    public IDCompositionVisual Root { get; }

    /// <summary>The window origin the bulb offsets are relative to.</summary>
    public PointI Origin { get; }

    /// <summary>The bulbs by placement key.</summary>
    public IReadOnlyDictionary<PlacementKey, BulbVisuals> Bulbs => bulbs;

    /// <summary>The scene opacity.</summary>
    public GroupOpacity Opacity => opacity;

    /// <summary>Visuals in use (diagnostics).</summary>
    public int VisualCount => 3 + bulbs.Values.Sum(b => b.VisualCount) + leaving.Sum(l => l.Bulb.VisualCount);

    /// <summary>Adds a bulb.</summary>
    public BulbVisuals Add(PreparedBulb bulb, VisualContext context)
    {
        var visuals = BulbVisuals.Create(bulb, bulbLayer, glowLayer, Origin, context, started => fading.Add(started));
        bulbs[PlacementKey.Of(bulb.Placement)] = visuals;
        return visuals;
    }

    /// <summary>Takes a bulb out of the scene; it fades out and is released when the fade has ended.</summary>
    public void Remove(PlacementKey key, long now, double fadeMilliseconds, VisualContext context)
    {
        if (!bulbs.Remove(key, out BulbVisuals? visuals))
        {
            return;
        }

        fading.Remove(visuals);
        if (fadeMilliseconds <= 0)
        {
            visuals.Dispose();
            return;
        }

        visuals.FadeOut(now, fadeMilliseconds, context);
        fading.Remove(visuals);
        leaving.Add((visuals, now + (long)(fadeMilliseconds * Stopwatch.Frequency / 1000)));
    }

    /// <summary>Settles finished fades (scene and bulbs) and releases bulbs whose fade-out has ended.</summary>
    /// <param name="now">The current timestamp.</param>
    /// <param name="batch">The animation batch.</param>
    /// <param name="changed">Set when something changed that needs a commit.</param>
    /// <returns>The earliest pending time, or null.</returns>
    public long? Tick(long now, AnimationBatch batch, ref bool changed)
    {
        changed |= opacity.SettleIfDue(now, batch);
        long? next = opacity.SettleAt;
        foreach (BulbVisuals bulb in fading.ToList())
        {
            if (bulb.Opacity.SettleIfDue(now, batch))
            {
                changed = true;
            }

            if (bulb.Opacity.SettleAt is { } at)
            {
                next = next is { } pending ? Math.Min(pending, at) : at;
            }
            else
            {
                fading.Remove(bulb);
            }
        }

        for (int i = leaving.Count - 1; i >= 0; i--)
        {
            if (leaving[i].RemoveAt <= now)
            {
                leaving[i].Bulb.Dispose();
                leaving.RemoveAt(i);
                changed = true;
            }
            else
            {
                next = next is { } pending ? Math.Min(pending, leaving[i].RemoveAt) : leaving[i].RemoveAt;
            }
        }

        return next;
    }

    /// <summary>The sprites the scene's bulbs use (to keep their surfaces).</summary>
    public void CollectSprites(HashSet<SpriteId> live)
    {
        foreach (BulbVisuals bulb in bulbs.Values.Concat(leaving.Select(l => l.Bulb)))
        {
            foreach (PlacedSprite frame in bulb.Bulb.Frames)
            {
                live.Add(frame.Id);
            }

            if (bulb.Bulb.Glow is { } glow)
            {
                live.Add(glow.Id);
            }
        }
    }

    /// <summary>Releases every visual.</summary>
    public void Dispose()
    {
        foreach (BulbVisuals bulb in bulbs.Values)
        {
            bulb.Dispose();
        }

        foreach ((BulbVisuals bulb, _) in leaving)
        {
            bulb.Dispose();
        }

        bulbs.Clear();
        leaving.Clear();
        fading.Clear();
        opacity.Dispose();
        glowOpacity.Dispose();
        Root.RemoveAllVisuals();
        glowLayer.Dispose();
        bulbLayer.Dispose();
        Root.Dispose();
    }
}
