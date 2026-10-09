using System.Runtime.InteropServices;

namespace HolidayLights.App.ScreenSaver.Native;

/// <summary>
/// Source-generated P/Invoke declarations of the screen saver: window placement, input polling, the <c>/p</c> preview
/// child window and its GDI presentation. Structures are passed by pointer, so nothing is marshalled at run time.
/// </summary>
internal static unsafe partial class SaverNativeMethods
{
    // Messages.
    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_PAINT = 0x000F;
    internal const uint WM_ERASEBKGND = 0x0014;
    internal const uint WM_SETCURSOR = 0x0020;
    internal const uint WM_DISPLAYCHANGE = 0x007E;
    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_SYSKEYDOWN = 0x0104;
    internal const uint WM_SYSCOMMAND = 0x0112;
    internal const uint WM_TIMER = 0x0113;
    internal const uint WM_MOUSEMOVE = 0x0200;
    internal const uint WM_LBUTTONDOWN = 0x0201;
    internal const uint WM_RBUTTONDOWN = 0x0204;
    internal const uint WM_MBUTTONDOWN = 0x0207;
    internal const uint WM_MOUSEWHEEL = 0x020A;
    internal const uint WM_XBUTTONDOWN = 0x020B;
    internal const uint WM_MOUSEHWHEEL = 0x020E;
    internal const uint WM_POWERBROADCAST = 0x0218;
    internal const uint WM_POINTERDOWN = 0x0246;

    // WM_SYSCOMMAND and WM_POWERBROADCAST values.
    internal const int SC_SCREENSAVE = 0xF140;
    internal const int PBT_APMSUSPEND = 0x0004;
    internal const int PBT_APMRESUMESUSPEND = 0x0007;
    internal const int PBT_APMRESUMEAUTOMATIC = 0x0012;

    // Window styles and SetWindowPos.
    internal const uint WS_CHILD = 0x40000000;
    internal const uint WS_VISIBLE = 0x10000000;
    internal const uint WS_CLIPCHILDREN = 0x02000000;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOOWNERZORDER = 0x0200;
    internal static readonly nint HWND_TOPMOST = -1;

    // GDI.
    internal const uint DIB_RGB_COLORS = 0;
    internal const uint BI_RGB = 0;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(NativePoint* point);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll")]
    internal static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint hwnd, NativeRect* rect);

    [LibraryImport("user32.dll")]
    internal static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll")]
    internal static partial nint GetWindowDpiAwarenessContext(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(NativeWindowClass* windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    internal static partial int GetMessage(NativeMessage* message, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(NativeMessage* message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(NativeMessage* message);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    internal static partial nuint SetTimer(nint hwnd, nuint id, uint milliseconds, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll")]
    internal static partial nint BeginPaint(nint hwnd, NativePaint* paint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndPaint(nint hwnd, NativePaint* paint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InvalidateRect(nint hwnd, NativeRect* rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? moduleName);

    /// <summary><c>WaitForMultipleObjects</c> result of the first handle.</summary>
    internal const uint WAIT_OBJECT_0 = 0;

    /// <summary>
    /// Waits for the next frame of the compositor clock (Windows 11) or one of the handles. Returns <c>WAIT_OBJECT_0 + i</c>
    /// for handle i, <c>WAIT_OBJECT_0 + count</c> for a clock tick, or <c>WAIT_TIMEOUT</c>.
    /// </summary>
    [LibraryImport("dcomp.dll")]
    internal static partial uint DCompositionWaitForCompositorClock(uint count, [In] nint[] handles, uint timeoutMilliseconds);

    [LibraryImport("gdi32.dll")]
    internal static partial int SetDIBitsToDevice(
        nint hdc, int x, int y, uint width, uint height, int sourceX, int sourceY, uint startScan, uint scanLines,
        void* bits, NativeBitmapInfoHeader* info, uint colorUse);
}

/// <summary><c>POINT</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}

/// <summary><c>RECT</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

/// <summary><c>MSG</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    public nint Hwnd;
    public uint Message;
    public nint WParam;
    public nint LParam;
    public uint Time;
    public NativePoint Point;
    public uint Private;
}

/// <summary><c>PAINTSTRUCT</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativePaint
{
    public nint Hdc;
    public int Erase;
    public NativeRect Paint;
    public int Restore;
    public int IncUpdate;
    public fixed byte Reserved[32];
}

/// <summary><c>BITMAPINFOHEADER</c> (a 32 bpp, top-down DIB needs no colour table).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeBitmapInfoHeader
{
    public uint Size;
    public int Width;
    public int Height;
    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ClrUsed;
    public uint ClrImportant;
}

/// <summary><c>WNDCLASSEXW</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeWindowClass
{
    public uint Size;
    public uint Style;
    public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> WindowProc;
    public int ClassExtra;
    public int WindowExtra;
    public nint Instance;
    public nint Icon;
    public nint Cursor;
    public nint Background;
    public char* MenuName;
    public char* ClassName;
    public nint SmallIcon;
}
