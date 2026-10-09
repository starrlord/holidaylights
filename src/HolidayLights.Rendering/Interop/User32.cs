using System.Runtime.InteropServices;

namespace HolidayLights.Rendering.Interop;

/// <summary>user32.dll imports used by the Lights thread.</summary>
internal static unsafe partial class User32
{
    private const string Dll = "user32.dll";

    [LibraryImport(Dll, SetLastError = true)]
    internal static partial ushort RegisterClassExW(WNDCLASSEXW* windowClass);

    [LibraryImport(Dll, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowExW(
        uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport(Dll)]
    internal static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport(Dll)]
    internal static partial nint GetWindow(nint hwnd, uint command);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindowExW(nint parent, nint childAfter, string? className, string? windowName);

    [LibraryImport(Dll)]
    internal static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport(Dll, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetPropW(nint hwnd, string name, nint data);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetPropW(nint hwnd, string name);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint RemovePropW(nint hwnd, string name);

    [LibraryImport(Dll)]
    internal static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport(Dll)]
    internal static partial int MapWindowPoints(nint from, nint to, POINT* points, uint count);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessageW(out MSG message, nint hwnd, uint filterMin, uint filterMax, uint remove);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in MSG message);

    [LibraryImport(Dll)]
    internal static partial nint DispatchMessageW(in MSG message);

    [LibraryImport(Dll)]
    internal static partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint milliseconds, uint wakeMask, uint flags);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessageW(string name);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SendMessageCallbackW(
        nint hwnd, uint message, nint wParam, nint lParam, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, void> callback, nuint data);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(nint hdc, RECT* clip, delegate* unmanaged[Stdcall]<nint, nint, RECT*, nint, int> callback, nint data);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfoW(nint monitor, MONITORINFOEXW* info);

    [LibraryImport(Dll)]
    internal static partial nint MonitorFromPoint(POINT point, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT point);

    [LibraryImport(Dll)]
    internal static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport(Dll)]
    internal static partial nint GetForegroundWindow();

    [LibraryImport(Dll)]
    internal static partial nint SetWinEventHook(
        uint eventMin, uint eventMax, nint module, delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void> callback, uint processId, uint threadId, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(nint hook);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint data);

    [LibraryImport(Dll)]
    internal static partial int GetClassNameW(nint hwnd, char* buffer, int maxCount);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ValidateRect(nint hwnd, RECT* rect);

    [LibraryImport(Dll)]
    internal static partial int DrawTextW(nint hdc, char* text, int count, RECT* rect, uint format);

    /// <summary>Returns the window class name (empty when the window is gone).</summary>
    internal static string ClassNameOf(nint hwnd)
    {
        char* buffer = stackalloc char[256];
        int length = GetClassNameW(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    /// <summary>Returns the extended window style.</summary>
    internal static uint ExtendedStyleOf(nint hwnd) => (uint)(long)GetWindowLongPtrW(hwnd, Win32Constants.GwlExStyle);
}
