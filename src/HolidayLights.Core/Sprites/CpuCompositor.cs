namespace HolidayLights.Core.Sprites;

/// <summary>The CPU compositor (see <see cref="ICpuCompositor"/>). Owner: core-sprites.</summary>
/// <remarks>
/// <para>All blending is 8-bit premultiplied arithmetic with exact rounding: source-over
/// <c>dst = src x o + dst x (1 - a x o)</c> (colour above alpha adds light, as DirectComposition composes it) and additive
/// <c>dst.rgb = min(255, dst.rgb + src.rgb x o)</c> with alpha unchanged; the opacity is quantized to 1/255.</para>
/// <para><see cref="DrawLights"/> draws in two passes: first the glow of every lit light bulb (additive, clipped to its
/// display's area), then every bulb in placement order, so glow lies beneath all bulbs and overlapping glows add up
/// (PRODUCT-SPEC 5.9).</para>
/// <para>Stateless and thread-safe.</para>
/// </remarks>
public sealed class CpuCompositor : ICpuCompositor
{
    /// <inheritdoc />
    public void Clear(PremultipliedImage target, uint premultipliedColor)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Pixels.AsSpan().Fill(premultipliedColor);
    }

    /// <inheritdoc />
    public void Fill(PremultipliedImage target, RectI area, uint premultipliedColor)
    {
        ArgumentNullException.ThrowIfNull(target);
        RectI clipped = area.Intersect(Bounds(target));
        if (clipped.IsEmpty || premultipliedColor == Bgra32.Transparent)
        {
            return;
        }

        if (premultipliedColor >> 24 == 255)
        {
            for (int y = clipped.Top; y < clipped.Bottom; y++)
            {
                target.Pixels.AsSpan(y * target.Width + clipped.Left, clipped.Width).Fill(premultipliedColor);
            }

            return;
        }

        var color = new uint[clipped.Width];
        Array.Fill(color, premultipliedColor);
        for (int y = clipped.Top; y < clipped.Bottom; y++)
        {
            PixelBlender.SourceOver(color, target.Pixels.AsSpan(y * target.Width + clipped.Left, clipped.Width), 255);
        }
    }

    /// <inheritdoc />
    public void Draw(PremultipliedImage target, PremultipliedImage source, int x, int y, float opacity, CompositeMode mode)
    {
        ArgumentNullException.ThrowIfNull(target);
        Blit(target, source, x, y, opacity, mode, Bounds(target));
    }

    /// <inheritdoc />
    public void DrawLights(PremultipliedImage target, LightsRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<LightsDrawItem> items = LightsDrawItem.Collect(request);

        float glowIntensity = request.GlowIntensity;
        if (glowIntensity > 0)
        {
            foreach (LightsDrawItem item in items)
            {
                DrawGlow(target, request, item, glowIntensity);
            }
        }

        foreach (LightsDrawItem item in items)
        {
            DrawBulb(target, request, item);
        }
    }

    /// <summary>Draws an image at an integer position, clipped to the target and to <paramref name="clip"/>.</summary>
    /// <param name="target">The buffer.</param>
    /// <param name="source">The image.</param>
    /// <param name="x">Left in target pixels.</param>
    /// <param name="y">Top in target pixels.</param>
    /// <param name="opacity">0-1 (clamped).</param>
    /// <param name="mode">Source-over or additive.</param>
    /// <param name="clip">Only pixels inside this rectangle change.</param>
    internal static void Blit(PremultipliedImage target, PremultipliedImage source, int x, int y, float opacity, CompositeMode mode, RectI clip)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        if (float.IsNaN(opacity))
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), opacity, "The opacity must be a number from 0 to 1.");
        }

        if (mode is not (CompositeMode.SourceOver or CompositeMode.Additive))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        uint factor = PixelBlender.OpacityToByte(opacity);
        RectI visible = clip.Intersect(Bounds(target));
        long left = Math.Max(visible.Left, (long)x);
        long top = Math.Max(visible.Top, (long)y);
        long right = Math.Min(visible.Right, (long)x + source.Width);
        long bottom = Math.Min(visible.Bottom, (long)y + source.Height);
        if (factor == 0 || left >= right || top >= bottom)
        {
            return;
        }

        int width = (int)(right - left);
        int sourceX = (int)(left - x);
        for (int row = (int)top; row < bottom; row++)
        {
            ReadOnlySpan<uint> from = source.Pixels.AsSpan((row - y) * source.Width + sourceX, width);
            Span<uint> to = target.Pixels.AsSpan(row * target.Width + (int)left, width);
            if (mode == CompositeMode.SourceOver)
            {
                PixelBlender.SourceOver(from, to, factor);
            }
            else
            {
                PixelBlender.Additive(from, to, factor);
            }
        }
    }

    private static void DrawGlow(PremultipliedImage target, LightsRenderRequest request, LightsDrawItem item, float glowIntensity)
    {
        if (item.Animation.Kind != BulbAnimationKind.LightBulb || item.State.Glow <= 0)
        {
            return;
        }

        GlowSprite? glow = request.Sprites.GetGlow(item.Bulb, item.Placement.Slot, item.Placement.Flavor, item.SpriteScale);
        if (glow is not null)
        {
            Blit(target, glow.Image, item.X + glow.OffsetX, item.Y + glow.OffsetY, item.State.Glow * glowIntensity,
                CompositeMode.Additive, item.DisplayArea);
        }
    }

    private static void DrawBulb(PremultipliedImage target, LightsRenderRequest request, LightsDrawItem item)
    {
        BulbPlacement placement = item.Placement;
        BulbAnimationInfo animation = item.Animation;
        if (animation.Kind == BulbAnimationKind.LightBulb)
        {
            PremultipliedImage unlit = request.Sprites.GetSprite(
                item.Bulb, placement.Slot, placement.Flavor, animation.UnlitFrame, item.SpriteScale, request.Style);
            Blit(target, unlit, item.X, item.Y, 1f, CompositeMode.SourceOver, Bounds(target));
            if (item.State.Brightness > 0)
            {
                PremultipliedImage lit = request.Sprites.GetSprite(
                    item.Bulb, placement.Slot, placement.Flavor, animation.LitFrame, item.SpriteScale, request.Style);
                Blit(target, lit, item.X, item.Y, item.State.Brightness, CompositeMode.SourceOver, Bounds(target));
            }

            return;
        }

        int frame = Math.Max(0, item.State.Frame) % animation.FrameCount;
        PremultipliedImage sprite = request.Sprites.GetSprite(
            item.Bulb, placement.Slot, placement.Flavor, frame, item.SpriteScale, request.Style);
        Blit(target, sprite, item.X, item.Y, item.State.Brightness, CompositeMode.SourceOver, Bounds(target));
    }

    private static RectI Bounds(PremultipliedImage image) => new(0, 0, image.Width, image.Height);
}
