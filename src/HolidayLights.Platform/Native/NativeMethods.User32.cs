using System.Runtime.InteropServices;

namespace HolidayLights.Platform.Native;

/// <summary>Source-generated P/Invoke declarations (user32). Structures are passed by pointer, so nothing is marshalled at run time.</summary>
internal static unsafe partial class NativeMethods
{
    // Window styles, messages and indices.
    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;
    internal const uint WS_EX_NOACTIVATE = 0x08000000;
    internal const int GWLP_USERDATA = -21;
    internal const uint WM_NCCREATE = 0x0081;
    internal const uint WM_NCDESTROY = 0x0082;
    internal const uint WM_CLOSE = 0x0010;
    internal const uint WM_SETTINGCHANGE = 0x001A;
    internal const uint WM_DISPLAYCHANGE = 0x007E;
    internal const uint WM_SYSCOLORCHANGE = 0x0015;
    internal const uint WM_TIMER = 0x0113;
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint WM_DPICHANGED = 0x02E0;
    internal const uint WM_WTSSESSION_CHANGE = 0x02B1;
    internal const uint WM_APP = 0x8000;

    // SystemParametersInfo.
    internal const uint SPI_GETHIGHCONTRAST = 0x0042;
    internal const uint SPI_SETSCREENSAVETIMEOUT = 0x000F;
    internal const uint SPI_SETSCREENSAVEACTIVE = 0x0011;
    internal const uint SPI_SETWORKAREA = 0x002F;
    internal const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    internal const uint SPIF_UPDATEINIFILE = 0x01;
    internal const uint SPIF_SENDCHANGE = 0x02;
    internal const uint HCF_HIGHCONTRASTON = 0x00000001;

    // Monitors and display devices.
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;
    internal const int MDT_EFFECTIVE_DPI = 0;
    internal const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;
    internal const uint DISPLAY_DEVICE_ACTIVE = 0x00000001;
    internal const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    internal const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    internal const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    internal const int SM_CXSCREEN = 0;
    internal const int SM_CYSCREEN = 1;
    internal const int SM_REMOTESESSION = 0x1000;
    internal static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    // Hot keys and keyboard.
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const uint MAPVK_VK_TO_VSC = 0;
    internal const uint TOUNICODE_NO_STATE_CHANGE = 0x4;

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(WindowClassEx* windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nuint SetTimer(nint hwnd, nuint id, uint elapse, nint timerProc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(nint hdc, NativeRect* clip, delegate* unmanaged[Stdcall]<nint, nint, NativeRect*, nint, int> callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint monitor, MonitorInfoEx* info);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayDevices(string? device, uint deviceIndex, DisplayDevice* displayDevice, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [LibraryImport("user32.dll")]
    internal static partial int QueryDisplayConfig(
        uint flags, ref uint pathCount, DisplayConfigPathInfo* paths, ref uint modeCount, DisplayConfigModeInfo* modes, nint currentTopologyId);

    [LibraryImport("user32.dll")]
    internal static partial int DisplayConfigGetDeviceInfo(DisplayConfigDeviceInfoHeader* request);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(NativePoint* point);

    [LibraryImport("user32.dll")]
    internal static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfo(uint action, uint uiParam, void* pvParam, uint winIni);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("user32.dll")]
    internal static partial int GetKeyboardLayoutList(int count, nint* layouts);

    [LibraryImport("user32.dll")]
    internal static partial int ToUnicodeEx(uint virtualKey, uint scanCode, byte* keyState, char* buffer, int bufferSize, uint flags, nint layout);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    internal static partial uint MapVirtualKeyEx(uint code, uint mapType, nint layout);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    internal static partial int GetClassName(nint hwnd, char* buffer, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hwnd, NativeRect* rect);

    [LibraryImport("user32.dll", EntryPoint = "LoadStringW")]
    internal static partial int LoadString(nint module, uint id, char* buffer, int maxCount);

    /// <summary>The class name of a window ("" when it cannot be read).</summary>
    internal static string ClassNameOf(nint hwnd)
    {
        char* buffer = stackalloc char[256];
        int length = GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : "";
    }
}
