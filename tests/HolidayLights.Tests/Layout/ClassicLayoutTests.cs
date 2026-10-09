using HolidayLights.Core.Layout;

namespace HolidayLights.Tests.Layout;

/// <summary>The 5.4 layout on scaled inputs (PRODUCT-SPEC 5.3), Each Display, rings and edge cases.</summary>
public sealed class ClassicLayoutTests
{
    private static readonly ClassicLayoutEngine Engine = new();
    private static readonly string Standard = BulbIds.BuiltIn("standard-bulbs");
    private static readonly string Snow = BulbIds.BuiltIn("snow-family");
    private static readonly string Holly = BulbIds.BuiltIn("jolly-holly");

    private static SlotAssignment StandardEverywhere => new()
    {
        Top = [Standard], Right = [Standard], Bottom = [Standard], Left = [Standard],
        TopLeft = Standard, TopRight = Standard, BottomLeft = Standard, BottomRight = Standard,
    };

    [Fact]
    public void ReferencePc_StandardBulbsAreFortyEightPixelsWithSeventyEightOnTopAndFortyOneOnEachSide()
    {
        // PRODUCT-SPEC 5.3.2: S = 1.5, work area 3840 x 2088.
        var target = new LayoutTarget("1", new RectI(0, 0, 3840, 2088), ArtScale.Effective(144 / 96.0, BulbSize.Standard));
        LightsLayout layout = Engine.Layout([target], StandardEverywhere, TableBulbs.Resolver);

        StripLayout[] strips = [.. layout.Displays[0].Strips];
        Assert.Equal([Side.Top, Side.Bottom, Side.Right, Side.Left], strips.Select(s => s.Side));
        Assert.All(layout.Placements, p => Assert.Equal(new SizeI(48, 48), p.Bounds.Size));
        Assert.Equal(78, strips[0].Count);
        Assert.Equal(0.0, strips[0].Gap);
        Assert.Equal(78, strips[1].Count);
        Assert.Equal(41, strips[2].Count);
        Assert.Equal(24.0 / 41, strips[2].Gap);
        Assert.Equal(41, strips[3].Count);
        Assert.Equal(new RectI(0, 48, 48, 2040), strips[3].Rect);
        Assert.Equal(new RectI(3792, 48, 3840, 2040), strips[2].Rect);
        LayoutAssertions.Valid(layout, [target]);
    }

    [Fact]
    public void LayoutFidelityScenario_MatchesTheNumbersOfAcceptanceSeven()
    {
        // PRODUCT-SPEC 7.5 #7: 1920 x 1080 at 100 % with a 48 px taskbar.
        var target = new LayoutTarget("1", new RectI(0, 0, 1920, 1032), 1.0);
        LightsLayout classic = Engine.Layout([target], SlotAssignment.Classic54Default, TableBulbs.Resolver);
        Assert.Equal(54, classic.Displays[0].Strips[1].Count);
        Assert.Equal(20.0 / 54, classic.Displays[0].Strips[1].Gap);
        Assert.Equal(28, classic.Displays[0].Strips[2].Count);
        Assert.Equal(0.5714, classic.Displays[0].Strips[2].Gap, 4);

        LightsLayout standard = Engine.Layout([target], StandardEverywhere, TableBulbs.Resolver);
        Assert.Equal(58, standard.Displays[0].Strips[0].Count);
        Assert.Equal(30, standard.Displays[0].Strips[2].Count);
        Assert.Equal(8.0 / 30, standard.Displays[0].Strips[2].Gap);
    }

    [Fact]
    public void Scale_RoundsEveryCellAndSpacingBeforeTheClassicAlgorithmRuns()
    {
        // Snow Family: 32 x 32 with spacing 2. At S = 1.25 the cell is round(40) = 40 and the spacing round(2.5) = 3.
        var arrangement = new SlotAssignment { Top = [Snow] };
        LightsLayout layout = Engine.Layout([new LayoutTarget("1", new RectI(0, 0, 1000, 700), 1.25)], arrangement, TableBulbs.Resolver);
        StripLayout top = layout.Displays[0].Strips[0];

        Assert.Equal(21, top.Count); // 46 px per snowman: 21 x 46 = 966, leftover 34
        Assert.Equal(34.0 / 21, top.Gap);
        Assert.Equal(new RectI(3, 0, 43, 40), top.Placements[0].Bounds);
        Assert.Equal(40, top.Thickness);

        // The second bulb starts at trunc((3 + 3) + 40 + gap + 3) = 50.
        Assert.Equal(50, top.Placements[1].Bounds.Left);
    }

    [Fact]
    public void Placements_CycleTypesAndFlavorsAndAlignToTheScreenEdges()
    {
        var arrangement = new SlotAssignment { Top = [Standard, Snow], Bottom = [Standard, Snow], Right = [Holly], Left = [Snow] };
        var target = new LayoutTarget("1", new RectI(100, 50, 900, 650), 1.0);
        LightsLayout layout = Engine.Layout([target], arrangement, TableBulbs.Resolver);

        StripLayout top = layout.Displays[0].Strips[0];
        Assert.Equal([Standard, Snow, Standard, Snow], top.Placements.Take(4).Select(p => p.BulbId));
        Assert.Equal([0, 0, 1, 1, 2, 2], top.Placements.Take(6).Select(p => p.Flavor));
        Assert.All(top.Placements, p => Assert.Equal(50, p.Bounds.Top));
        StripLayout bottom = layout.Displays[0].Strips[1];
        Assert.All(bottom.Placements, p => Assert.Equal(650, p.Bounds.Bottom));
        StripLayout right = layout.Displays[0].Strips[2];
        Assert.All(right.Placements, p => Assert.Equal(900, p.Bounds.Right));
        StripLayout left = layout.Displays[0].Strips[3];
        Assert.All(left.Placements, p => Assert.Equal(100, p.Bounds.Left));

        // The chase counter skips the single-phase snowmen: Standard 1, Snow 2, Standard 2, Snow 3, ...
        Assert.Equal([1, 2, 2, 3, 3, 4], top.Placements.Take(6).Select(p => p.ChaseIndex));
        LayoutAssertions.Valid(layout, [target]);
    }

    [Fact]
    public void Ring_RunsClockwiseFromTheTopLeftCorner()
    {
        var target = new LayoutTarget("1", new RectI(0, 0, 640, 480), 1.0);
        LightsLayout layout = Engine.Layout([target], StandardEverywhere, TableBulbs.Resolver);
        IReadOnlyList<StripLayout> strips = layout.Displays[0].Strips;
        BulbPlacement Corner(Side side, int end) => strips[side == Side.Top ? 0 : 1].Placements.Single(p => p.IsCorner && p.Index == end);
        IEnumerable<int> Bulbs(int strip) => strips[strip].Placements.Where(p => !p.IsCorner).Select(p => p.Ordinal);

        int[] expected =
        [
            Corner(Side.Top, 0).Ordinal, .. Bulbs(0),
            Corner(Side.Top, 1).Ordinal, .. Bulbs(2),
            Corner(Side.Bottom, 1).Ordinal, .. Bulbs(1).Reverse(),
            Corner(Side.Bottom, 0).Ordinal, .. Bulbs(3).Reverse(),
        ];
        LightsRing ring = Assert.Single(layout.Rings);
        Assert.Equal(expected, ring.Ordinals);
        LayoutAssertions.Valid(layout, [target]);
    }

    [Fact]
    public void EachDisplay_FramesEveryDisplayCompletelyAtItsOwnScale()
    {
        LayoutTarget[] targets =
        [
            new("1", new RectI(0, 0, 3840, 2088), 1.5),
            new("2", new RectI(-1920, 0, 0, 1032), 1.0),
        ];
        LightsLayout layout = Engine.Layout(targets, StandardEverywhere, TableBulbs.Resolver);

        Assert.Equal(FrameMode.EachDisplay, layout.Mode);
        Assert.Equal(2, layout.Rings.Count);
        Assert.All(layout.Displays, d => Assert.Equal(4, d.Strips.Count));
        Assert.All(layout.Displays.SelectMany(d => d.Strips), s => Assert.Null(s.OutlineEdge));
        Assert.All(layout.Placements.Where(p => p.DisplayId == "1"), p => Assert.Equal(48, p.Bounds.Width));
        Assert.All(layout.Placements.Where(p => p.DisplayId == "2"), p => Assert.Equal(32, p.Bounds.Width));
        Assert.Equal(58, layout.Displays[1].Strips[0].Count);
        Assert.All(layout.Placements.Where(p => p.DisplayId == "2"), p => Assert.Equal(1, p.RingId));
        LayoutAssertions.Valid(layout, targets);
    }

    [Fact]
    public void UnknownIds_AreSkippedLike54Compacting()
    {
        var arrangement = new SlotAssignment { Top = ["addon:Missing", Standard, "user:Gone"], TopLeft = "addon:Missing", TopRight = Holly };
        var target = new LayoutTarget("1", new RectI(0, 0, 640, 480), 1.0);
        LightsLayout layout = Engine.Layout([target], arrangement, TableBulbs.Resolver);
        StripLayout top = layout.Displays[0].Strips[0];

        Assert.All(top.Placements.Where(p => !p.IsCorner), p => Assert.Equal(Standard, p.BulbId));
        BulbPlacement corner = Assert.Single(top.Placements, p => p.IsCorner);
        Assert.Equal(CellSlot.TopRight, corner.Slot);
        Assert.Equal(19, top.Count); // 640 - 32 for the right corner = 608 = 19 x 32
    }

    [Fact]
    public void EmptyInputs_GiveEmptyStripsAndNoRings()
    {
        var target = new LayoutTarget("1", new RectI(0, 0, 640, 480), 1.0);
        LightsLayout blank = Engine.Layout([target], SlotAssignment.Empty, TableBulbs.Resolver);
        Assert.Empty(blank.Placements);
        Assert.Equal(4, blank.Displays[0].Strips.Count);
        Assert.All(blank.Displays[0].Strips, s => Assert.Equal(0, s.Thickness));
        Assert.All(blank.Displays[0].Strips, s => Assert.Equal(double.PositiveInfinity, s.Gap));
        Assert.Equal(new RectI(640, 0, 640, 480), blank.Displays[0].Strips[2].Rect);
        Assert.Empty(Assert.Single(blank.Rings).Ordinals);

        LightsLayout none = Engine.Layout([], StandardEverywhere, TableBulbs.Resolver);
        Assert.Empty(none.Displays);
        Assert.Empty(none.Rings);

        LightsLayout degenerate = Engine.Layout([new LayoutTarget("0", default, 1.0)], StandardEverywhere, TableBulbs.Resolver);
        Assert.Empty(Assert.Single(degenerate.Displays).Strips);
        Assert.Empty(degenerate.Placements);
    }

    [Fact]
    public void Layout_RejectsInvalidScalesAndNullArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Engine.Layout([new LayoutTarget("1", new RectI(0, 0, 10, 10), 0)], StandardEverywhere, TableBulbs.Resolver));
        Assert.Throws<ArgumentOutOfRangeException>(() => Engine.Layout([new LayoutTarget("1", new RectI(0, 0, 10, 10), double.NaN)], StandardEverywhere, TableBulbs.Resolver));
        Assert.Throws<ArgumentNullException>(() => Engine.Layout(null!, StandardEverywhere, TableBulbs.Resolver));
        Assert.Throws<ArgumentNullException>(() => Engine.Layout([], null!, TableBulbs.Resolver));
        Assert.Throws<ArgumentNullException>(() => Engine.Layout([], StandardEverywhere, null!));
    }

    [Fact]
    public void Layout_IsIndependentOfTheBulbIdCasing()
    {
        var target = new LayoutTarget("1", new RectI(0, 0, 640, 480), 1.0);
        var upper = new SlotAssignment { Top = [Standard.ToUpperInvariant()] };
        LightsLayout layout = Engine.Layout([target], upper, TableBulbs.Resolver);
        Assert.Equal(20, layout.Displays[0].Strips[0].Count);
    }
}
