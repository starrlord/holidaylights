using System.Runtime.InteropServices;

namespace HolidayLights.Rendering.Interop;

/// <summary>kernel32.dll imports.</summary>
internal static unsafe partial class Kernel32
{
    private const string Dll = "kernel32.dll";

    [LibraryImport(Dll, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWaitableTimer(nint timer, long* dueTime, int period, nint completionRoutine, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CancelWaitableTimer(nint timer);

    [LibraryImport(Dll, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateEventExW(nint attributes, string? name, uint flags, uint access);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetEvent(nint handle);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport(Dll)]
    internal static partial nint GetCurrentThread();

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetThreadTimes(nint thread, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryThreadCycleTime(nint thread, out ulong cycles);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetThreadInformation(nint thread, int informationClass, void* information, uint size);
}

/// <summary>gdi32.dll imports (text for the on-screen pill and Identify).</summary>
internal static unsafe partial class Gdi32
{
    private const string Dll = "gdi32.dll";

    [LibraryImport(Dll)]
    internal static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    [LibraryImport(Dll)]
    internal static partial nint SelectObject(nint hdc, nint gdiObject);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint gdiObject);

    [LibraryImport(Dll)]
    internal static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* info, uint usage, void** bits, nint section, uint offset);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateFontW(
        int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut,
        uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

    [LibraryImport(Dll)]
    internal static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport(Dll)]
    internal static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTextExtentPoint32W(nint hdc, char* text, int count, out SIZE size);

    [LibraryImport(Dll)]
    internal static partial uint GetGlyphIndicesW(nint hdc, char* text, int count, ushort* indices, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GdiFlush();
}

/// <summary>dwmapi.dll imports.</summary>
internal static unsafe partial class Dwmapi
{
    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);
}

/// <summary>shcore.dll imports.</summary>
internal static partial class Shcore
{
    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}

/// <summary>ole32.dll imports.</summary>
internal static unsafe partial class Ole32
{
    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(Guid* classId, nint outer, uint context, Guid* interfaceId, nint* instance);
}
