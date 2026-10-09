using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Power;

/// <summary>What <see cref="FullScreenDetector"/> needs to know about top-level windows (a seam for tests).</summary>
internal interface IWindowProbe
{
    /// <summary>Gets the foreground window (0 for none).</summary>
    nint Foreground { get; }

    /// <summary>Describes a window that can be seen, or returns null when it cannot count.</summary>
    /// <param name="window">The window handle.</param>
    /// <returns>
    /// Null for a window that no longer exists, is invisible, minimized or cloaked (on another virtual desktop), or is a
    /// shell window (the desktop, the taskbars, Task View, Alt+Tab).
    /// </returns>
    WindowFacts? Inspect(nint window);
}

/// <summary>A window that can be seen.</summary>
/// <param name="Bounds">The window's extended frame bounds, physical pixels.</param>
/// <param name="IsOwn">True for a window of this process (never full screen).</param>
internal readonly record struct WindowFacts(RectI Bounds, bool IsOwn);

/// <summary>The real windows (user32 and DWM).</summary>
internal sealed unsafe class NativeWindowProbe : IWindowProbe
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",

        // Shell overlays that cover a display for a moment: Task View and Alt+Tab.
        "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
    };

    private NativeWindowProbe()
    {
    }

    /// <summary>Gets the only instance.</summary>
    public static NativeWindowProbe Instance { get; } = new();

    /// <inheritdoc />
    public nint Foreground => NativeMethods.GetForegroundWindow();

    /// <inheritdoc />
    public WindowFacts? Inspect(nint window)
    {
        // IsWindowVisible is false for a destroyed window too.
        if (window == 0 || !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window) ||
            ShellClasses.Contains(NativeMethods.ClassNameOf(window)))
        {
            return null;
        }

        int cloaked = 0;
        if (NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return null;
        }

        if (!TryGetBounds(window, out RectI bounds))
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        return new WindowFacts(bounds, processId == (uint)Environment.ProcessId);
    }

    private static bool TryGetBounds(nint window, out RectI bounds)
    {
        NativeRect rect;
        if (NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(NativeRect)) != 0)
        {
            using PhysicalPixelsScope scope = PhysicalPixelsScope.Enter();
            if (!NativeMethods.GetWindowRect(window, &rect))
            {
                bounds = default;
                return false;
            }
        }

        bounds = rect.ToRectI();
        return true;
    }
}
