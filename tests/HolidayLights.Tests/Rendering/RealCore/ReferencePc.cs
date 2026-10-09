namespace HolidayLights.Tests.Rendering.RealCore;

/// <summary>
/// The commissioning PC of PRODUCT-SPEC 1.1: two 3840 x 2160 displays at 150 % (144 DPI), the main one at (0, 0) and the
/// second at x = -3840, each with a 72 px bottom taskbar (work area 3840 x 2088).
/// </summary>
public static class ReferencePc
{
    /// <summary>The main display.</summary>
    public static DisplayInfo Main { get; } = Display(1, 0, primary: true);

    /// <summary>The second display, left of the main one.</summary>
    public static DisplayInfo Second { get; } = Display(2, -3840, primary: false);

    /// <summary>Both displays, main first.</summary>
    public static IReadOnlyList<DisplayInfo> Displays { get; } = [Main, Second];

    private static DisplayInfo Display(int number, int left, bool primary) => new()
    {
        DeviceId = $"reference-display-{number}",
        DeviceName = $@"\\.\DISPLAY{number}",
        Number = number,
        Bounds = RectI.FromXYWH(left, 0, 3840, 2160),
        WorkArea = RectI.FromXYWH(left, 0, 3840, 2088),
        Dpi = 144,
        IsPrimary = primary,
    };
}
