using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Scenes;
using Vortice.DirectComposition;

namespace HolidayLights.Rendering.Presentation;

/// <summary>What a bulb's visuals need from their surroundings.</summary>
/// <param name="Device">The composition device.</param>
/// <param name="Surfaces">The uploaded sprites.</param>
/// <param name="Images">The pixels of the current prepared scene (uploaded on first use).</param>
/// <param name="Batch">The animations of this iteration.</param>
internal sealed record VisualContext(CompositionDevice Device, SurfaceCache Surfaces, IReadOnlyDictionary<SpriteId, PremultipliedImage> Images, AnimationBatch Batch);

/// <summary>
/// The visuals of one placed bulb (PRODUCT-SPEC 5.8): a container at the bulb position holding the base visual (the unlit frame
/// of a light bulb, or the current frame of other bulbs) and, for light bulbs, the lit visual above it at opacity <c>b</c>;
/// the glow visual lives in the display's glow layer beneath all bulbs at opacity <c>b x intensity</c>. The lit and glow
/// opacities are effect groups; the container gets one only while the whole bulb fades.
/// </summary>
internal sealed class BulbVisuals : IDisposable
{
    private readonly IDCompositionVisual bulbLayer;
    private readonly IDCompositionVisual glowLayer;
    private readonly PointI origin;
    private readonly IDCompositionVisual container;
    private readonly IDCompositionVisual baseVisual;
    private readonly GroupOpacity containerOpacity;
    private readonly Action<BulbVisuals> fadeStarted;
    private IDCompositionVisual? litVisual;
    private IDCompositionEffectGroup? litEffect;
    private IDCompositionVisual? glowVisual;
    private IDCompositionEffectGroup? glowEffect;
    private OpacityCurve litCurve = OpacityCurve.Constant(0);
    private OpacityCurve glowCurve = OpacityCurve.Constant(0);
    private PointI basePosition;

    private BulbVisuals(
        PreparedBulb bulb, IDCompositionVisual bulbLayer, IDCompositionVisual glowLayer, PointI origin, VisualContext context, Action<BulbVisuals> fadeStarted)
    {
        Bulb = bulb;
        this.bulbLayer = bulbLayer;
        this.glowLayer = glowLayer;
        this.origin = origin;
        this.fadeStarted = fadeStarted;
        container = context.Device.CreateVisual();
        container.SetOffsetX(bulb.Placement.Bounds.Left - origin.X);
        container.SetOffsetY(bulb.Placement.Bounds.Top - origin.Y);
        containerOpacity = new GroupOpacity(context.Device, container);
        baseVisual = context.Device.CreateVisual();
        container.AddInFront(baseVisual);
        Frame = -1;
        ShowFrame(BaseFrame, context);
        CreateLitAndGlow(context);
        bulbLayer.AddInFront(container);
    }

    /// <summary>The prepared bulb shown.</summary>
    public PreparedBulb Bulb { get; private set; }

    /// <summary>The frame shown by the base visual.</summary>
    public int Frame { get; private set; }

    /// <summary>The light-bulb opacity curve last applied.</summary>
    public OpacityCurve LitCurve => litCurve;

    /// <summary>The glow curve last applied.</summary>
    public OpacityCurve GlowCurve => glowCurve;

    /// <summary>The whole-bulb opacity.</summary>
    public GroupOpacity Opacity => containerOpacity;

    /// <summary>Number of DirectComposition visuals this bulb uses.</summary>
    public int VisualCount => 2 + (litVisual is null ? 0 : 1) + (glowVisual is null ? 0 : 1);

    private int BaseFrame => Bulb.Kind == BulbAnimationKind.LightBulb ? Bulb.UnlitFrame : 0;

    /// <summary>Creates the visuals of a bulb and adds them on top of the display's bulbs.</summary>
    /// <param name="bulb">The prepared bulb.</param>
    /// <param name="bulbLayer">The display's bulb layer.</param>
    /// <param name="glowLayer">The display's glow layer.</param>
    /// <param name="origin">The window origin.</param>
    /// <param name="context">The composition context.</param>
    /// <param name="fadeStarted">Called when a whole-bulb fade starts (the scene settles it when it ends).</param>
    public static BulbVisuals Create(
        PreparedBulb bulb, IDCompositionVisual bulbLayer, IDCompositionVisual glowLayer, PointI origin, VisualContext context, Action<BulbVisuals> fadeStarted) =>
        new(bulb, bulbLayer, glowLayer, origin, context, fadeStarted);

    /// <summary>Shows a frame in the base visual (animation and static bulbs; light bulbs keep their unlit frame).</summary>
    public void ShowFrame(int frame, VisualContext context)
    {
        if (Bulb.Kind == BulbAnimationKind.LightBulb)
        {
            frame = Bulb.UnlitFrame;
        }

        if (frame == Frame || frame < 0 || frame >= Bulb.Frames.Count)
        {
            return;
        }

        PlacedSprite sprite = Bulb.Frames[frame];
        baseVisual.SetContent(context.Surfaces.Get(sprite.Id, context.Images));
        PointI position = Relative(sprite.Position);
        if (Frame < 0 || position != basePosition)
        {
            baseVisual.SetOffsetX(position.X);
            baseVisual.SetOffsetY(position.Y);
            basePosition = position;
        }

        Frame = frame;
    }

    /// <summary>Sets the lit visual's opacity (light bulbs only).</summary>
    public void SetLit(OpacityCurve curve, VisualContext context)
    {
        if (litEffect is not null)
        {
            context.Batch.Apply(litEffect, curve);
            litCurve = curve;
        }
    }

    /// <summary>Sets the glow visual's opacity (glow intensity already applied).</summary>
    public void SetGlow(OpacityCurve curve, VisualContext context)
    {
        if (glowEffect is not null)
        {
            context.Batch.Apply(glowEffect, curve);
            glowCurve = curve;
        }
    }

    /// <summary>Sets the whole bulb's opacity (arrangement fades, the power-up's dim start of non-light bulbs).</summary>
    public void SetOpacity(OpacityCurve curve, VisualContext context)
    {
        containerOpacity.Set(curve, context.Batch);
        if (containerOpacity.SettleAt is not null)
        {
            fadeStarted(this);
        }
    }

    /// <summary>Fades the bulb and its glow out from their current values (removed bulbs, 150 ms).</summary>
    public void FadeOut(long now, double milliseconds, VisualContext context)
    {
        SetOpacity(OpacityCurves.Ramp(now, containerOpacity.Curve.Evaluate(now), 0, milliseconds), context);
        if (glowEffect is not null)
        {
            SetGlow(OpacityCurves.Ramp(now, glowCurve.Evaluate(now), 0, milliseconds), context);
        }
    }

    /// <summary>Switches to the sprites of a re-prepared bulb with the same placement (another pixel style or glow setting).</summary>
    public void Update(PreparedBulb bulb, VisualContext context)
    {
        PreparedBulb previous = Bulb;
        Bulb = bulb;
        bool sameFrames = previous.Kind == bulb.Kind && previous.Frames.SequenceEqual(bulb.Frames);
        if (!sameFrames)
        {
            int frame = Math.Min(Math.Max(Frame, 0), bulb.Frames.Count - 1);
            Frame = -1;
            ShowFrame(bulb.Kind == BulbAnimationKind.LightBulb ? bulb.UnlitFrame : frame, context);
        }

        if (!sameFrames || previous.Glow != bulb.Glow)
        {
            RemoveLitAndGlow();
            CreateLitAndGlow(context);
            SetLit(litCurve, context);
            SetGlow(glowCurve, context);
        }
    }

    /// <summary>Removes the visuals from the tree and releases them.</summary>
    public void Dispose()
    {
        RemoveLitAndGlow();
        bulbLayer.RemoveVisual(container);
        containerOpacity.Dispose();
        baseVisual.Dispose();
        container.Dispose();
    }

    private void CreateLitAndGlow(VisualContext context)
    {
        if (Bulb.Kind != BulbAnimationKind.LightBulb || Bulb.LitFrame >= Bulb.Frames.Count)
        {
            return;
        }

        PlacedSprite lit = Bulb.Frames[Bulb.LitFrame];
        litVisual = context.Device.CreateVisual();
        PointI litPosition = Relative(lit.Position);
        litVisual.SetOffsetX(litPosition.X);
        litVisual.SetOffsetY(litPosition.Y);
        litVisual.SetContent(context.Surfaces.Get(lit.Id, context.Images));
        litEffect = context.Device.CreateEffectGroup();
        litEffect.SetOpacity(0f);
        litVisual.SetEffect(litEffect);
        container.AddInFrontOf(litVisual, baseVisual);

        if (Bulb.Glow is { } glow)
        {
            glowVisual = context.Device.CreateVisual();
            glowVisual.SetOffsetX(glow.Position.X - origin.X);
            glowVisual.SetOffsetY(glow.Position.Y - origin.Y);
            glowVisual.SetContent(context.Surfaces.Get(glow.Id, context.Images));
            glowEffect = context.Device.CreateEffectGroup();
            glowEffect.SetOpacity(0f);
            glowVisual.SetEffect(glowEffect);
            glowLayer.AddInFront(glowVisual);
        }
    }

    private void RemoveLitAndGlow()
    {
        if (litVisual is not null)
        {
            container.RemoveVisual(litVisual);
            litVisual.Dispose();
            litEffect?.Dispose();
            litVisual = null;
            litEffect = null;
        }

        if (glowVisual is not null)
        {
            glowLayer.RemoveVisual(glowVisual);
            glowVisual.Dispose();
            glowEffect?.Dispose();
            glowVisual = null;
            glowEffect = null;
        }
    }

    private PointI Relative(PointI position) =>
        new(position.X - Bulb.Placement.Bounds.Left, position.Y - Bulb.Placement.Bounds.Top);
}
