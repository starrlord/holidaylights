using System.Runtime.InteropServices;

namespace HolidayLights.Platform.Native;

/// <summary>Win32 <c>RECT</c> (right and bottom exclusive).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly RectI ToRectI() => new(Left, Top, Right, Bottom);
}

/// <summary>Win32 <c>POINT</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}

/// <summary><c>MONITORINFOEXW</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MonitorInfoEx
{
    public uint Size;
    public NativeRect Monitor;
    public NativeRect Work;
    public uint Flags;
    public fixed char Device[32];
}

/// <summary><c>DISPLAY_DEVICEW</c> (840 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DisplayDevice
{
    public uint Size;
    public fixed char DeviceName[32];
    public fixed char DeviceString[128];
    public uint StateFlags;
    public fixed char DeviceId[128];
    public fixed char DeviceKey[128];
}

/// <summary><c>LUID</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeLuid
{
    public uint LowPart;
    public int HighPart;
}

/// <summary><c>DISPLAYCONFIG_PATH_SOURCE_INFO</c> (20 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    public NativeLuid AdapterId;
    public uint Id;
    public uint ModeInfoIndex;
    public uint StatusFlags;
}

/// <summary><c>DISPLAYCONFIG_PATH_TARGET_INFO</c> (48 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    public NativeLuid AdapterId;
    public uint Id;
    public uint ModeInfoIndex;
    public uint OutputTechnology;
    public uint Rotation;
    public uint Scaling;
    public uint RefreshRateNumerator;
    public uint RefreshRateDenominator;
    public uint ScanLineOrdering;
    public int TargetAvailable;
    public uint StatusFlags;
}

/// <summary><c>DISPLAYCONFIG_PATH_INFO</c> (72 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    public DisplayConfigPathSourceInfo SourceInfo;
    public DisplayConfigPathTargetInfo TargetInfo;
    public uint Flags;
}

/// <summary><c>DISPLAYCONFIG_MODE_INFO</c> (64 bytes; the mode union is not read).</summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct DisplayConfigModeInfo
{
    public uint InfoType;
    public uint Id;
    public NativeLuid AdapterId;
}

/// <summary><c>DISPLAYCONFIG_DEVICE_INFO_HEADER</c> (20 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDeviceInfoHeader
{
    public uint Type;
    public uint Size;
    public NativeLuid AdapterId;
    public uint Id;
}

/// <summary><c>DISPLAYCONFIG_SOURCE_DEVICE_NAME</c> (84 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DisplayConfigSourceDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;
    public fixed char ViewGdiDeviceName[32];
}

/// <summary><c>DISPLAYCONFIG_TARGET_DEVICE_NAME</c> (420 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DisplayConfigTargetDeviceName
{
    public DisplayConfigDeviceInfoHeader Header;
    public uint Flags;
    public uint OutputTechnology;
    public ushort EdidManufactureId;
    public ushort EdidProductCodeId;
    public uint ConnectorInstance;
    public fixed char MonitorFriendlyDeviceName[64];
    public fixed char MonitorDevicePath[128];
}

/// <summary><c>WNDCLASSEXW</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WindowClassEx
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

/// <summary><c>HIGHCONTRASTW</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeHighContrast
{
    public uint Size;
    public uint Flags;
    public char* DefaultScheme;
}

/// <summary><c>DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DeviceNotifySubscribeParameters
{
    public delegate* unmanaged[Stdcall]<nint, uint, nint, uint> Callback;
    public nint Context;
}
