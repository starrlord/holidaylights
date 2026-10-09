using HolidayLights.Core.Layout;

namespace HolidayLights.Tests.Layout;

/// <summary>
/// Every golden layout case (14 themes x 5 work areas x 5 patterns, PRODUCT-SPEC 1.4 G8, 7.5 #7): strips,
/// thicknesses, counts, gaps and every placement must be identical at scale 1. (The phases are checked by the flash tests.)
/// </summary>
public sealed class GoldenLayoutTests
{
    private static readonly ClassicLayoutEngine Engine = new();

    [Theory]
    [MemberData(nameof(GoldenLayouts.Files), MemberType = typeof(GoldenLayouts))]
    public void Layout_EqualsLayoutSim(string fileName)
    {
        GoldenTheme theme = GoldenLayouts.Read(fileName);
        Assert.Equal(25, theme.Cases.Count);
        foreach (GoldenCase golden in theme.Cases)
        {
            LightsLayout layout = Engine.Layout([new LayoutTarget("display", golden.Rect, 1.0)], theme.Arrangement, TableBulbs.Resolver);
            AssertSameLayout(golden, layout, $"{fileName} {golden.Rect} pattern {(int)golden.Pattern}");
        }
    }

    private static void AssertSameLayout(GoldenCase golden, LightsLayout layout, string label)
    {
        IReadOnlyList<StripLayout> strips = Assert.Single(layout.Displays).Strips;
        Assert.Equal(golden.Strips.Select(s => s.Side), strips.Select(s => s.Side));
        for (int s = 0; s < strips.Count; s++)
        {
            GoldenStrip expected = golden.Strips[s];
            StripLayout actual = strips[s];
            string where = $"{label}, {expected.Side} strip";
            Assert.True(expected.Count == actual.Count, $"{where}: count {actual.Count}, expected {expected.Count}");
            Assert.True(expected.Thickness == actual.Thickness, $"{where}: thickness {actual.Thickness}, expected {expected.Thickness}");
            if (expected.Count > 0)
            {
                Assert.True(expected.Gap == actual.Gap, $"{where}: gap {actual.Gap:R}, expected {expected.Gap:R}");
            }
            else
            {
                Assert.Equal(double.PositiveInfinity, actual.Gap);
            }

            if (expected.Side == Side.Right && expected.Placements.Count == 0)
            {
                // 5.4 leaves an empty right strip's rectangle at the whole free area; ours is the empty edge between the
                // horizontal strips (nothing is drawn either way).
                Assert.Equal(new RectI(expected.Rect.Right, expected.Rect.Top, expected.Rect.Right, expected.Rect.Bottom), actual.Rect);
            }
            else
            {
                Assert.True(expected.Rect == actual.Rect, $"{where}: rect {actual.Rect}, expected {expected.Rect}");
            }

            Assert.True(expected.Placements.Count == actual.Placements.Count, $"{where}: {actual.Placements.Count} placements, expected {expected.Placements.Count}");
            for (int p = 0; p < expected.Placements.Count; p++)
            {
                GoldenPlacement e = expected.Placements[p];
                BulbPlacement a = actual.Placements[p];
                Assert.True(
                    e.Slot == a.Slot && e.Index == a.Index && BulbIds.Comparer.Equals(e.BulbId, a.BulbId) && e.Flavor == a.Flavor
                        && e.Bounds == a.Bounds && e.ChaseIndex == a.ChaseIndex && a.StripIndex == s,
                    $"{where}, placement {p}: {a.Slot} #{a.Index} {a.BulbId} flavor {a.Flavor} {a.Bounds} k={a.ChaseIndex}; expected {e.Slot} #{e.Index} {e.BulbId} flavor {e.Flavor} {e.Bounds} k={e.ChaseIndex}");
            }
        }
    }
}
