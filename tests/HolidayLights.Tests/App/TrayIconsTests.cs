using H.NotifyIcon;
using HolidayLights.App.Tray;
using HolidayLights.Tests.Shared;
using DrawingIcon = System.Drawing.Icon;

namespace HolidayLights.Tests.App;

/// <summary>
/// The tray icons (PRODUCT-SPEC 2.2): H.NotifyIcon's <c>TaskbarIcon</c> disposes the icon it replaces, so turning the
/// lights off and on again (or switching the taskbar between light and dark) must hand it a new icon every time.
/// The <c>TaskbarIcon</c> here is never created, so nothing appears in the notification area.
/// </summary>
public sealed class TrayIconsTests
{
    [Fact]
    public void EveryIconHandedOutIsANewCopyWithItsOwnHandle()
    {
        using var icons = new TrayIcons();
        DrawingIcon first = icons.Create(lit: true, lightTaskbar: false);
        first.Dispose();
        using DrawingIcon second = icons.Create(lit: true, lightTaskbar: false);
        Assert.NotSame(first, second);
        Assert.NotEqual(IntPtr.Zero, second.Handle);
    }

    [Fact]
    public void TurningTheLightsOffAndOnAgainKeepsWorking() => StaThread.Run(() =>
    {
        using var icons = new TrayIcons();
        using var taskbar = new TaskbarIcon();

        // Lights on, off, on, off; then the taskbar turns light and dark again (each change disposes the previous icon).
        foreach ((bool lit, bool light) in new[] { (true, false), (false, false), (true, false), (false, false), (false, true), (false, false), (true, true) })
        {
            DrawingIcon icon = icons.Create(lit, light);
            taskbar.Icon = icon;
            Assert.Same(icon, taskbar.Icon);
            Assert.NotEqual(IntPtr.Zero, icon.Handle);
        }
    });
}
