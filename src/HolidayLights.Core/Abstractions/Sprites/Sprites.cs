using System.Text.Json.Serialization;

namespace HolidayLights.Core.Abstractions;

/// <summary>"Pixels" (PRODUCT-SPEC 5.3.3): how pixel art is scaled.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpriteStyle>))]
public enum SpriteStyle
{
    /// <summary>"Smooth" (default): MMPX 2x/4x to the smallest power of two at or above S, then area-average down in premultiplied alpha.</summary>
    [JsonStringEnumMemberName("smooth")]
    Smooth,

    /// <summary>"Crisp": nearest neighbour to ceil(S), then area-average down to S (pure nearest neighbour at integer scales).</summary>
    [JsonStringEnumMemberName("crisp")]
    Crisp,
}

/// <summary>"Glow" (PRODUCT-SPEC 5.9).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GlowLevel>))]
public enum GlowLevel
{
    /// <summary>"Off".</summary>
    [JsonStringEnumMemberName("off")]
    Off,

    /// <summary>"Soft" (default): intensity 0.55.</summary>
    [JsonStringEnumMemberName("soft")]
    Soft,

    /// <summary>"Bright": intensity 1.0.</summary>
    [JsonStringEnumMemberName("bright")]
    Bright,
}

/// <summary>Glow constants (PRODUCT-SPEC 5.9).</summary>
public static class GlowLevels
{
    /// <summary>The opacity factor of glow visuals: Off 0, Soft 0.55, Bright 1.0.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The intensity.</returns>
    public static float Intensity(GlowLevel level) => level switch
    {
        GlowLevel.Off => 0f,
        GlowLevel.Soft => 0.55f,
        GlowLevel.Bright => 1f,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}

/// <summary>Cache key of one scaled sprite.</summary>
/// <param name="ContentKey">The bulb's <see cref="IBulb.ContentKey"/>.</param>
/// <param name="Slot">The art slot.</param>
/// <param name="Flavor">Raw flavor (reduced modulo the flavor count for sides; 0 for corners and the preview).</param>
/// <param name="Frame">The frame (0 to frame count - 1).</param>
/// <param name="Scale">S, the art-pixel to output-pixel factor.</param>
/// <param name="Style">Smooth or Crisp.</param>
public readonly record struct SpriteKey(string ContentKey, CellSlot Slot, int Flavor, int Frame, double Scale, SpriteStyle Style);

/// <summary>A sprite to prepare ahead of time.</summary>
/// <param name="Bulb">The bulb.</param>
/// <param name="Slot">The art slot.</param>
/// <param name="Flavor">Raw flavor.</param>
/// <param name="Frame">The frame.</param>
/// <param name="Scale">S.</param>
/// <param name="Style">Smooth or Crisp.</param>
public sealed record SpriteRequest(IBulb Bulb, CellSlot Slot, int Flavor, int Frame, double Scale, SpriteStyle Style);

/// <summary>A glow halo of a lit light bulb.</summary>
/// <param name="Image">Premultiplied with alpha 0 everywhere (pure additive light), so it brightens whatever lies behind it.</param>
/// <param name="OffsetX">Horizontal offset of the image relative to the bulb sprite's top-left (negative: the margin of ceil(3 sigma)).</param>
/// <param name="OffsetY">Vertical offset of the image relative to the bulb sprite's top-left.</param>
public sealed record GlowSprite(PremultipliedImage Image, int OffsetX, int OffsetY);

/// <summary>
/// Scaled, premultiplied bulb sprites and glow halos (PRODUCT-SPEC 5.3.3, 5.9). Implemented by core-sprites.
/// </summary>
/// <remarks>
/// <para>Sizes follow <see cref="ArtScale.Scale(SizeI, double)"/> exactly, so sprites fit the layout to the pixel. At S = 1
/// both styles return the original pixels (premultiplied).</para>
/// <para>Results are cached in memory. <see cref="PrefetchAsync"/> also stores the scaled frames on disk
/// (<c>%LOCALAPPDATA%\Holiday Lights\Cache</c>, keyed by <see cref="SpriteKey"/>) and bakes no halos; the halo of a prefetched
/// light bulb is baked by its first <see cref="GetGlow"/> and then stored on disk. Nothing asks for halos while glow is off.
/// Every request reads the disk cache on a memory miss. The presenter therefore prefetches the desktop's sprites to get a fast warm start,
/// and previews at arbitrary zooms never fill the disk. Returned images are shared: callers must not modify them.
/// Thread-safe.</para>
/// </remarks>
public interface ISpriteProvider
{
    /// <summary>Returns one scaled frame.</summary>
    /// <param name="bulb">The bulb.</param>
    /// <param name="slot">The art slot.</param>
    /// <param name="flavor">Raw flavor.</param>
    /// <param name="frame">The frame (0 to frame count - 1).</param>
    /// <param name="scale">S (any positive value; previews use S x zoom).</param>
    /// <param name="style">Smooth or Crisp.</param>
    /// <returns>The premultiplied sprite.</returns>
    PremultipliedImage GetSprite(IBulb bulb, CellSlot slot, int flavor, int frame, double scale, SpriteStyle style);

    /// <summary>
    /// Returns the glow halo of a light bulb animation: its emissive pixels (luma(lit) - luma(unlit) &gt;= 32 or
    /// max(R,G,B)(lit) - max(R,G,B)(unlit) &gt;= 32) in their lit colours, Gaussian blur with
    /// sigma = min(0.22 x min(cell width, cell height), 24 art px) at the scale, margin ceil(3 sigma) (PO decision 1).
    /// </summary>
    /// <param name="bulb">The bulb.</param>
    /// <param name="slot">The art slot.</param>
    /// <param name="flavor">Raw flavor.</param>
    /// <param name="scale">S.</param>
    /// <returns>The halo, or null when the animation is not a light bulb or has no emissive pixels.</returns>
    GlowSprite? GetGlow(IBulb bulb, CellSlot slot, int flavor, double scale);

    /// <summary>
    /// Prepares sprites on the thread pool and stores them in the disk cache (start-up, arrangement and scale changes). No
    /// halo is baked here: a prefetched light bulb's halo is baked by its first <see cref="GetGlow"/> and then stored.
    /// </summary>
    /// <param name="requests">The sprites to prepare.</param>
    /// <param name="cancellationToken">Stops preparing.</param>
    /// <returns>A task that completes when every sprite is ready.</returns>
    Task PrefetchAsync(IEnumerable<SpriteRequest> requests, CancellationToken cancellationToken = default);

    /// <summary>Drops every cached sprite of a content key (memory and disk), e.g. after Bulb Editing replaced a file.</summary>
    /// <param name="contentKey">An <see cref="IBulb.ContentKey"/>.</param>
    void Evict(string contentKey);
}

/// <summary>How a source image is combined with the target.</summary>
public enum CompositeMode
{
    /// <summary>Premultiplied source-over: <c>dst = src x o + dst x (1 - a x o)</c> (o = opacity).</summary>
    SourceOver,

    /// <summary>Additive light: <c>dst.rgb = min(255, dst.rgb + src.rgb x o)</c>; alpha unchanged. Used for glow.</summary>
    Additive,
}

/// <summary>Renders the bulbs of one display of a layout into a CPU buffer (light stages, theme cards, strips, <c>--render-test</c>).</summary>
public sealed record LightsRenderRequest
{
    /// <summary>The layout.</summary>
    public required LightsLayout Layout { get; init; }

    /// <summary>Which display's placements to draw (<see cref="DisplayInfo.DeviceId"/>); null draws every placement.</summary>
    public string? DisplayId { get; init; }

    /// <summary>One state per placement ordinal (from <see cref="IFlashSequencer.Sample"/>, or all frame 0 lit for static previews).</summary>
    public required IReadOnlyList<BulbVisualState> States { get; init; }

    /// <summary>Resolves the bulbs of the layout.</summary>
    public required IBulbResolver Bulbs { get; init; }

    /// <summary>Provides sprites; they are requested at <c>target S x Zoom</c>.</summary>
    public required ISpriteProvider Sprites { get; init; }

    /// <summary>Layout pixels to target pixels: <c>target = layout x Zoom + Offset</c> (1 for full-size output).</summary>
    public double Zoom { get; init; } = 1.0;

    /// <summary>Horizontal offset in target pixels.</summary>
    public double OffsetX { get; init; }

    /// <summary>Vertical offset in target pixels.</summary>
    public double OffsetY { get; init; }

    /// <summary>Pixel-art style.</summary>
    public SpriteStyle Style { get; init; } = SpriteStyle.Smooth;

    /// <summary>Glow intensity (<see cref="GlowLevels.Intensity"/>); 0 draws no glow (High Contrast, light backgrounds).</summary>
    public float GlowIntensity { get; init; }
}

/// <summary>
/// The CPU compositor (ARCHITECTURE 4.5): premultiplied source-over and additive blits into a premultiplied BGRA buffer.
/// Implemented by core-sprites. Stateless and thread-safe (each call may run on any thread with its own target).
/// </summary>
public interface ICpuCompositor
{
    /// <summary>Fills the whole target with one premultiplied colour.</summary>
    /// <param name="target">The buffer.</param>
    /// <param name="premultipliedColor">A packed premultiplied pixel (<see cref="Bgra32"/>).</param>
    void Clear(PremultipliedImage target, uint premultipliedColor);

    /// <summary>Source-over fills a rectangle with a premultiplied colour (e.g. the taskbar band #202020 at 85 %).</summary>
    /// <param name="target">The buffer.</param>
    /// <param name="area">The rectangle (clipped to the target).</param>
    /// <param name="premultipliedColor">A packed premultiplied pixel.</param>
    void Fill(PremultipliedImage target, RectI area, uint premultipliedColor);

    /// <summary>Draws an image at an integer position (clipped to the target).</summary>
    /// <param name="target">The buffer.</param>
    /// <param name="source">The image.</param>
    /// <param name="x">Left in target pixels.</param>
    /// <param name="y">Top in target pixels.</param>
    /// <param name="opacity">0-1.</param>
    /// <param name="mode">Source-over or additive.</param>
    void Draw(PremultipliedImage target, PremultipliedImage source, int x, int y, float opacity, CompositeMode mode);

    /// <summary>
    /// Draws the bulbs of a layout in two passes: first the glow of every lit light bulb (additive, clipped to its display's
    /// area), then, for each placement, the unlit frame and the lit frame at its brightness for light bulbs, or the state's
    /// frame for other bulbs. Glow therefore lies beneath every bulb (PRODUCT-SPEC 5.9), and overlapping glows add up.
    /// Positions are <c>floor(v x Zoom + Offset + 0.5)</c>.
    /// </summary>
    /// <param name="target">The buffer (it may already hold a backdrop).</param>
    /// <param name="request">What to draw.</param>
    void DrawLights(PremultipliedImage target, LightsRenderRequest request);
}
