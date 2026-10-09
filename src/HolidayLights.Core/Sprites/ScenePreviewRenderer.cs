namespace HolidayLights.Core.Sprites;

/// <summary>
/// Renders a <see cref="LightsScene"/> (one display, or the whole desktop) into a premultiplied buffer at any zoom: light
/// stages, theme cards and thumbnails, and <c>--render-test</c> (PRODUCT-SPEC 3.0.2, D20).
/// </summary>
/// <remarks>
/// <para>Per display it paints the backdrop (night gradient, a solid colour, or nothing), the taskbar band over the part
/// outside the work area, then the lights with <see cref="ICpuCompositor.DrawLights"/>: the same layout, sprites (at
/// S x zoom), brightness model and additive glow as the desktop, so a preview shows exactly what the desktop shows for
/// the same states. Physical pixel <c>p</c> lands on target pixel <c>p x zoom + offset</c>.</para>
/// <para>When the scene's lights are off, only the backdrop and band are drawn; pass the scene <c>with { LightsOn = true }</c>
/// to show the arrangement anyway. Icons, windows and wallpapers are not drawn (draw a wallpaper into the target first and
/// use <see cref="PreviewBackdrop.None"/>). Thread-safe when each call has its own target.</para>
/// </remarks>
public sealed class ScenePreviewRenderer
{
    private readonly ISpriteProvider sprites;
    private readonly ICpuCompositor compositor;

    /// <summary>Creates the renderer.</summary>
    /// <param name="sprites">The sprite provider.</param>
    /// <param name="compositor">The compositor.</param>
    public ScenePreviewRenderer(ISpriteProvider sprites, ICpuCompositor compositor)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(compositor);
        this.sprites = sprites;
        this.compositor = compositor;
    }

    /// <summary>The size of a preview of an area: <c>round(size x zoom)</c>, at least one pixel per non-empty dimension.</summary>
    /// <param name="area">The area in physical pixels.</param>
    /// <param name="zoom">Target pixels per physical pixel.</param>
    /// <returns>The size in target pixels.</returns>
    public static SizeI GetOutputSize(RectI area, double zoom)
    {
        ValidateZoom(zoom);
        return new SizeI(ZoomLength(area.Width, zoom), ZoomLength(area.Height, zoom));
    }

    /// <summary>
    /// The static picture of a layout: frame 0 of every bulb, light bulbs lit with full glow (theme cards, reduced motion,
    /// "Stop Flashing", previews that do not animate).
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="bulbs">Resolves its bulbs.</param>
    /// <returns>One state per placement ordinal.</returns>
    public static BulbVisualState[] CreateStaticStates(LightsLayout layout, IBulbResolver bulbs)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(bulbs);
        var states = new BulbVisualState[layout.Placements.Count];
        foreach (BulbPlacement placement in layout.Placements)
        {
            BulbAnimationInfo? animation = bulbs.TryGetBulb(placement.BulbId, out IBulb? bulb)
                ? bulb.GetAnimation(placement.Slot, placement.Flavor)
                : null;
            states[placement.Ordinal] = animation?.Kind == BulbAnimationKind.LightBulb
                ? new BulbVisualState(animation.LitFrame, 1f, 1f)
                : new BulbVisualState(0, 1f, 0f);
        }

        return states;
    }

    /// <summary>Renders one display: the image covers its whole bounds (work area and taskbar) at the zoom.</summary>
    /// <param name="request">What to render.</param>
    /// <param name="displayId">The display (<see cref="DisplayInfo.DeviceId"/>).</param>
    /// <returns>A new image of <see cref="GetOutputSize"/> of the display's bounds.</returns>
    /// <exception cref="ArgumentException">The scene has no such display.</exception>
    public PremultipliedImage RenderDisplay(ScenePreviewRequest request, string displayId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(displayId);
        DisplayInfo display = FindDisplay(request.Scene, displayId);
        SizeI size = GetOutputSize(display.Bounds, request.Zoom);
        var image = new PremultipliedImage(size.Width, size.Height);
        RenderInto(image, request, displayId, -display.Bounds.Left * request.Zoom, -display.Bounds.Top * request.Zoom);
        return image;
    }

    /// <summary>Renders every display of the scene at its place in the virtual screen (pixels between displays stay transparent).</summary>
    /// <param name="request">What to render.</param>
    /// <returns>A new image covering the bounding box of all displays (empty when the scene has none).</returns>
    public PremultipliedImage RenderDesktop(ScenePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RectI desktop = default;
        foreach (DisplayScene display in request.Scene.Displays)
        {
            desktop = desktop.Union(display.Display.Bounds);
        }

        if (desktop.IsEmpty)
        {
            return new PremultipliedImage(0, 0);
        }

        SizeI size = GetOutputSize(desktop, request.Zoom);
        var image = new PremultipliedImage(size.Width, size.Height);
        RenderInto(image, request, null, -desktop.Left * request.Zoom, -desktop.Top * request.Zoom);
        return image;
    }

    /// <summary>Renders one display, or every display, into an existing target.</summary>
    /// <param name="target">The buffer (it may already hold a picture, e.g. a wallpaper).</param>
    /// <param name="request">What to render.</param>
    /// <param name="displayId">One display, or null for every display of the scene.</param>
    /// <param name="offsetX">Target x of physical x = 0 (after zooming).</param>
    /// <param name="offsetY">Target y of physical y = 0 (after zooming).</param>
    /// <exception cref="ArgumentException">The scene has no such display.</exception>
    public void RenderInto(PremultipliedImage target, ScenePreviewRequest request, string? displayId, double offsetX, double offsetY)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        ValidateZoom(request.Zoom);
        LightsScene scene = request.Scene;
        IReadOnlyList<BulbVisualState> states = request.States ?? CreateStaticStates(scene.Layout, request.Bulbs);
        IEnumerable<DisplayInfo> displays = displayId is null
            ? scene.Displays.Select(d => d.Display)
            : [FindDisplay(scene, displayId)];

        foreach (DisplayInfo display in displays)
        {
            PaintBackdrop(target, request, TargetPixels.Map(display.Bounds, request.Zoom, offsetX, offsetY));
            if (request.DrawTaskbarBand)
            {
                PaintTaskbarBand(target, display, request.Zoom, offsetX, offsetY);
            }

            if (scene.LightsOn)
            {
                compositor.DrawLights(target, new LightsRenderRequest
                {
                    Layout = scene.Layout,
                    DisplayId = display.DeviceId,
                    States = states,
                    Bulbs = request.Bulbs,
                    Sprites = sprites,
                    Zoom = request.Zoom,
                    OffsetX = offsetX,
                    OffsetY = offsetY,
                    Style = request.Style ?? scene.Effects.Pixels,
                    GlowIntensity = request.GlowIntensity ?? GlowLevels.Intensity(scene.Effects.Glow),
                });
            }
        }
    }

    private void PaintBackdrop(PremultipliedImage target, ScenePreviewRequest request, RectI area)
    {
        switch (request.Backdrop)
        {
            case PreviewBackdrop.NightGradient:
                RectI visible = area.Intersect(new RectI(0, 0, target.Width, target.Height));
                for (int y = visible.Top; y < visible.Bottom; y++)
                {
                    target.Pixels.AsSpan(y * target.Width + visible.Left, visible.Width)
                        .Fill(PreviewPalette.NightGradientAt(y - area.Top, area.Height));
                }

                break;
            case PreviewBackdrop.SolidColor:
                compositor.Fill(target, area, request.BackdropColor.ToBgra32());
                break;
            case PreviewBackdrop.None:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Backdrop, "Unknown backdrop.");
        }
    }

    /// <summary>Fills the parts of a display outside its work area (the taskbar and app bars) with the taskbar band.</summary>
    private void PaintTaskbarBand(PremultipliedImage target, DisplayInfo display, double zoom, double offsetX, double offsetY)
    {
        RectI bounds = TargetPixels.Map(display.Bounds, zoom, offsetX, offsetY);
        RectI work = TargetPixels.Map(display.WorkArea, zoom, offsetX, offsetY).Intersect(bounds);
        uint band = PreviewPalette.TaskbarBand;
        if (work.IsEmpty)
        {
            compositor.Fill(target, bounds, band);
            return;
        }

        compositor.Fill(target, new RectI(bounds.Left, bounds.Top, bounds.Right, work.Top), band);
        compositor.Fill(target, new RectI(bounds.Left, work.Bottom, bounds.Right, bounds.Bottom), band);
        compositor.Fill(target, new RectI(bounds.Left, work.Top, work.Left, work.Bottom), band);
        compositor.Fill(target, new RectI(work.Right, work.Top, bounds.Right, work.Bottom), band);
    }

    private static DisplayInfo FindDisplay(LightsScene scene, string displayId) =>
        scene.Displays.FirstOrDefault(d => string.Equals(d.Display.DeviceId, displayId, StringComparison.Ordinal))?.Display
        ?? throw new ArgumentException($"The scene has no display '{displayId}'.", nameof(displayId));

    private static int ZoomLength(int length, double zoom) =>
        length <= 0 ? 0 : Math.Max(1, (int)Math.Round(length * zoom, MidpointRounding.AwayFromZero));

    private static void ValidateZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "The zoom must be a positive finite number.");
        }
    }
}
