using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HolidayLights.Rendering.Interop;

namespace HolidayLights.Rendering.Shell;

/// <summary>The real window queries (Lights thread).</summary>
internal sealed unsafe class Win32ShellWindows : IShellWindows
{
    /// <summary>The shared instance.</summary>
    public static Win32ShellWindows Instance { get; } = new();

    /// <inheritdoc />
    public nint Find(nint parent, nint after, string className) => User32.FindWindowExW(parent, after, className, null);

    /// <inheritdoc />
    public bool HasNoRedirectionBitmap(nint hwnd) => (User32.ExtendedStyleOf(hwnd) & ExtendedWindowStyles.NoRedirectionBitmap) != 0;

    /// <inheritdoc />
    public uint ProcessIdOf(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint processId);
        return processId;
    }

    /// <inheritdoc />
    public IReadOnlyList<nint> TopLevelWindows()
    {
        var windows = new List<nint>(512);
        GCHandle handle = GCHandle.Alloc(windows);
        try
        {
            User32.EnumWindows(&Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return windows;
    }

    /// <summary>The children of a window in z-order (topmost first).</summary>
    public static IReadOnlyList<nint> ChildrenOf(nint parent)
    {
        var children = new List<nint>();
        for (nint child = User32.GetWindow(parent, Win32Constants.GwChild); child != 0 && children.Count < 4096; child = User32.GetWindow(child, Win32Constants.GwHwndNext))
        {
            children.Add(child);
        }

        return children;
    }

    /// <summary>The top-level z-order with the facts the layer rules need.</summary>
    public ZOrderSnapshot Snapshot(IReadOnlySet<nint> ours)
    {
        IReadOnlyList<nint> windows = TopLevelWindows();
        var entries = new List<ZOrderEntry>(windows.Count);
        foreach (nint hwnd in windows)
        {
            bool topmost = (User32.ExtendedStyleOf(hwnd) & ExtendedWindowStyles.Topmost) != 0;
            bool taskbar = topmost && User32.ClassNameOf(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
            entries.Add(new ZOrderEntry(hwnd, topmost, taskbar, ours.Contains(hwnd), User32.IsWindowVisible(hwnd)));
        }

        return new ZOrderSnapshot(entries);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(nint hwnd, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> windows)
        {
            windows.Add(hwnd);
        }

        return 1;
    }
}
