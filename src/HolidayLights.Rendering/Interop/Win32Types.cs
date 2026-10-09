using System.Runtime.InteropServices;

namespace HolidayLights.Rendering.Interop;

/// <summary>Win32 <c>RECT</c> (right and bottom exclusive).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;

    public static RECT From(RectI rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public readonly RectI ToRectI() => new(Left, Top, Right, Bottom);
}

/// <summary>Win32 <c>POINT</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>Win32 <c>SIZE</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int Cx;
    public int Cy;
}

/// <summary>Win32 <c>MSG</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint Hwnd;
    public uint Message;
    public nint WParam;
    public nint LParam;
    public uint Time;
    public POINT Point;
    public uint Private;
}

/// <summary>Win32 <c>WNDCLASSEXW</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSEXW
{
    public uint Size;
    public uint Style;
    public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> WndProc;
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

/// <summary>Win32 <c>MONITORINFOEXW</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MONITORINFOEXW
{
    public uint Size;
    public RECT Monitor;
    public RECT Work;
    public uint Flags;
    public fixed char Device[32];
}

/// <summary>Win32 <c>BITMAPINFOHEADER</c> (used alone as a <c>BITMAPINFO</c> for 32-bpp DIB sections).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
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

/// <summary><c>THREAD_POWER_THROTTLING_STATE</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct THREAD_POWER_THROTTLING_STATE
{
    public uint Version;
    public uint ControlMask;
    public uint StateMask;
}

/// <summary>Window style bits used by the layers.</summary>
internal static class WindowStyles
{
    public const uint Child = 0x40000000;
    public const uint Popup = 0x80000000;
    public const uint Disabled = 0x08000000;
    public const uint ClipSiblings = 0x04000000;
}

/// <summary>Extended window style bits used by the layers.</summary>
internal static class ExtendedWindowStyles
{
    public const uint Topmost = 0x00000008;
    public const uint NoParentNotify = 0x00000004;
    public const uint Transparent = 0x00000020;
    public const uint ToolWindow = 0x00000080;
    public const uint Layered = 0x00080000;
    public const uint NoRedirectionBitmap = 0x00200000;
    public const uint NoActivate = 0x08000000;
}

/// <summary><c>SetWindowPos</c> flags and special insert-after handles.</summary>
internal static class WindowPositions
{
    public const uint NoSize = 0x0001;
    public const uint NoMove = 0x0002;
    public const uint NoZOrder = 0x0004;
    public const uint NoActivate = 0x0010;
    public const uint ShowWindow = 0x0040;
    public const uint NoOwnerZOrder = 0x0200;

    public static readonly nint Top = 0;
    public static readonly nint Bottom = 1;
    public static readonly nint TopMost = -1;
    public static readonly nint NoTopMost = -2;
}

/// <summary>Window messages handled by the Lights thread.</summary>
internal static class WindowMessages
{
    public const uint Destroy = 0x0002;
    public const uint Paint = 0x000F;
    public const uint EraseBackground = 0x0014;
    public const uint SettingChange = 0x001A;
    public const uint MouseActivate = 0x0021;
    public const uint DisplayChange = 0x007E;
    public const uint NcDestroy = 0x0082;
    public const uint NcHitTest = 0x0084;
    public const uint PowerBroadcast = 0x0218;
    public const uint DpiChanged = 0x02E0;

    /// <summary>The undocumented Progman message that splits the wallpaper into its own WorkerW (wParam 0xD, lParam 1; never lParam 0).</summary>
    public const uint ProgmanSpawnWorker = 0x052C;

    public const int HitTestTransparent = -1;
    public const int MouseActivateNoActivate = 3;
    public const uint SetWorkArea = 0x002F;
    public const uint PowerResumeSuspend = 0x0007;
    public const uint PowerResumeAutomatic = 0x0012;
}

/// <summary>Other Win32 constants.</summary>
internal static class Win32Constants
{
    public const uint GwHwndNext = 2;
    public const uint GwChild = 5;
    public const int GwlExStyle = -20;
    public const int SwHide = 0;
    public const int SwShowNoActivate = 4;
    public const uint PmRemove = 0x0001;
    public const uint QsAllInput = 0x04FF;
    public const uint MwmoInputAvailable = 0x0004;
    public const uint Infinite = 0xFFFFFFFF;
    public const uint MonitorDefaultToNearest = 2;
    public const int MdtEffectiveDpi = 0;
    public const uint EventSystemForeground = 0x0003;
    public const uint WinEventOutOfContext = 0x0000;
    public const uint WinEventSkipOwnProcess = 0x0002;
    public const uint CreateWaitableTimerHighResolution = 0x00000002;
    public const uint TimerAllAccess = 0x1F0003;
    public const uint EventAllAccess = 0x1F0003;
    public const uint DwmwaExcludedFromPeek = 12;
    public const int ThreadPowerThrottling = 3;
    public const uint ThreadPowerThrottlingCurrentVersion = 1;
    public const uint ThreadPowerThrottlingExecutionSpeed = 0x1;

    /// <summary><c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>.</summary>
    public static readonly nint DpiAwarenessContextPerMonitorAwareV2 = -4;
}
