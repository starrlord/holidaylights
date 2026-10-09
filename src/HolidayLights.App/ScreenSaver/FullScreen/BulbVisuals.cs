using HolidayLights.App.ScreenSaver.Scene;
using Vortice.DirectComposition;

namespace HolidayLights.App.ScreenSaver.FullScreen;

/// <summary>
/// The bulbs around one saver display as composition visuals, drawn like the desktop (PRODUCT-SPEC 5.8, 5.9): per light
/// bulb an unlit visual, the lit visual above it at opacity b, and its glow beneath all bulbs at opacity b x intensity;
/// animation bulbs switch their picture at step boundaries. Only what changed is touched.
/// </summary>
/// <remarks>Positions are physical pixels of the display. Saver UI thread.</remarks>
internal sealed class BulbVisuals : IDisposable
{
    private readonly SaverCompositor compositor;
    private readonly List<IDisposable> owned = [];
    private readonly Item[] items;

    /// <summary>Creates the visuals of a scene's bulbs.</summary>
    /// <param name="compositor">The devices.</param>
    /// <param name="bulbs">The scene's bulbs.</param>
    /// <param name="display">The display (its top-left is the origin).</param>
    /// <param name="resolver">Resolves the bulbs.</param>
    /// <param name="sprites">Bulb sprites and glow.</param>
    /// <param name="style">The Look's pixel style.</param>
    /// <param name="glowIntensity">The glow intensity (0 = none).</param>
    public BulbVisuals(
        SaverCompositor compositor, SaverBulbs bulbs, DisplayInfo display, IBulbResolver resolver, ISpriteProvider sprites, SpriteStyle style, float glowIntensity)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(bulbs);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(sprites);
        this.compositor = compositor;
        Glow = Own(compositor.CreateVisual());
        Bulbs = Own(compositor.CreateVisual());
        var created = new List<Item>(bulbs.Layout.Placements.Count);
        foreach (BulbPlacement placement in bulbs.Layout.Placements)
        {
            if (resolver.TryGetBulb(placement.BulbId, out IBulb? bulb))
            {
                var art = new BulbArt(bulb, placement, sprites, style, bulbs.Scale);
                created.Add(Create(art, placement.Bounds.Left - display.Bounds.Left, placement.Bounds.Top - display.Bounds.Top, glowIntensity));
            }
        }

        items = [.. created];
    }

    /// <summary>The glow of every light bulb (beneath <see cref="Bulbs"/>).</summary>
    public IDCompositionVisual Glow { get; }

    /// <summary>The bulbs.</summary>
    public IDCompositionVisual Bulbs { get; }

    /// <summary>Shows the bulbs' states.</summary>
    /// <param name="states">One state per placement ordinal.</param>
    /// <param name="glowIntensity">The glow intensity.</param>
    public void Update(IReadOnlyList<BulbVisualState> states, float glowIntensity)
    {
        ArgumentNullException.ThrowIfNull(states);
        foreach (Item item in items)
        {
            item.Show(states[item.Art.Ordinal], glowIntensity, compositor);
        }
    }

    /// <summary>Releases the visuals and effects.</summary>
    public void Dispose()
    {
        foreach (IDisposable resource in owned)
        {
            resource.Dispose();
        }

        owned.Clear();
    }

    private Item Create(BulbArt art, int x, int y, float glowIntensity)
    {
        if (art.Animation.Kind != BulbAnimationKind.LightBulb)
        {
            Faded frame = Fade(compositor.CreateVisual(x, y, null), 1);
            SaverCompositor.Append(Bulbs, frame.Visual);
            return new Item(art, frame, null, null);
        }

        IDCompositionVisual unlit = Own(compositor.CreateVisual(x, y, compositor.GetSurface(art.Sprite(art.Animation.UnlitFrame))));
        Faded lit = Fade(compositor.CreateVisual(x, y, compositor.GetSurface(art.Sprite(art.Animation.LitFrame))), 0);
        SaverCompositor.Append(Bulbs, unlit);
        SaverCompositor.Append(Bulbs, lit.Visual);
        Faded? glow = null;
        if (glowIntensity > 0 && art.Halo() is { } halo && compositor.GetSurface(halo.Image) is { } surface)
        {
            glow = Fade(compositor.CreateVisual(x + halo.OffsetX, y + halo.OffsetY, surface), 0);
            SaverCompositor.Append(Glow, glow.Visual);
        }

        return new Item(art, null, lit, glow);
    }

    private Faded Fade(IDCompositionVisual visual, float opacity)
    {
        IDCompositionEffectGroup effect = Own(compositor.CreateOpacity(opacity));
        visual.SetEffect(effect);
        return new Faded(Own(visual), effect, opacity);
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        owned.Add(resource);
        return resource;
    }

    /// <summary>A visual with its opacity effect and the opacity it shows.</summary>
    private sealed class Faded(IDCompositionVisual visual, IDCompositionEffectGroup effect, float opacity)
    {
        private float shown = opacity;

        public IDCompositionVisual Visual => visual;

        public void Show(float value)
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            if (clamped != shown)
            {
                shown = clamped;
                effect.SetOpacity(clamped);
            }
        }
    }

    /// <summary>What a placement draws: its bulb, slot and flavor at the display's scale.</summary>
    private sealed class BulbArt(IBulb bulb, BulbPlacement placement, ISpriteProvider sprites, SpriteStyle style, double scale)
    {
        public int Ordinal => placement.Ordinal;

        public BulbAnimationInfo Animation { get; } = bulb.GetAnimation(placement.Slot, placement.Flavor);

        public PremultipliedImage Sprite(int frame) => sprites.GetSprite(bulb, placement.Slot, placement.Flavor, frame, scale, style);

        public GlowSprite? Halo() => sprites.GetGlow(bulb, placement.Slot, placement.Flavor, scale);
    }

    /// <summary>
    /// The visuals of one placement: a light bulb's lit picture and glow (above its unlit picture), or another bulb's
    /// current frame at its brightness.
    /// </summary>
    private sealed class Item(BulbArt art, Faded? frameVisual, Faded? lit, Faded? glow)
    {
        private int frame = -1;

        public BulbArt Art => art;

        public void Show(BulbVisualState state, float glowIntensity, SaverCompositor compositor)
        {
            if (lit is not null)
            {
                lit.Show(state.Brightness);
                glow?.Show(state.Glow * glowIntensity);
                return;
            }

            int shown = art.Animation.FrameCount <= 0 ? 0 : Math.Max(0, state.Frame) % art.Animation.FrameCount;
            if (shown != frame && frameVisual is not null)
            {
                frame = shown;
                if (compositor.GetSurface(art.Sprite(shown)) is { } surface)
                {
                    frameVisual.Visual.SetContent(surface);
                }
            }

            frameVisual?.Show(state.Brightness);
        }
    }
}
