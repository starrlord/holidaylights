using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HolidayLights.App.ScreenSaver.Native;

namespace HolidayLights.App.ScreenSaver.FullScreen;

/// <summary>
/// One borderless, top-most, cursor-less window that covers one display exactly (physical pixels, taskbar included). It
/// reports the input that ends the saver (<see cref="SaverExitRules"/>) and swallows <c>SC_SCREENSAVE</c>.
/// </summary>
internal sealed class SaverWindow : Window
{
    private readonly DisplayInfo display;
    private readonly PointI startCursor;
    private nint handle;

    /// <summary>Creates the window (not shown yet).</summary>
    /// <param name="display">The display to cover.</param>
    /// <param name="background">The background colour.</param>
    /// <param name="startCursor">Where the pointer was when the saver started (physical pixels).</param>
    public SaverWindow(DisplayInfo display, RgbColor background, PointI startCursor)
    {
        ArgumentNullException.ThrowIfNull(display);
        this.display = display;
        this.startCursor = startCursor;
        Title = "Holiday Lights";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = System.Windows.Input.Cursors.None;
        var brush = new SolidColorBrush(Color.FromRgb(background.R, background.G, background.B));
        brush.Freeze();
        Background = brush;
        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => Place();
    }

    /// <summary>Raised (once or more) when input or a system event ends the saver.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>The display the window covers.</summary>
    public DisplayInfo Display => display;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(Hook);
        Place();
    }

    /// <summary>Covers the display exactly, above other top-most windows (the taskbar).</summary>
    private void Place()
    {
        if (handle != 0)
        {
            RectI bounds = display.Bounds;
            SaverNativeMethods.SetWindowPos(handle, SaverNativeMethods.HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                SaverNativeMethods.SWP_NOACTIVATE | SaverNativeMethods.SWP_NOOWNERZORDER);
        }
    }

    private unsafe nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        uint id = (uint)message;
        switch (id)
        {
            case SaverNativeMethods.WM_SETCURSOR:
                SaverNativeMethods.SetCursor(0);
                handled = true;
                return 1;
            case SaverNativeMethods.WM_SYSCOMMAND when ((int)wParam & 0xFFF0) == SaverNativeMethods.SC_SCREENSAVE:
                handled = true;
                return 0;
            case SaverNativeMethods.WM_MOUSEMOVE:
                NativePoint point;
                if (SaverNativeMethods.GetCursorPos(&point) && SaverExitRules.MovedTooFar(startCursor, new PointI(point.X, point.Y), display.Scale))
                {
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                }

                return 0;
        }

        if (SaverExitRules.EndsSaver(id, wParam, lParam))
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
            handled = id is SaverNativeMethods.WM_KEYDOWN or SaverNativeMethods.WM_SYSKEYDOWN;
        }

        return 0;
    }
}
