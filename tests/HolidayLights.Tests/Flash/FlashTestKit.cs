using System.Diagnostics;
using HolidayLights.Core.Flash;
using HolidayLights.Core.Layout;
using HolidayLights.Tests.Layout;

namespace HolidayLights.Tests.Flash;

/// <summary>Layouts, sequencers and clock helpers shared by the flash tests.</summary>
internal static class FlashTestKit
{
    public static ClassicLayoutEngine LayoutEngine { get; } = new();

    public static FlashEngine Engine { get; } = new();

    public static string StandardBulbs => BulbIds.BuiltIn("standard-bulbs");

    /// <summary>Stopwatch ticks of a duration.</summary>
    /// <param name="milliseconds">Milliseconds.</param>
    /// <returns>Ticks.</returns>
    public static long Ms(double milliseconds) => (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000.0);

    /// <summary>Standard Bulbs on every edge and corner of a 1920 x 1032 work area: 180 light bulbs in one ring.</summary>
    /// <returns>The layout and its resolver.</returns>
    public static (LightsLayout Layout, IBulbResolver Bulbs) StandardFrame()
    {
        string id = StandardBulbs;
        var arrangement = new SlotAssignment
        {
            Top = [id], Right = [id], Bottom = [id], Left = [id],
            TopLeft = id, TopRight = id, BottomLeft = id, BottomRight = id,
        };
        FakeBulbResolver bulbs = TableBulbs.Resolver;
        return (LayoutEngine.Layout([new LayoutTarget("d", new RectI(0, 0, 1920, 1032), 1.0)], arrangement, bulbs), bulbs);
    }

    /// <summary>A frame of synthetic bulbs: light bulbs on the top and bottom, a 4-frame animation on the sides, static corners.</summary>
    /// <param name="width">Work-area width.</param>
    /// <param name="height">Work-area height.</param>
    /// <returns>The layout and its resolver.</returns>
    public static (LightsLayout Layout, IBulbResolver Bulbs) MixedFrame(int width = 800, int height = 600)
    {
        var light = new FakeBulb("addon:Light", 20, 20) { Kind = BulbAnimationKind.LightBulb, Phases = 2 };
        var animation = new FakeBulb("addon:Anim", 20, 20) { Phases = 4 };
        var holly = new FakeBulb("addon:Holly", 20, 20) { Phases = 1 };
        var bulbs = new FakeBulbResolver([light, animation, holly]);
        var arrangement = new SlotAssignment
        {
            Top = [light.Id], Bottom = [light.Id], Right = [animation.Id], Left = [animation.Id],
            TopLeft = holly.Id, TopRight = holly.Id, BottomLeft = holly.Id, BottomRight = holly.Id,
        };
        return (LayoutEngine.Layout([new LayoutTarget("d", new RectI(0, 0, width, height), 1.0)], arrangement, bulbs), bulbs);
    }

    /// <summary>Creates a sequencer.</summary>
    /// <param name="frame">Layout and resolver.</param>
    /// <param name="options">The options.</param>
    /// <returns>The sequencer at step 0.</returns>
    public static IFlashSequencer Create((LightsLayout Layout, IBulbResolver Bulbs) frame, FlashOptions options) =>
        Engine.CreateSequencer(frame.Layout, frame.Bulbs, options);

    /// <summary>Copies the current states.</summary>
    /// <param name="sequencer">The sequencer.</param>
    /// <returns>The states.</returns>
    public static BulbVisualState[] Snapshot(IFlashSequencer sequencer) => sequencer.Current.ToArray();

    /// <summary>The ordinals of the light bulbs, in ring order.</summary>
    /// <param name="sequencer">The sequencer.</param>
    /// <returns>Ordinals sorted by q.</returns>
    public static int[] LightBulbsInRingOrder(IFlashSequencer sequencer) =>
        [.. sequencer.Layout.Rings.SelectMany(r => r.Ordinals).Where(o => sequencer.GetBulb(o).Kind == BulbAnimationKind.LightBulb)];
}
