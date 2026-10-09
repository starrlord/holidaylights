namespace HolidayLights.Tests.Layout;

/// <summary>Structural invariants every layout must satisfy, in both frame modes.</summary>
internal static class LayoutAssertions
{
    /// <summary>
    /// Checks dense ordinals, strips and their placements, bulbs inside their strip and display, no overlapping bulbs on a
    /// display, rings covering every bulb once, and the index and chase counters.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="targets">The targets it was built for.</param>
    public static void Valid(LightsLayout layout, IReadOnlyList<LayoutTarget> targets)
    {
        Assert.Equal(targets.Count, layout.Displays.Count);
        for (int i = 0; i < layout.Placements.Count; i++)
        {
            Assert.Equal(i, layout.Placements[i].Ordinal);
        }

        var seen = new HashSet<int>();
        for (int t = 0; t < targets.Count; t++)
        {
            DisplayLayout display = layout.Displays[t];
            Assert.Same(targets[t], display.Target);
            var cells = new List<RectI>();
            for (int s = 0; s < display.Strips.Count; s++)
            {
                StripLayout strip = display.Strips[s];
                Assert.Equal(s, strip.Index);
                Assert.Equal(targets[t].DisplayId, strip.DisplayId);
                Assert.Equal(strip.Count, strip.Placements.Count(p => !p.IsCorner));
                Assert.True(strip.Count > 0 ? double.IsFinite(strip.Gap) && strip.Gap >= 0 : double.IsPositiveInfinity(strip.Gap), $"gap {strip.Gap}");
                foreach (BulbPlacement p in strip.Placements)
                {
                    Assert.Same(layout.Placements[p.Ordinal], p);
                    Assert.True(seen.Add(p.Ordinal), "a placement is in two strips");
                    Assert.Equal(s, p.StripIndex);
                    Assert.Equal(targets[t].DisplayId, p.DisplayId);
                    Assert.True(strip.Rect.Contains(p.Bounds), $"{p.Bounds} outside strip {strip.Side} {strip.Rect}");
                    Assert.True(targets[t].Area.Contains(p.Bounds), $"{p.Bounds} outside display {targets[t].Area}");
                    if (p.IsCorner)
                    {
                        Assert.True(strip.Side.IsHorizontal(), "only horizontal strips draw corners");
                        Assert.Null(p.ChaseIndex);
                    }
                    else
                    {
                        Assert.Equal(strip.Side.ToSlot(), p.Slot);
                        Assert.NotNull(p.ChaseIndex);
                    }

                    cells.Add(p.Bounds);
                }
            }

            for (int a = 0; a < cells.Count; a++)
            {
                for (int b = a + 1; b < cells.Count; b++)
                {
                    Assert.False(cells[a].IntersectsWith(cells[b]), $"{cells[a]} overlaps {cells[b]} on {targets[t].DisplayId}");
                }
            }
        }

        Assert.Equal(layout.Placements.Count, seen.Count);
        var inRings = new HashSet<int>();
        for (int r = 0; r < layout.Rings.Count; r++)
        {
            LightsRing ring = layout.Rings[r];
            Assert.Equal(r, ring.Id);
            for (int k = 0; k < ring.Ordinals.Count; k++)
            {
                BulbPlacement p = layout.Placements[ring.Ordinals[k]];
                Assert.True(inRings.Add(p.Ordinal), "a placement is in two rings");
                Assert.Equal(r, p.RingId);
                Assert.Equal(k, p.RingIndex);
            }
        }

        Assert.Equal(layout.Placements.Count, inRings.Count);
        AssertContinuousCounters(layout);
    }

    // Along each strip, continuing across the pieces of one outline edge: the type/flavor index i runs 0, 1, 2, ... and the
    // chase counter k starts at 1 and grows by 0 or 1 per side bulb (so Alternating and Bulb Chase never restart at a seam).
    private static void AssertContinuousCounters(LightsLayout layout)
    {
        IEnumerable<IGrouping<object, StripLayout>> edges = layout.Displays.SelectMany(d => d.Strips)
            .GroupBy(s => s.OutlineEdge is { } edge ? (object)edge : s);
        foreach (IGrouping<object, StripLayout> edge in edges)
        {
            BulbPlacement[] sideBulbs = [.. edge
                .OrderBy(s => s.Side.IsHorizontal() ? s.Rect.Left : s.Rect.Top)
                .SelectMany(s => s.Placements.Where(p => !p.IsCorner))];
            Assert.Equal(Enumerable.Range(0, sideBulbs.Length), sideBulbs.Select(p => p.Index));
            int expectedChase = 1;
            foreach (BulbPlacement p in sideBulbs)
            {
                Assert.True(p.ChaseIndex == expectedChase || (p != sideBulbs[0] && p.ChaseIndex == expectedChase + 1), $"chase counter {p.ChaseIndex} after {expectedChase}");
                expectedChase = p.ChaseIndex!.Value;
            }
        }
    }
}
