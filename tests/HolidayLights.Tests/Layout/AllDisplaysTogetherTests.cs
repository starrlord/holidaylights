using HolidayLights.Core.Layout;

namespace HolidayLights.Tests.Layout;

/// <summary>"Frame: All Displays Together" (PRODUCT-SPEC 5.2.4, PO-1): one wreath around each group of touching displays.</summary>
public sealed class AllDisplaysTogetherTests
{
    private static readonly ClassicLayoutEngine Engine = new();
    private static readonly string Standard = BulbIds.BuiltIn("standard-bulbs");
    private static readonly string Snow = BulbIds.BuiltIn("snow-family");
    private static readonly string Holly = BulbIds.BuiltIn("jolly-holly");

    private static SlotAssignment Everywhere(string id) => new()
    {
        Top = [id], Right = [id], Bottom = [id], Left = [id],
        TopLeft = id, TopRight = id, BottomLeft = id, BottomRight = id,
    };

    [Fact]
    public void ReferencePc_OneWreathWithSplitTopAndBottomEdges()
    {
        // Two 3840 x 2160 displays at 150 %, the second at x = -3840, 72 px taskbars (PRODUCT-SPEC 5.2.4 step 8).
        LayoutTarget[] targets =
        [
            new("display1", new RectI(0, 0, 3840, 2088), 1.5),
            new("display2", new RectI(-3840, 0, 0, 2088), 1.5),
        ];
        var arrangement = Everywhere(Standard) with { Top = [Standard, Snow] };
        LightsLayout layout = Engine.Layout(targets, arrangement, TableBulbs.Resolver, FrameMode.AllDisplaysTogether);

        LightsRing ring = Assert.Single(layout.Rings);
        Assert.Equal(layout.Placements.Count, ring.Ordinals.Count);
        StripLayout[] one = [.. layout.Displays[0].Strips];
        StripLayout[] two = [.. layout.Displays[1].Strips];
        Assert.Equal([Side.Top, Side.Bottom, Side.Right], one.Select(s => s.Side));
        Assert.Equal([Side.Top, Side.Bottom, Side.Left], two.Select(s => s.Side));
        Assert.Equal(new RectI(0, 0, 3840, 48), one[0].Rect);
        Assert.Equal(new RectI(-3840, 0, 0, 48), two[0].Rect);

        // Four corners, one at each outer end of the horizontal pieces; none at the seam x = 0.
        BulbPlacement[] corners = [.. layout.Placements.Where(p => p.IsCorner)];
        Assert.Equal(4, corners.Length);
        Assert.Equal(
            [(CellSlot.TopLeft, new PointI(-3840, 0)), (CellSlot.TopRight, new PointI(3792, 0)), (CellSlot.BottomRight, new PointI(3792, 2040)), (CellSlot.BottomLeft, new PointI(-3840, 2040))],
            ring.Ordinals.Select(o => layout.Placements[o]).Where(p => p.IsCorner).Select(p => (p.Slot, p.Bounds.TopLeft)));

        // The type index continues across the seam: display 2 holds i = 0..n-1 of the top edge, display 1 continues.
        BulbPlacement[] leftPiece = [.. two[0].Placements.Where(p => !p.IsCorner)];
        BulbPlacement[] rightPiece = [.. one[0].Placements.Where(p => !p.IsCorner)];
        Assert.Equal(leftPiece.Length, rightPiece[0].Index);
        Assert.Equal(two[0].OutlineEdge, one[0].OutlineEdge);
        Assert.NotNull(one[0].OutlineEdge);

        // So does the 5.4 chase counter (1, then +1 after every multi-phase bulb), as if the edge were one strip.
        int expectedChase = 1;
        foreach (BulbPlacement p in leftPiece.Concat(rightPiece))
        {
            Assert.Equal(expectedChase, p.ChaseIndex);
            expectedChase += PhaseCount(p.BulbId) > 1 ? 1 : 0;
        }

        Assert.True(rightPiece[0].ChaseIndex > leftPiece.Length / 2);

        // Each piece is a 5.4 strip: the left piece starts after its corner, the right piece at the seam.
        Assert.Equal(-3840 + 48, leftPiece[0].Bounds.Left);
        Assert.Equal(0, rightPiece[0].Bounds.Left);

        // The vertical pieces fit between the horizontal strips: left on display 2, right on display 1.
        Assert.Equal(new RectI(3792, 48, 3840, 2040), one[2].Rect);
        Assert.Equal(new RectI(-3840, 48, -3792, 2040), two[2].Rect);
        Assert.Equal(41, one[2].Count);
        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void ReferencePc_ChaseCounterContinuesAcrossTheSeamOnEveryEdge()
    {
        // Standard Bulbs everywhere: every side bulb has two phases, so k simply counts the bulbs of each outline edge.
        LayoutTarget[] targets =
        [
            new("display1", new RectI(0, 0, 3840, 2088), 1.5),
            new("display2", new RectI(-3840, 0, 0, 2088), 1.5),
        ];
        LightsLayout layout = Engine.Layout(targets, Everywhere(Standard), TableBulbs.Resolver, FrameMode.AllDisplaysTogether);
        foreach (Side side in new[] { Side.Top, Side.Bottom })
        {
            BulbPlacement[] edge = [.. layout.Placements.Where(p => p.Slot == side.ToSlot()).OrderBy(p => p.Bounds.Left)];
            Assert.Equal(2 * 79, edge.Length);
            Assert.Equal(Enumerable.Range(1, edge.Length), edge.Select(p => p.ChaseIndex!.Value));
        }

        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void ReferencePc_RingRunsClockwiseAcrossBothDisplays()
    {
        LayoutTarget[] targets =
        [
            new("display1", new RectI(0, 0, 3840, 2088), 1.5),
            new("display2", new RectI(-3840, 0, 0, 2088), 1.5),
        ];
        LightsLayout layout = Engine.Layout(targets, Everywhere(Standard), TableBulbs.Resolver, FrameMode.AllDisplaysTogether);
        BulbPlacement[] ring = [.. layout.Rings[0].Ordinals.Select(o => layout.Placements[o])];

        // Top edge left to right over both displays, then down the right side, back along the bottom, up the left side.
        int topEnd = Array.FindIndex(ring, p => p.Slot == CellSlot.TopRight);
        Assert.True(ring.Take(topEnd).Zip(ring.Skip(1).Take(topEnd - 1)).All(pair => pair.First.Bounds.Left < pair.Second.Bounds.Left));
        int bottomStart = Array.FindIndex(ring, p => p.Slot == CellSlot.BottomRight);
        int bottomEnd = Array.FindIndex(ring, p => p.Slot == CellSlot.BottomLeft);
        Assert.True(ring[(bottomStart + 1)..bottomEnd].Zip(ring[(bottomStart + 2)..bottomEnd]).All(pair => pair.First.Bounds.Left > pair.Second.Bounds.Left));
        Assert.Equal(CellSlot.Left, ring[^1].Slot);
        Assert.True(ring[^1].Bounds.Top < ring[^2].Bounds.Top);
        Assert.Equal(2 * 79 + 2, ring.Count(p => p.Slot is CellSlot.Top or CellSlot.TopLeft or CellSlot.TopRight));
    }

    [Fact]
    public void LoneDisplay_IsFramedExactlyLikeEachDisplay()
    {
        LayoutTarget[] targets = [new("1", new RectI(-100, 20, 1820, 1052), 1.25)];
        LightsLayout each = Engine.Layout(targets, SlotAssignment.Classic54Default, TableBulbs.Resolver);
        LightsLayout together = Engine.Layout(targets, SlotAssignment.Classic54Default, TableBulbs.Resolver, FrameMode.AllDisplaysTogether);

        Assert.Equal(each.Placements.Select(Describe), together.Placements.Select(Describe));
        Assert.Equal(each.Displays[0].Strips.Select(s => (s.Side, s.Rect, s.Count, s.Gap, s.Thickness)), together.Displays[0].Strips.Select(s => (s.Side, s.Rect, s.Count, s.Gap, s.Thickness)));
        Assert.Equal(each.Rings[0].Ordinals, together.Rings[0].Ordinals);

        static string Describe(BulbPlacement p) => $"{p.Ordinal} {p.Slot} {p.Index} {p.BulbId} {p.Flavor} {p.Bounds} {p.ChaseIndex} {p.RingIndex}";
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void Gaps_OfTwoPixelsOrLessStillMakeOneWreath(int gap, int expectedRings)
    {
        LayoutTarget[] targets =
        [
            new("a", new RectI(0, 0, 1000, 700), 1.0),
            new("b", new RectI(1000 + gap, 0, 2000 + gap, 700), 1.0),
        ];
        LightsLayout layout = Engine.Layout(targets, Everywhere(Standard), TableBulbs.Resolver, FrameMode.AllDisplaysTogether);

        Assert.Equal(expectedRings, layout.Rings.Count);
        Assert.Equal(expectedRings == 1 ? 2 : 4, layout.Placements.Count(p => p.IsCorner && p.DisplayId == "a"));
        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void CornerContactOnly_IsTwoWreaths()
    {
        LayoutTarget[] targets =
        [
            new("a", new RectI(0, 0, 1000, 700), 1.0),
            new("b", new RectI(1000, 700, 2000, 1400), 1.0),
        ];
        LightsLayout layout = Engine.Layout(targets, Everywhere(Standard), TableBulbs.Resolver, FrameMode.AllDisplaysTogether);
        Assert.Equal(2, layout.Rings.Count);
        Assert.Equal(8, layout.Placements.Count(p => p.IsCorner));
        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void LShape_ConcaveCornerGetsNoCornerBulbAndStripsNeverOverlap()
    {
        // A tall display with a short one at its right, aligned at the top: the outline is an L with one concave corner.
        LayoutTarget[] targets =
        [
            new("tall", new RectI(0, 0, 1600, 1200), 1.0),
            new("short", new RectI(1600, 0, 2400, 600), 1.0),
        ];
        LightsLayout layout = Engine.Layout(targets, Everywhere(Standard), TableBulbs.Resolver, FrameMode.AllDisplaysTogether);

        // Convex corners: (0,0), (2400,0), (2400,600), (1600,1200), (0,1200); the concave one at (1600,600) gets none.
        Assert.Equal(5, layout.Placements.Count(p => p.IsCorner));
        BulbPlacement shortBottomRight = Assert.Single(layout.Placements, p => p.Slot == CellSlot.BottomRight && p.DisplayId == "short");
        Assert.Equal(new RectI(2368, 568, 2400, 600), shortBottomRight.Bounds);
        BulbPlacement tallBottomRight = Assert.Single(layout.Placements, p => p.Slot == CellSlot.BottomRight && p.DisplayId == "tall");
        Assert.Equal(new RectI(1568, 1168, 1600, 1200), tallBottomRight.Bounds);
        Assert.DoesNotContain(layout.Placements, p => p.IsCorner && p.Bounds.Contains(new PointI(1599, 599)));

        // The tall display's right side below the short one starts at the concave corner and ends above its bottom strip.
        StripLayout tallRight = Assert.Single(layout.Displays[0].Strips, s => s.Side == Side.Right);
        Assert.Equal(600, tallRight.Rect.Top);
        Assert.Equal(1168, tallRight.Rect.Bottom);

        // The short display's bottom edge runs from the concave corner to its own corner.
        StripLayout shortBottom = Assert.Single(layout.Displays[1].Strips, s => s.Side == Side.Bottom);
        Assert.Equal(1600, shortBottom.Rect.Left);
        Assert.Equal(0, shortBottom.Placements.First(p => !p.IsCorner).Index);
        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void StepBetweenDisplays_KeepsTheVerticalPieceClearOfTheBottomStrip()
    {
        // The right display reaches 24 px lower than the left one (an auto-hidden taskbar there): the exposed 24 px of its
        // left side are shorter than the bottom strip, so they hold no bulb.
        LayoutTarget[] targets =
        [
            new("left", new RectI(-1920, 0, 0, 1032), 1.0),
            new("right", new RectI(0, 0, 1920, 1056), 1.0),
        ];
        LightsLayout layout = Engine.Layout(targets, Everywhere(Standard), TableBulbs.Resolver, FrameMode.AllDisplaysTogether);

        StripLayout exposed = Assert.Single(layout.Displays[1].Strips, s => s.Side == Side.Left);
        Assert.Equal(0, exposed.Count);
        BulbPlacement bottomLeft = Assert.Single(layout.Placements, p => p.Slot == CellSlot.BottomLeft && p.DisplayId == "right");
        Assert.Equal(new RectI(0, 1024, 32, 1056), bottomLeft.Bounds);
        Assert.Equal(2, layout.Placements.Count(p => p.Slot == CellSlot.BottomLeft));
        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void MixedDpi_EachPieceUsesItsDisplaysScale()
    {
        LayoutTarget[] targets =
        [
            new("4k", new RectI(0, 0, 3840, 2088), 1.5),
            new("hd", new RectI(3840, 0, 5760, 1032), 1.0),
        ];
        LightsLayout layout = Engine.Layout(targets, Everywhere(Standard), TableBulbs.Resolver, FrameMode.AllDisplaysTogether);

        Assert.All(layout.Placements.Where(p => p.DisplayId == "4k"), p => Assert.Equal(48, p.Bounds.Height));
        Assert.All(layout.Placements.Where(p => p.DisplayId == "hd"), p => Assert.Equal(32, p.Bounds.Height));
        StripLayout hdTop = layout.Displays[1].Strips[0];
        StripLayout bigTop = layout.Displays[0].Strips[0];
        Assert.Equal(bigTop.Placements.Count(p => !p.IsCorner), hdTop.Placements.First(p => !p.IsCorner).Index);
        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void VerticallyStackedDisplays_ShareTheirSideEdges()
    {
        LayoutTarget[] targets =
        [
            new("upper", new RectI(0, -1080, 1920, 0), 1.0),
            new("lower", new RectI(0, 0, 1920, 1032), 1.0),
        ];
        LightsLayout layout = Engine.Layout(targets, Everywhere(Snow), TableBulbs.Resolver, FrameMode.AllDisplaysTogether);

        Assert.Single(layout.Rings);
        Assert.Equal([Side.Top, Side.Right, Side.Left], layout.Displays[0].Strips.Select(s => s.Side));
        Assert.Equal([Side.Bottom, Side.Right, Side.Left], layout.Displays[1].Strips.Select(s => s.Side));
        StripLayout upperRight = layout.Displays[0].Strips[1];
        StripLayout lowerRight = layout.Displays[1].Strips[1];
        Assert.Equal(0, upperRight.Rect.Bottom);
        Assert.Equal(0, lowerRight.Rect.Top);
        Assert.Equal(upperRight.Count, lowerRight.Placements[0].Index);
        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void DisplayInTheMiddleOfAnotherDisplaysTop_CutsThatTopEdgeIntoTwoPieces()
    {
        LayoutTarget[] targets =
        [
            new("wide", new RectI(0, 0, 3000, 1000), 1.0),
            new("above", new RectI(1000, -800, 2000, 0), 1.0),
        ];
        LightsLayout layout = Engine.Layout(targets, Everywhere(Standard) with { Top = [Standard, Holly] }, TableBulbs.Resolver, FrameMode.AllDisplaysTogether);

        StripLayout[] wideTops = [.. layout.Displays[0].Strips.Where(s => s.Side == Side.Top)];
        Assert.Equal(2, wideTops.Length);
        Assert.Equal(new[] { 0, 2000 }, wideTops.Select(s => s.Rect.Left));
        Assert.NotEqual(wideTops[0].OutlineEdge, wideTops[1].OutlineEdge);
        Assert.Equal(0, wideTops[1].Placements.First(p => !p.IsCorner).Index);
        Assert.Equal(6, layout.Placements.Count(p => p.IsCorner));
        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void RingOfDisplaysAroundAHole_FramesOnlyTheOuterOutline()
    {
        LayoutTarget[] targets =
        [
            new("top", new RectI(0, 0, 3000, 1000), 1.0),
            new("left", new RectI(0, 1000, 1000, 2000), 1.0),
            new("right", new RectI(2000, 1000, 3000, 2000), 1.0),
            new("bottom", new RectI(0, 2000, 3000, 3000), 1.0),
        ];
        LightsLayout layout = Engine.Layout(targets, Everywhere(Standard), TableBulbs.Resolver, FrameMode.AllDisplaysTogether);

        Assert.Single(layout.Rings);
        Assert.Equal(4, layout.Placements.Count(p => p.IsCorner));
        Assert.DoesNotContain(layout.Placements, p => p.Bounds.IntersectsWith(new RectI(900, 900, 2100, 2100)));
        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void RandomDesktops_KeepEveryInvariantInBothModes()
    {
        var random = new Random(20261008);
        string[] ids = [.. Enumerable.Range(0, 49).Select(i => TableBulbs.ByLegacyId(i).Id)];
        double[] scales = [0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 3.0];
        int merged = 0;
        int seams = 0;
        for (int run = 0; run < 60; run++)
        {
            LayoutTarget[] targets = RandomDesktop(random, scales);
            SlotAssignment arrangement = RandomArrangement(random, ids);
            foreach (FrameMode mode in new[] { FrameMode.EachDisplay, FrameMode.AllDisplaysTogether })
            {
                LightsLayout layout = Engine.Layout(targets, arrangement, TableBulbs.Resolver, mode);
                LayoutAssertions.Valid(layout, targets);
                if (mode == FrameMode.AllDisplaysTogether)
                {
                    merged += layout.Rings.Count < targets.Length ? 1 : 0;
                    seams += layout.Displays.SelectMany(d => d.Strips).GroupBy(s => s.OutlineEdge).Count(g => g.Select(s => s.DisplayId).Distinct().Count() > 1);
                }
            }
        }

        // The desktops really exercise wreaths over several displays and edges cut at seams.
        Assert.True(merged >= 15, $"only {merged} desktops formed a shared wreath");
        Assert.True(seams >= 15, $"only {seams} outline edges crossed a seam");
    }

    private static SlotAssignment RandomArrangement(Random random, string[] ids)
    {
        string[] Edge() => [.. Enumerable.Range(0, random.Next(0, 7)).Select(_ => ids[random.Next(ids.Length)])];
        string? Corner() => random.Next(4) == 0 ? null : ids[random.Next(ids.Length)];
        return new SlotAssignment
        {
            Top = Edge(), Right = Edge(), Bottom = Edge(), Left = Edge(),
            TopLeft = Corner(), TopRight = Corner(), BottomLeft = Corner(), BottomRight = Corner(),
        };
    }

    // Displays placed edge to edge like Windows arranges them (with offsets along the shared edge), each with a random
    // taskbar taken from a random side and a random scale.
    private static LayoutTarget[] RandomDesktop(Random random, double[] scales)
    {
        (int, int)[] sizes = [(1920, 1080), (2560, 1440), (3840, 2160), (1366, 768), (1280, 1024), (1080, 1920)];
        var monitors = new List<RectI>();
        (int w, int h) = sizes[random.Next(sizes.Length)];
        monitors.Add(new RectI(0, 0, w, h));
        int count = random.Next(1, 5);
        for (int attempt = 0; monitors.Count < count && attempt < 100; attempt++)
        {
            RectI anchor = monitors[random.Next(monitors.Count)];
            (w, h) = sizes[random.Next(sizes.Length)];
            int along = random.Next(2) == 0 ? 0 : random.Next(-Math.Min(w, h) / 2, Math.Min(w, h) / 2);
            RectI candidate = random.Next(4) switch
            {
                0 => RectI.FromXYWH(anchor.Right, anchor.Top + along, w, h),
                1 => RectI.FromXYWH(anchor.Left - w, anchor.Top + along, w, h),
                2 => RectI.FromXYWH(anchor.Left + along, anchor.Bottom, w, h),
                _ => RectI.FromXYWH(anchor.Left + along, anchor.Top - h, w, h),
            };
            if (!monitors.Any(m => m.IntersectsWith(candidate)))
            {
                monitors.Add(candidate);
            }
        }

        return [.. monitors.Select((m, i) =>
        {
            int bar = new[] { 0, 0, 40, 48, 72 }[random.Next(5)];
            RectI work = random.Next(4) switch
            {
                0 => m with { Bottom = m.Bottom - bar },
                1 => m with { Top = m.Top + bar },
                2 => m with { Left = m.Left + bar },
                _ => m with { Right = m.Right - bar },
            };
            return new LayoutTarget($"display{i}", work, scales[random.Next(scales.Length)]);
        })];
    }

    private static int PhaseCount(string id) =>
        TableBulbs.Resolver.TryGetBulb(id, out IBulb? bulb) ? bulb.GetPhaseCount(Side.Top) : 0;
}
