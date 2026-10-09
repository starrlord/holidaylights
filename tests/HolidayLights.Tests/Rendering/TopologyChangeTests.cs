using HolidayLights.Rendering.Shell;

namespace HolidayLights.Tests.Rendering;

/// <summary>
/// The display-change diff of the Lights thread's 2 s check (PRODUCT-SPEC 5.2.3, 5.13) on simulated snapshots: the reference
/// PC, then the changes a user can make (unplug, plug, resolution, scale, taskbar) and their effect on the layers.
/// </summary>
public sealed class TopologyChangeTests
{
    private static readonly MonitorState Main = new(@"\\.\DISPLAY1", RectI.FromXYWH(0, 0, 3840, 2160), RectI.FromXYWH(0, 0, 3840, 2088), 144);
    private static readonly MonitorState Second = new(@"\\.\DISPLAY2", RectI.FromXYWH(-3840, 0, 3840, 2160), RectI.FromXYWH(-3840, 0, 3840, 2088), 144);
    private static readonly MonitorState[] ReferencePc = [Main, Second];

    [Fact]
    public void TheSameSnapshot_InAnotherOrder_IsNoChange()
    {
        TopologyChange change = TopologyChange.Between(ReferencePc, [Second, Main]);

        Assert.True(change.IsEmpty);
        Assert.Equal("no change", change.Describe());
    }

    [Fact]
    public void UnpluggingTheSecondDisplay_RemovesItAndLeavesItsLayerStale()
    {
        MonitorState[] after = [Main];

        TopologyChange change = TopologyChange.Between(ReferencePc, after);

        Assert.False(change.IsEmpty);
        Assert.Equal([Second], change.Removed);
        Assert.Empty(change.Added);
        Assert.Empty(change.Changed);
        Assert.Equal(@"\\.\DISPLAY2 removed", change.Describe());
        Assert.Equal(["display-2"], TopologyChange.StaleDisplays(Layers(), after));
    }

    [Fact]
    public void PluggingInAThirdDisplay_AddsItWithoutTouchingTheOthers()
    {
        var third = new MonitorState(@"\\.\DISPLAY3", RectI.FromXYWH(3840, 0, 1920, 1080), RectI.FromXYWH(3840, 0, 1920, 1032), 96);

        TopologyChange change = TopologyChange.Between(ReferencePc, [Main, Second, third]);

        Assert.Equal([third], change.Added);
        Assert.Empty(change.Removed);
        Assert.Empty(change.Changed);
        Assert.Contains("added at 3840,0,5760,1080 (96 DPI)", change.Describe(), StringComparison.Ordinal);
        Assert.Empty(TopologyChange.StaleDisplays(Layers(), [Main, Second, third]));
    }

    [Fact]
    public void ALowerResolution_ChangesTheBoundsAndMakesTheLayerStale()
    {
        MonitorState smaller = Main with { Bounds = RectI.FromXYWH(0, 0, 2560, 1440), WorkArea = RectI.FromXYWH(0, 0, 2560, 1392) };

        TopologyChange change = TopologyChange.Between(ReferencePc, [smaller, Second]);

        (MonitorState before, MonitorState after) = Assert.Single(change.Changed);
        Assert.Equal(Main, before);
        Assert.Equal(smaller, after);
        Assert.Equal(["display-1"], TopologyChange.StaleDisplays(Layers(), [smaller, Second]));
    }

    [Fact]
    public void AScaleChange_IsAChangeButTheLayerStillFitsItsDisplay()
    {
        // 150 % -> 175 %: the layer keeps covering the same physical pixels until the new scene (new sprite sizes) arrives.
        MonitorState rescaled = Main with { Dpi = 168 };

        TopologyChange change = TopologyChange.Between(ReferencePc, [rescaled, Second]);

        Assert.Single(change.Changed);
        Assert.Equal(@"\\.\DISPLAY1 DPI 144 -> 168", change.Describe());
        Assert.Empty(TopologyChange.StaleDisplays(Layers(), [rescaled, Second]));
    }

    [Fact]
    public void MovingTheTaskbar_ChangesOnlyTheWorkArea()
    {
        MonitorState taskbarOnTop = Main with { WorkArea = RectI.FromXYWH(0, 72, 3840, 2088) };

        TopologyChange change = TopologyChange.Between(ReferencePc, [taskbarOnTop, Second]);

        Assert.Equal(@"\\.\DISPLAY1 work area 0,0,3840,2088 -> 0,72,3840,2160", change.Describe());
        Assert.Empty(TopologyChange.StaleDisplays(Layers(), [taskbarOnTop, Second]));
    }

    [Fact]
    public void SwappingTheMainDisplay_MovesBothDisplays()
    {
        // The second display becomes the main one: the virtual screen origin moves and both rectangles change.
        MonitorState[] swapped = [Main with { Bounds = RectI.FromXYWH(3840, 0, 3840, 2160), WorkArea = RectI.FromXYWH(3840, 0, 3840, 2088) }, Second with { Bounds = RectI.FromXYWH(0, 0, 3840, 2160), WorkArea = RectI.FromXYWH(0, 0, 3840, 2088) }];

        TopologyChange change = TopologyChange.Between(ReferencePc, swapped);

        Assert.Equal(2, change.Changed.Count);

        // Display 1's old rectangle (0,0) is now shown by display 2, so only display 2's layer (at -3840) is stale.
        Assert.Equal(["display-2"], TopologyChange.StaleDisplays(Layers(), swapped));
    }

    [Fact]
    public void ARenamedDevice_IsARemovalAndAnAddition()
    {
        MonitorState renamed = Second with { DeviceName = @"\\.\DISPLAY5" };

        TopologyChange change = TopologyChange.Between(ReferencePc, [Main, renamed]);

        Assert.Equal([renamed], change.Added);
        Assert.Equal([Second], change.Removed);
        Assert.Empty(TopologyChange.StaleDisplays(Layers(), [Main, renamed]));
    }

    [Fact]
    public void EveryDisplayGone_LeavesEveryLayerStale() =>
        Assert.Equal(["display-1", "display-2"], TopologyChange.StaleDisplays(Layers(), []).Order(StringComparer.Ordinal));

    /// <summary>The layers of the reference PC's scene.</summary>
    private static (string, RectI)[] Layers() => [("display-1", Main.Bounds), ("display-2", Second.Bounds)];
}
