using HolidayLights.Platform.Displays;

namespace HolidayLights.Tests.Platform;

public sealed class DisplayTopologyTests
{
    private const string MainId = @"\\?\DISPLAY#DEL41B8#5&1&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string LeftId = @"\\?\DISPLAY#DEL41B8#5&1&0&UID4354#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    /// <summary>The reference PC: two 3840 x 2160 displays at 150 %, the second at x = -3840, 72 px bottom taskbars.</summary>
    internal static IReadOnlyList<MonitorSample> ReferenceSamples =>
    [
        new(@"\\.\DISPLAY1", new RectI(0, 0, 3840, 2160), new RectI(0, 0, 3840, 2088), 144, IsPrimary: true),
        new(@"\\.\DISPLAY2", new RectI(-3840, 0, 0, 2160), new RectI(-3840, 0, 0, 2088), 144, IsPrimary: false),
    ];

    internal static IReadOnlyDictionary<string, DisplayIdentity> ReferenceIdentities => new Dictionary<string, DisplayIdentity>
    {
        [@"\\.\DISPLAY1"] = new(MainId, "DELL U2723QE"),
        [@"\\.\DISPLAY2"] = new(LeftId, ""),
    };

    [Fact]
    public void Build_NumbersTheMainDisplayOneAndKeepsPhysicalGeometry()
    {
        IReadOnlyList<DisplayInfo> displays = DisplayTopology.Build(ReferenceSamples, ReferenceIdentities);

        Assert.Equal(2, displays.Count);
        DisplayInfo main = displays[0];
        Assert.Equal(1, main.Number);
        Assert.True(main.IsPrimary);
        Assert.Equal(MainId, main.DeviceId);
        Assert.Equal(@"\\.\DISPLAY1", main.DeviceName);
        Assert.Equal("DELL U2723QE", main.FriendlyName);
        Assert.Equal(new RectI(0, 0, 3840, 2088), main.WorkArea);
        Assert.Equal(1.5, main.Scale);
        Assert.Equal("Display 1 - 3840 x 2160 - 150 %", main.Describe());

        DisplayInfo left = displays[1];
        Assert.Equal(2, left.Number);
        Assert.False(left.IsPrimary);
        Assert.Equal(LeftId, left.DeviceId);
        Assert.Equal("Display 2", left.FriendlyName);
        Assert.Equal(-3840, left.Bounds.Left);
    }

    [Fact]
    public void Build_OrdersTheOtherDisplaysLeftToRightThenTopToBottom()
    {
        MonitorSample[] samples =
        [
            new("C", new RectI(1920, 0, 3840, 1080), new RectI(1920, 0, 3840, 1080), 96, false),
            new("M", new RectI(0, 0, 1920, 1080), new RectI(0, 0, 1920, 1040), 96, true),
            new("B", new RectI(-1920, 1080, 0, 2160), new RectI(-1920, 1080, 0, 2160), 120, false),
            new("A", new RectI(-1920, 0, 0, 1080), new RectI(-1920, 0, 0, 1080), 96, false),
        ];

        IReadOnlyList<DisplayInfo> displays = DisplayTopology.Build(samples, new Dictionary<string, DisplayIdentity>());

        Assert.Equal(["M", "A", "B", "C"], displays.Select(d => d.DeviceName));
        Assert.Equal([1, 2, 3, 4], displays.Select(d => d.Number));
        Assert.Equal(["M", "A", "B", "C"], displays.Select(d => d.DeviceId));
    }

    [Fact]
    public void Build_KeepsDeviceIdsUniqueAndFallsBackToTheGdiName()
    {
        MonitorSample[] samples =
        [
            new("G1", new RectI(0, 0, 100, 100), new RectI(0, 0, 100, 100), 96, true),
            new("G2", new RectI(100, 0, 200, 100), new RectI(100, 0, 200, 100), 96, false),
            new("G3", new RectI(200, 0, 300, 100), new RectI(200, 0, 300, 100), 96, false),
        ];
        var identities = new Dictionary<string, DisplayIdentity>
        {
            ["G1"] = new("same", "One"),
            ["G2"] = new("same", "Two"),
            ["G3"] = new("", "Three"),
        };

        IReadOnlyList<DisplayInfo> displays = DisplayTopology.Build(samples, identities);

        Assert.Equal(3, displays.Select(d => d.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("same", displays[0].DeviceId);
        Assert.Equal("G3", displays[2].DeviceId);
        Assert.Equal("Three", displays[2].FriendlyName);
    }

    [Fact]
    public void Build_WithoutAPrimaryFlagTheFirstMonitorIsMain()
    {
        MonitorSample[] samples = [new("X", new RectI(0, 0, 10, 10), new RectI(0, 0, 10, 10), 96, false)];

        DisplayInfo only = Assert.Single(DisplayTopology.Build(samples, new Dictionary<string, DisplayIdentity>()));

        Assert.True(only.IsPrimary);
        Assert.Equal(1, only.Number);
        Assert.Empty(DisplayTopology.Build([], new Dictionary<string, DisplayIdentity>()));
    }

    [Theory]
    [InlineData(100, 100, 1)]
    [InlineData(-1, 0, 2)]
    [InlineData(-3840, 2159, 2)]
    [InlineData(3839, 2159, 1)]
    [InlineData(5000, 100, 1)]
    [InlineData(-9000, 3000, 2)]
    [InlineData(-1, -500, 2)]
    public void Nearest_FindsTheContainingOrClosestDisplay(int x, int y, int expectedNumber)
    {
        IReadOnlyList<DisplayInfo> displays = DisplayTopology.Build(ReferenceSamples, ReferenceIdentities);

        Assert.Equal(expectedNumber, DisplayTopology.Nearest(displays, new PointI(x, y)).Number);
    }
}
