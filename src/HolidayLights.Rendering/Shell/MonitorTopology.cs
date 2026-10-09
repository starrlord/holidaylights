using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HolidayLights.Rendering.Interop;

namespace HolidayLights.Rendering.Shell;

/// <summary>One monitor as the Lights thread sees it (physical pixels, PerMonitorV2).</summary>
/// <param name="DeviceName">GDI device name (<c>\\.\DISPLAY1</c>).</param>
/// <param name="Bounds"><c>rcMonitor</c>.</param>
/// <param name="WorkArea"><c>rcWork</c>.</param>
/// <param name="Dpi">Effective DPI.</param>
internal readonly record struct MonitorState(string DeviceName, RectI Bounds, RectI WorkArea, int Dpi);

/// <summary>
/// The <c>{device, rcMonitor, rcWork, dpi}</c> snapshot of every monitor (PRODUCT-SPEC 5.2.3), used by the maintenance to notice
/// topology changes before the next scene arrives (layers on a vanished display are hidden until then).
/// </summary>
internal static unsafe class MonitorTopology
{
    /// <summary>Enumerates the monitors.</summary>
    public static IReadOnlyList<MonitorState> Capture()
    {
        var handles = new List<nint>();
        GCHandle gc = GCHandle.Alloc(handles);
        try
        {
            User32.EnumDisplayMonitors(0, null, &Collect, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        var monitors = new List<MonitorState>(handles.Count);
        foreach (nint monitor in handles)
        {
            if (Describe(monitor) is { } state)
            {
                monitors.Add(state);
            }
        }

        return monitors;
    }

    /// <summary>The monitor under the mouse pointer (nearest when the pointer is off-screen).</summary>
    public static MonitorState? UnderCursor()
    {
        if (!User32.GetCursorPos(out POINT cursor))
        {
            return null;
        }

        return Describe(User32.MonitorFromPoint(cursor, Win32Constants.MonitorDefaultToNearest));
    }

    private static MonitorState? Describe(nint monitor)
    {
        if (monitor == 0)
        {
            return null;
        }

        MONITORINFOEXW info = default;
        info.Size = (uint)sizeof(MONITORINFOEXW);
        if (!User32.GetMonitorInfoW(monitor, &info))
        {
            return null;
        }

        int dpi = Shcore.GetDpiForMonitor(monitor, Win32Constants.MdtEffectiveDpi, out uint dpiX, out _) >= 0 ? (int)dpiX : 96;
        string device = new(info.Device);
        return new MonitorState(device, info.Monitor.ToRectI(), info.Work.ToRectI(), dpi);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(nint monitor, nint hdc, RECT* clip, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> handles)
        {
            handles.Add(monitor);
        }

        return 1;
    }
}
