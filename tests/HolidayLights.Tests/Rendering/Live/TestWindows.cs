using System.Runtime.InteropServices;
using HolidayLights.Rendering.Interop;

namespace HolidayLights.Tests.Rendering.Live;

/// <summary>Win32 functions only the live tests use (the presenter needs none of them).</summary>
internal static partial class TestWindows
{
    /// <summary><c>GA_PARENT</c>.</summary>
    public const uint ParentAncestor = 1;

    /// <summary><c>GA_ROOT</c>.</summary>
    public const uint RootAncestor = 2;

    /// <summary><c>GW_OWNER</c>.</summary>
    public const uint OwnerWindow = 4;

    /// <summary><c>WM_CLOSE</c>.</summary>
    public const uint CloseMessage = 0x0010;

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowW(string? className, string? windowName);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    /// <summary>The window at a screen point (physical pixels).</summary>
    public static nint WindowFromPoint(POINT point) => WindowFromPoint((long)point.Y << 32 | (uint)point.X);

    /// <summary><c>WindowFromPoint</c> with the POINT passed as one 64-bit value (x in the low half).</summary>
    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(long point);
}
