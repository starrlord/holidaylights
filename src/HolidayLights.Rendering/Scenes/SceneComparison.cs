namespace HolidayLights.Rendering.Scenes;

/// <summary>
/// The identity of a placed bulb across scenes, without the dense ordinal (which shifts when another display changes):
/// a bulb whose key is unchanged keeps its visuals; others fade in or out (PRODUCT-SPEC 4.4 "only the affected edges re-lay out").
/// </summary>
internal readonly record struct PlacementKey(string DisplayId, int StripIndex, CellSlot Slot, int Index, string BulbId, int Flavor, RectI Bounds)
{
    /// <summary>The key of a placement (bulb ids compare case-insensitively).</summary>
    public static PlacementKey Of(BulbPlacement placement) =>
        new(placement.DisplayId, placement.StripIndex, placement.Slot, placement.Index, placement.BulbId.ToUpperInvariant(), placement.Flavor, placement.Bounds);
}

/// <summary>Value comparisons of scenes, so that a rebuilt but equal scene does not restart patterns or rebuild visuals.</summary>
internal static class SceneComparison
{
    /// <summary>True when two layouts place the same bulbs in the same places with the same rings.</summary>
    public static bool SameLayout(LightsLayout a, LightsLayout b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Mode != b.Mode || !a.Placements.SequenceEqual(b.Placements) || a.Rings.Count != b.Rings.Count || a.Displays.Count != b.Displays.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Rings.Count; i++)
        {
            if (a.Rings[i].Id != b.Rings[i].Id || !a.Rings[i].Ordinals.SequenceEqual(b.Rings[i].Ordinals))
            {
                return false;
            }
        }

        for (int i = 0; i < a.Displays.Count; i++)
        {
            if (a.Displays[i].Target != b.Displays[i].Target)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the flash sequencer of <paramref name="previous"/> can keep running for <paramref name="next"/>.</summary>
    public static bool SameSequencerInputs(LightsScene previous, LightsScene next) =>
        previous.Flash == next.Flash && SameLayout(previous.Layout, next.Layout);

    /// <summary>True when the sprites of <paramref name="next"/> equal those of <paramref name="previous"/> (layout, pixels and glow on or off).</summary>
    public static bool SameSprites(LightsScene previous, LightsScene next) =>
        previous.Effects.Pixels == next.Effects.Pixels
        && (previous.Effects.Glow == GlowLevel.Off) == (next.Effects.Glow == GlowLevel.Off)
        && SameLayout(previous.Layout, next.Layout);
}
