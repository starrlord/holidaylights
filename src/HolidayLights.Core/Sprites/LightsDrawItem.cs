namespace HolidayLights.Core.Sprites;

/// <summary>One placement of a <see cref="LightsRenderRequest"/>, resolved for drawing.</summary>
/// <param name="Placement">The placement.</param>
/// <param name="Bulb">Its bulb.</param>
/// <param name="Animation">The animation of its slot and flavor.</param>
/// <param name="State">What it shows.</param>
/// <param name="X">Left in target pixels.</param>
/// <param name="Y">Top in target pixels.</param>
/// <param name="SpriteScale">The scale its sprites are requested at: the display's S times the zoom.</param>
/// <param name="DisplayArea">Its display's laid-out area in target pixels (glow is clipped to it).</param>
internal readonly record struct LightsDrawItem(
    BulbPlacement Placement, IBulb Bulb, BulbAnimationInfo Animation, BulbVisualState State,
    int X, int Y, double SpriteScale, RectI DisplayArea)
{
    /// <summary>
    /// Resolves the placements a request draws, in placement order: placements of the requested display (or all), whose
    /// display is in the layout and whose bulb resolves (others are skipped, as on the desktop).
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The items to draw.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The zoom or an offset is not a usable number.</exception>
    /// <exception cref="ArgumentException">A required part is missing, or there are fewer states than placements.</exception>
    public static IReadOnlyList<LightsDrawItem> Collect(LightsRenderRequest request)
    {
        if (request.Layout is null || request.States is null || request.Bulbs is null || request.Sprites is null)
        {
            throw new ArgumentException("The request needs a layout, states, a bulb resolver and a sprite provider.", nameof(request));
        }

        LightsLayout layout = request.Layout;
        if (!double.IsFinite(request.Zoom) || request.Zoom <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Zoom, "The zoom must be a positive finite number.");
        }

        if (!double.IsFinite(request.OffsetX) || !double.IsFinite(request.OffsetY))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The offsets must be finite numbers.");
        }

        if (request.States.Count < layout.Placements.Count)
        {
            throw new ArgumentException(
                $"The request has {request.States.Count} states for {layout.Placements.Count} placements.", nameof(request));
        }

        var displays = new Dictionary<string, DisplayLayout>(StringComparer.Ordinal);
        foreach (DisplayLayout display in layout.Displays)
        {
            displays.TryAdd(display.Target.DisplayId, display);
        }

        var bulbs = new Dictionary<string, IBulb?>(BulbIds.Comparer);
        var items = new List<LightsDrawItem>(layout.Placements.Count);
        foreach (BulbPlacement placement in layout.Placements)
        {
            if ((request.DisplayId is not null && !string.Equals(placement.DisplayId, request.DisplayId, StringComparison.Ordinal)) ||
                !displays.TryGetValue(placement.DisplayId, out DisplayLayout? display) ||
                Resolve(request.Bulbs, bulbs, placement.BulbId) is not IBulb bulb)
            {
                continue;
            }

            items.Add(new LightsDrawItem(
                placement,
                bulb,
                bulb.GetAnimation(placement.Slot, placement.Flavor),
                request.States[placement.Ordinal],
                TargetPixels.Snap(placement.Bounds.Left, request.Zoom, request.OffsetX),
                TargetPixels.Snap(placement.Bounds.Top, request.Zoom, request.OffsetY),
                display.Target.Scale * request.Zoom,
                TargetPixels.Map(display.Target.Area, request.Zoom, request.OffsetX, request.OffsetY)));
        }

        return items;
    }

    private static IBulb? Resolve(IBulbResolver resolver, Dictionary<string, IBulb?> resolved, string id)
    {
        if (!resolved.TryGetValue(id, out IBulb? bulb))
        {
            bulb = resolver.TryGetBulb(id, out IBulb? found) ? found : null;
            resolved[id] = bulb;
        }

        return bulb;
    }
}
