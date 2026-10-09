using HolidayLights.Platform.Displays;

namespace HolidayLights.Tests.Platform;

public sealed class DisplayChangesTests
{
    [Fact]
    public void Compute_EqualTuplesAreNoChangeEvenWhenNumbersOrNamesDiffer()
    {
        DisplayInfo a = Display("a", 1, new RectI(0, 0, 100, 100));
        DisplayInfo renamed = a with { Number = 2, FriendlyName = "Other", DeviceName = @"\\.\DISPLAY9" };

        DisplayChanges changes = DisplayChanges.Compute([a], [renamed]);

        Assert.True(changes.IsEmpty);
        Assert.Empty(changes.AffectedIds);
    }

    [Fact]
    public void Compute_ReportsAddedRemovedAndChangedDisplays()
    {
        DisplayInfo kept = Display("kept", 1, new RectI(0, 0, 100, 100));
        DisplayInfo removed = Display("removed", 2, new RectI(100, 0, 200, 100));
        DisplayInfo resized = Display("resized", 3, new RectI(200, 0, 300, 100));
        DisplayInfo rescaled = Display("rescaled", 4, new RectI(300, 0, 400, 100));
        DisplayInfo taskbarMoved = Display("taskbar", 5, new RectI(400, 0, 500, 100));
        DisplayInfo added = Display("added", 6, new RectI(500, 0, 600, 100));

        DisplayChanges changes = DisplayChanges.Compute(
            [kept, removed, resized, rescaled, taskbarMoved],
            [
                kept,
                resized with { Bounds = new RectI(200, 0, 320, 100) },
                rescaled with { Dpi = 144 },
                taskbarMoved with { WorkArea = new RectI(400, 40, 500, 100) },
                added,
            ]);

        Assert.False(changes.IsEmpty);
        Assert.Equal(["added"], changes.Added.Select(d => d.DeviceId));
        Assert.Equal(["removed"], changes.Removed.Select(d => d.DeviceId));
        Assert.Equal(["resized", "rescaled", "taskbar"], changes.Changed.Select(d => d.DeviceId));
        Assert.Equal(144, changes.Changed.Single(d => d.DeviceId == "rescaled").Dpi);
        Assert.True(changes.AffectedIds.SetEquals(["added", "removed", "resized", "rescaled", "taskbar"]));
    }

    [Fact]
    public void Compute_MatchesDeviceIdsIgnoringCase()
    {
        DisplayInfo lower = Display(@"\\?\display#abc", 1, new RectI(0, 0, 10, 10));
        DisplayInfo upper = lower with { DeviceId = @"\\?\DISPLAY#ABC" };

        Assert.True(DisplayChanges.Compute([lower], [upper]).IsEmpty);
    }

    internal static DisplayInfo Display(string id, int number, RectI bounds) => new()
    {
        DeviceId = id,
        DeviceName = $@"\\.\DISPLAY{number}",
        Number = number,
        Bounds = bounds,
        WorkArea = bounds,
        Dpi = 96,
        IsPrimary = number == 1,
    };
}
