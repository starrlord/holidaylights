using System.Collections.Concurrent;
using System.Windows;
using HolidayLights.Core.Sprites;

namespace HolidayLights.App.Preview;

/// <summary>
/// The inputs of a <see cref="LightStrip"/>, read on the UI thread, so that its model can be built (and its sprites
/// prepared) on the thread pool.
/// </summary>
/// <param name="Bulbs">Resolves the bulbs.</param>
/// <param name="Side">The edge.</param>
/// <param name="IncludeCorners">Draw the edge's corners at both ends.</param>
/// <param name="Arrangement">The arrangement (the desktop's when the strip has none).</param>
/// <param name="SingleBulbId">A run of one bulb, or null.</param>
/// <param name="BulbHeight">Height in DIP of the tallest cell.</param>
/// <param name="MaxArtScale">The most DIPs per art pixel.</param>
/// <param name="FallbackToListPreview">Show the list preview of a nearly empty single bulb.</param>
/// <param name="CropRun">Lay out a longer run of the single bulb and crop it at the far end.</param>
/// <param name="Style">The pixel-art style.</param>
/// <param name="PixelsPerDip">Device pixels per DIP.</param>
/// <param name="RenderSize">The strip's size in DIP.</param>
internal sealed record StripSpec(
    IBulbResolver Bulbs, Side Side, bool IncludeCorners, SlotAssignment Arrangement, string? SingleBulbId, double BulbHeight,
    double MaxArtScale, bool FallbackToListPreview, bool CropRun, SpriteStyle Style, double PixelsPerDip, Size RenderSize)
{
    /// <summary>Reads a strip's inputs (UI thread).</summary>
    /// <param name="services">The services.</param>
    /// <param name="strip">The strip.</param>
    /// <param name="pixelsPerDip">Device pixels per DIP.</param>
    /// <param name="renderSize">The strip's size in DIP.</param>
    /// <returns>The inputs.</returns>
    public static StripSpec From(IAppServices services, LightStrip strip, double pixelsPerDip, Size renderSize) => new(
        strip.Bulbs ?? services.Bulbs,
        strip.Side,
        strip.IncludeCorners,
        strip.Arrangement ?? services.Settings.Current.Current.Arrangement,
        strip.SingleBulbId,
        strip.BulbHeight,
        strip.MaxArtScale,
        strip.FallbackToListPreview,
        strip.CropRun,
        services.Settings.Current.Look.Pixels,
        pixelsPerDip,
        renderSize);
}

/// <summary>
/// What a <see cref="LightStrip"/> draws: one edge (with its corners) laid out by the real layout engine in the strip's
/// device pixels, scaled so the tallest cell has the requested height; or, for a spacer bulb, its list preview.
/// </summary>
/// <remarks>Built on any thread from a <see cref="StripSpec"/>; drawn on the UI thread.</remarks>
internal sealed class StripModel
{
    /// <summary>The id of the strip's single layout target.</summary>
    public const string LayoutTargetId = "strip";

    /// <summary>A cropped run is laid out this many times longer than the strip, so it always runs past the far end.</summary>
    private const int CroppedRunLengths = 3;

    private static readonly ConcurrentDictionary<string, bool> NearlyEmptyCache = new(StringComparer.Ordinal);

    private readonly SlotAssignment source;
    private readonly IBulb? previewBulb;
    private readonly double previewScale;

    private StripModel(IBulbResolver bulbs, SlotAssignment source, LightsLayout layout, SizeI pixelSize, double offsetX, double offsetY, SpriteStyle style, IBulb? previewBulb, double previewScale)
    {
        Bulbs = bulbs;
        this.source = source;
        Layout = layout;
        PixelSize = pixelSize;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Style = style;
        this.previewBulb = previewBulb;
        this.previewScale = previewScale;
    }

    /// <summary>Resolves the strip's bulbs.</summary>
    public IBulbResolver Bulbs { get; }

    /// <summary>The layout in strip pixels (a cropped run reaches past <see cref="PixelSize"/>).</summary>
    public LightsLayout Layout { get; }

    /// <summary>The strip's size in device pixels.</summary>
    public SizeI PixelSize { get; }

    /// <summary>Where layout pixel 0 lands (centres the strip across its thickness).</summary>
    public double OffsetX { get; }

    /// <summary>Where layout pixel 0 lands vertically.</summary>
    public double OffsetY { get; }

    /// <summary>The pixel-art style.</summary>
    public SpriteStyle Style { get; }

    /// <summary>True when nothing can be drawn (no bulb in the edge or its corners).</summary>
    public bool IsEmpty => source.HasNoBulbs();

    /// <summary>Builds the model of a strip (UI thread: reads the strip's properties).</summary>
    /// <param name="services">The services.</param>
    /// <param name="strip">The strip and its settings.</param>
    /// <param name="pixelsPerDip">Device pixels per DIP.</param>
    /// <param name="renderSize">The strip's size in DIP.</param>
    /// <returns>The model.</returns>
    public static StripModel Create(IAppServices services, LightStrip strip, double pixelsPerDip, Size renderSize) =>
        Create(services, StripSpec.From(services, strip, pixelsPerDip, renderSize));

    /// <summary>Builds the model of a strip from its inputs (any thread: decodes art and runs the layout engine).</summary>
    /// <param name="services">The services.</param>
    /// <param name="spec">The strip's inputs.</param>
    /// <returns>The model.</returns>
    public static StripModel Create(IAppServices services, StripSpec spec)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(spec);
        IBulbResolver bulbs = spec.Bulbs;
        Side side = spec.Side;
        SlotAssignment source = Source(spec, bulbs, ref side);
        bool horizontal = side is Side.Top or Side.Bottom;
        var pixelSize = new SizeI(
            Math.Max(0, (int)Math.Floor(spec.RenderSize.Width * spec.PixelsPerDip)),
            Math.Max(0, (int)Math.Floor(spec.RenderSize.Height * spec.PixelsPerDip)));
        int thickness = ArtThickness(source, bulbs, side);
        if (thickness <= 0 || pixelSize.IsEmpty)
        {
            return new StripModel(bulbs, source, LightsLayout.Empty, pixelSize, 0, 0, spec.Style, null, 0);
        }

        double scale = Math.Min(spec.BulbHeight * spec.PixelsPerDip / thickness, spec.MaxArtScale * spec.PixelsPerDip);
        int length = horizontal ? pixelSize.Width : pixelSize.Height;
        int runLength = length;
        if (spec.SingleBulbId is { } id && bulbs.TryGetBulb(id, out IBulb? single))
        {
            if (spec.FallbackToListPreview && IsNearlyEmpty(single, side))
            {
                return CreatePreviewModel(bulbs, source, pixelSize, spec, single);
            }

            scale = FitOneBulb(single, side, length, scale);
            if (spec.CropRun)
            {
                // PRODUCT-SPEC 3.2.5: flavors 1, 2, 3 ... side by side at the bulb's real spacing, cropped to the width.
                runLength = length * CroppedRunLengths;
            }
        }

        var area = horizontal ? new RectI(0, 0, runLength, pixelSize.Height) : new RectI(0, 0, pixelSize.Width, runLength);
        LightsLayout layout = services.Layout.Layout([new LayoutTarget(LayoutTargetId, area, scale)], source, bulbs);
        RectI band = layout.Displays.SelectMany(d => d.Strips).Where(s => s.Count > 0 || s.Placements.Count > 0)
            .Select(s => s.Rect).Aggregate(default(RectI), (a, b) => a.Union(b));
        double offsetX = horizontal || band.IsEmpty ? 0 : (pixelSize.Width - band.Width) / 2.0 - band.Left;
        double offsetY = !horizontal || band.IsEmpty ? 0 : (pixelSize.Height - band.Height) / 2.0 - band.Top;
        return new StripModel(bulbs, source, layout, pixelSize, Math.Round(offsetX), Math.Round(offsetY), spec.Style, null, 0);
    }

    /// <summary>True when the strip shows the bulb.</summary>
    /// <param name="bulbId">A bulb id.</param>
    /// <returns>True when used.</returns>
    public bool UsesBulb(string bulbId) => source.Contains(bulbId);

    /// <summary>
    /// Prepares every sprite the strip will draw - each frame of each placement and, with glow, the halos of light bulbs -
    /// so that drawing it later only copies pixels (thread pool: the Bulb List tiles).
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="glow">True when the strip draws glow.</param>
    public void WarmSprites(IAppServices services, bool glow)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (previewBulb is not null)
        {
            services.Sprites.GetSprite(previewBulb, CellSlot.Preview, 0, 0, previewScale, Style);
            return;
        }

        double scale = Layout.Displays.Count > 0 ? Layout.Displays[0].Target.Scale : 1;
        var seen = new HashSet<(string, CellSlot, int)>();
        foreach (BulbPlacement placement in Layout.Placements)
        {
            if (!seen.Add((placement.BulbId, placement.Slot, placement.Flavor)) || !Bulbs.TryGetBulb(placement.BulbId, out IBulb? bulb))
            {
                continue;
            }

            BulbAnimationInfo animation = bulb.GetAnimation(placement.Slot, placement.Flavor);
            if (animation.Kind == BulbAnimationKind.LightBulb)
            {
                services.Sprites.GetSprite(bulb, placement.Slot, placement.Flavor, animation.UnlitFrame, scale, Style);
                services.Sprites.GetSprite(bulb, placement.Slot, placement.Flavor, animation.LitFrame, scale, Style);
                if (glow)
                {
                    services.Sprites.GetGlow(bulb, placement.Slot, placement.Flavor, scale);
                }

                continue;
            }

            for (int frame = 0; frame < animation.FrameCount; frame++)
            {
                services.Sprites.GetSprite(bulb, placement.Slot, placement.Flavor, frame, scale, Style);
            }
        }
    }

    /// <summary>Draws the strip into a buffer of <see cref="PixelSize"/>.</summary>
    /// <param name="target">The buffer.</param>
    /// <param name="states">One state per placement.</param>
    /// <param name="services">The services.</param>
    /// <param name="glowIntensity">Glow intensity (0 for none).</param>
    public void Draw(PremultipliedImage target, IReadOnlyList<BulbVisualState> states, IAppServices services, float glowIntensity)
    {
        if (previewBulb is not null)
        {
            DrawListPreview(target, services);
            return;
        }

        if (Layout.Placements.Count == 0)
        {
            return;
        }

        services.Compositor.DrawLights(target, new LightsRenderRequest
        {
            Layout = Layout,
            States = states,
            Bulbs = Bulbs,
            Sprites = services.Sprites,
            Style = Style,
            GlowIntensity = glowIntensity,
            OffsetX = OffsetX,
            OffsetY = OffsetY,
        });
    }

    /// <summary>True when a bulb's art on a side is (nearly) invisible: spacers show their list preview instead.</summary>
    /// <param name="bulb">The bulb.</param>
    /// <param name="side">The side.</param>
    /// <returns>True for spacers and other nearly empty art.</returns>
    public static bool IsNearlyEmpty(IBulb bulb, Side side) =>
        NearlyEmptyCache.GetOrAdd($"{bulb.ContentKey}|{side}", _ =>
        {
            Rgba32Image image = bulb.GetCell(side.ToSlot(), 0, 0).Image;
            int opaque = 0;
            foreach (uint pixel in image.Pixels)
            {
                if (pixel >> 24 != 0)
                {
                    opaque++;
                }
            }

            return opaque <= Math.Max(4, image.Pixels.Length / 50);
        });

    private static SlotAssignment Source(StripSpec spec, IBulbResolver bulbs, ref Side side)
    {
        if (spec.SingleBulbId is { } id)
        {
            return SlotAssignment.Empty.WithEdge(side, [id]);
        }

        SlotAssignment arrangement = spec.Arrangement;
        IReadOnlyList<string> ids = arrangement.GetEdge(side);
        if (ids.Count == 0 && side == Side.Top)
        {
            ids = CellSlots.Sides.Select(arrangement.GetEdge).FirstOrDefault(e => e.Count > 0) ?? [];
        }

        SlotAssignment result = SlotAssignment.Empty.WithEdge(side, ids.Where(i => bulbs.TryGetBulb(i, out _)));
        if (spec.IncludeCorners && side is Side.Top or Side.Bottom)
        {
            (Corner start, Corner end) = CellSlots.CornersOf(side);
            result = result.WithCorner(start, arrangement.GetCorner(start)).WithCorner(end, arrangement.GetCorner(end));
        }

        return result;
    }

    /// <summary>The largest cell across the strip at scale 1 (height for horizontal strips, width for vertical ones).</summary>
    private static int ArtThickness(SlotAssignment source, IBulbResolver bulbs, Side side)
    {
        bool horizontal = side is Side.Top or Side.Bottom;
        int thickness = 0;
        foreach (string id in source.GetEdge(side))
        {
            if (!bulbs.TryGetBulb(id, out IBulb? bulb))
            {
                continue;
            }

            for (int flavor = 0; flavor < bulb.GetFlavorCount(side); flavor++)
            {
                SizeI size = bulb.GetCellSize(side.ToSlot(), flavor, 0);
                thickness = Math.Max(thickness, horizontal ? size.Height : size.Width);
            }
        }

        foreach (Corner corner in CellSlots.Corners)
        {
            if (source.GetCorner(corner) is { } id && bulbs.TryGetBulb(id, out IBulb? bulb))
            {
                SizeI size = bulb.GetCellSize(corner.ToSlot(), 0, 0);
                thickness = Math.Max(thickness, horizontal ? size.Height : size.Width);
            }
        }

        return thickness;
    }

    /// <summary>
    /// The scale at which at least one bulb fits along a single-bulb strip: a bulb wider than its tile (2002 is 50 art
    /// pixels wide) is drawn smaller instead of leaving the tile empty.
    /// </summary>
    /// <param name="bulb">The bulb.</param>
    /// <param name="side">The strip's side.</param>
    /// <param name="length">The strip's length in device pixels.</param>
    /// <param name="scale">The scale chosen for the strip's thickness.</param>
    /// <returns>The scale, reduced when needed.</returns>
    private static double FitOneBulb(IBulb bulb, Side side, int length, double scale)
    {
        bool horizontal = side is Side.Top or Side.Bottom;
        int cell = 0;
        for (int flavor = 0; flavor < bulb.GetFlavorCount(side); flavor++)
        {
            SizeI size = bulb.GetCellSize(side.ToSlot(), flavor, 0);
            cell = Math.Max(cell, horizontal ? size.Width : size.Height);
        }

        // One bulb takes spacing + cell + spacing, each rounded on its own (ArtScale.Scale): two pixels of slack absorb it.
        int needed = cell + 2 * Math.Max(0, horizontal ? bulb.HorizontalSpacing : bulb.VerticalSpacing);
        return needed > 0 && length > 2 && needed * scale > length - 2 ? (length - 2.0) / needed : scale;
    }

    private static StripModel CreatePreviewModel(IBulbResolver bulbs, SlotAssignment source, SizeI pixelSize, StripSpec spec, IBulb bulb)
    {
        RectI window = bulb.PreviewWindow;
        double fit = Math.Min((double)pixelSize.Width / Math.Max(1, window.Width), (double)pixelSize.Height / Math.Max(1, window.Height));
        double scale = Math.Min(fit, spec.MaxArtScale * spec.PixelsPerDip);
        return new StripModel(bulbs, source, LightsLayout.Empty, pixelSize, 0, 0, spec.Style, bulb, Math.Max(scale, 0.01));
    }

    private void DrawListPreview(PremultipliedImage target, IAppServices services)
    {
        RectI window = previewBulb!.PreviewWindow;
        PremultipliedImage sprite = services.Sprites.GetSprite(previewBulb, CellSlot.Preview, 0, 0, previewScale, Style);
        var crop = new RectI(
            (int)Math.Round(window.Left * previewScale), (int)Math.Round(window.Top * previewScale),
            (int)Math.Round(window.Right * previewScale), (int)Math.Round(window.Bottom * previewScale))
            .Intersect(new RectI(0, 0, sprite.Width, sprite.Height));
        if (crop.IsEmpty)
        {
            return;
        }

        PremultipliedImage cut = sprite.Crop(crop);
        services.Compositor.Draw(target, cut, (target.Width - cut.Width) / 2, (target.Height - cut.Height) / 2, 1f, CompositeMode.SourceOver);
    }
}

/// <summary>A cheap fingerprint of a frame: the states of every bulb and the glow intensity (equal fingerprints draw equal pixels).</summary>
internal static class FrameSignature
{
    /// <summary>The fingerprint of a frame.</summary>
    /// <param name="states">One state per placement.</param>
    /// <param name="glow">The glow intensity.</param>
    /// <returns>A 64-bit FNV-1a hash of the exact values.</returns>
    public static ulong Of(IReadOnlyList<BulbVisualState> states, float glow)
    {
        ArgumentNullException.ThrowIfNull(states);
        ulong hash = 14695981039346656037UL;
        Mix(ref hash, (uint)BitConverter.SingleToInt32Bits(glow));
        Mix(ref hash, (uint)states.Count);
        for (int i = 0; i < states.Count; i++)
        {
            BulbVisualState state = states[i];
            Mix(ref hash, (uint)state.Frame);
            Mix(ref hash, (uint)BitConverter.SingleToInt32Bits(state.Brightness));
            Mix(ref hash, (uint)BitConverter.SingleToInt32Bits(state.Glow));
        }

        return hash;
    }

    private static void Mix(ref ulong hash, uint value)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash ^= (value >> shift) & 0xFF;
            hash *= 1099511628211UL;
        }
    }
}
