namespace HolidayLights.Tests.Rendering.Fakes;

/// <summary>
/// A simple stand-in for core-layout: one bulb type along each edge of each display's work area (cells of 32 art pixels
/// scaled by S, left-aligned, no gap distribution) with a corner bulb in each corner, and the clockwise ring of PRODUCT-SPEC 5.7.
/// </summary>
internal static class FakeLayout
{
    /// <summary>Lays out <paramref name="sideBulb"/> on the edges and <paramref name="cornerBulb"/> in the corners of each display.</summary>
    public static LightsLayout Build(IReadOnlyList<DisplayInfo> displays, string sideBulb, string? cornerBulb, BulbSize size = BulbSize.Standard, int flavors = 5)
    {
        var placements = new List<BulbPlacement>();
        var rings = new List<LightsRing>();
        var displayLayouts = new List<DisplayLayout>();
        foreach (DisplayInfo display in displays)
        {
            double scale = ArtScale.Effective(display.Scale, size);
            int cell = ArtScale.Scale(32, scale);
            RectI area = display.WorkArea;
            int ringId = rings.Count;
            var strips = new List<StripLayout>();
            var ring = new List<int>();
            var top = new List<BulbPlacement>();
            var bottom = new List<BulbPlacement>();
            var right = new List<BulbPlacement>();
            var left = new List<BulbPlacement>();

            BulbPlacement Place(int strip, CellSlot slot, int index, string bulb, int flavor, int x, int y) => new()
            {
                Ordinal = 0,
                DisplayId = display.DeviceId,
                StripIndex = strip,
                Slot = slot,
                Index = index,
                BulbId = bulb,
                Flavor = flavor,
                Bounds = RectI.FromXYWH(x, y, cell, cell),
                ChaseIndex = slot.IsCorner() ? null : index + 1,
                RingId = ringId,
                RingIndex = 0,
            };

            int horizontal = (area.Width - 2 * cell) / cell;
            int vertical = (area.Height - 2 * cell) / cell;
            if (cornerBulb is not null)
            {
                top.Add(Place(0, CellSlot.TopLeft, 0, cornerBulb, 0, area.Left, area.Top));
                top.Add(Place(0, CellSlot.TopRight, 1, cornerBulb, 0, area.Right - cell, area.Top));
                bottom.Add(Place(1, CellSlot.BottomLeft, 0, cornerBulb, 0, area.Left, area.Bottom - cell));
                bottom.Add(Place(1, CellSlot.BottomRight, 1, cornerBulb, 0, area.Right - cell, area.Bottom - cell));
            }

            for (int i = 0; i < horizontal; i++)
            {
                top.Add(Place(0, CellSlot.Top, i, sideBulb, i % flavors, area.Left + cell + i * cell, area.Top));
                bottom.Add(Place(1, CellSlot.Bottom, i, sideBulb, i % flavors, area.Left + cell + i * cell, area.Bottom - cell));
            }

            for (int i = 0; i < vertical; i++)
            {
                right.Add(Place(2, CellSlot.Right, i, sideBulb, i % flavors, area.Right - cell, area.Top + cell + i * cell));
                left.Add(Place(3, CellSlot.Left, i, sideBulb, i % flavors, area.Left, area.Top + cell + i * cell));
            }

            // Ordinals in strip build order (Top, Bottom, Right, Left), placements corners first.
            var ordered = new List<List<BulbPlacement>> { top, bottom, right, left };
            var withOrdinals = new List<List<BulbPlacement>>();
            foreach (List<BulbPlacement> strip in ordered)
            {
                var numbered = new List<BulbPlacement>();
                foreach (BulbPlacement placement in strip)
                {
                    numbered.Add(placement with { Ordinal = placements.Count + numbered.Count + withOrdinals.Sum(s => s.Count) });
                }

                withOrdinals.Add(numbered);
            }

            // Clockwise ring: TL, top L->R, TR, right T->B, BR, bottom R->L, BL, left B->T.
            BulbPlacement? Corner(List<BulbPlacement> strip, CellSlot slot) => strip.FirstOrDefault(p => p.Slot == slot);
            void Add(BulbPlacement? placement)
            {
                if (placement is not null)
                {
                    ring.Add(placement.Ordinal);
                }
            }

            Add(Corner(withOrdinals[0], CellSlot.TopLeft));
            withOrdinals[0].Where(p => p.Slot == CellSlot.Top).ToList().ForEach(p => Add(p));
            Add(Corner(withOrdinals[0], CellSlot.TopRight));
            withOrdinals[2].ForEach(p => Add(p));
            Add(Corner(withOrdinals[1], CellSlot.BottomRight));
            withOrdinals[1].Where(p => p.Slot == CellSlot.Bottom).Reverse().ToList().ForEach(p => Add(p));
            Add(Corner(withOrdinals[1], CellSlot.BottomLeft));
            Enumerable.Reverse(withOrdinals[3]).ToList().ForEach(p => Add(p));

            var ringIndex = ring.Select((ordinal, index) => (ordinal, index)).ToDictionary(x => x.ordinal, x => x.index);
            for (int s = 0; s < withOrdinals.Count; s++)
            {
                List<BulbPlacement> final = [.. withOrdinals[s].Select(p => p with { RingIndex = ringIndex[p.Ordinal] })];
                placements.AddRange(final);
                Side side = s switch { 0 => Side.Top, 1 => Side.Bottom, 2 => Side.Right, _ => Side.Left };
                strips.Add(new StripLayout
                {
                    Index = s,
                    DisplayId = display.DeviceId,
                    Side = side,
                    Rect = final.Count == 0 ? default : final.Select(p => p.Bounds).Aggregate((a, b) => a.Union(b)),
                    Thickness = cell,
                    Count = final.Count(p => !p.IsCorner),
                    Gap = 0,
                    Placements = final,
                });
            }

            rings.Add(new LightsRing(ringId, ring));
            displayLayouts.Add(new DisplayLayout(new LayoutTarget(display.DeviceId, area, scale), strips));
        }

        return new LightsLayout
        {
            Mode = FrameMode.EachDisplay,
            Arrangement = SlotAssignment.Empty,
            Displays = displayLayouts,
            Placements = [.. placements.OrderBy(p => p.Ordinal)],
            Rings = rings,
        };
    }

    /// <summary>A scene for the displays with that layout.</summary>
    public static LightsScene Scene(IReadOnlyList<DisplayInfo> displays, LightsLayout layout, LayerMode layer, FlashPatternId pattern = FlashPatternId.FlashTogether, int interval = 5, GlowLevel glow = GlowLevel.Soft, bool lightsOn = true) => new()
    {
        Revision = 1,
        LightsOn = lightsOn,
        RequestedLayer = layer,
        Displays = [.. displays.Select(d => new DisplayScene(d, true))],
        Layout = layout,
        Flash = new FlashOptions { Pattern = pattern, SmoothFading = true },
        Interval = interval,
        Effects = new SceneEffects { Pixels = SpriteStyle.Smooth, Glow = glow },
    };
}
