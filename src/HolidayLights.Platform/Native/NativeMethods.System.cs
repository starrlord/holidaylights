using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HolidayLights.Platform.Native;

/// <summary>Source-generated P/Invoke declarations (kernel32, advapi32, shcore, dwmapi, secur32, wtsapi32, powrprof).</summary>
internal static unsafe partial class NativeMethods
{
    internal const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    internal const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;
    internal const uint REG_NOTIFY_CHANGE_NAME = 0x00000001;
    internal const uint REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;
    internal const uint REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;
    internal const int GEOCLASS_NATION = 16;
    internal const int GEO_ISO2 = 4;
    internal const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const uint DWMWA_CLOAKED = 14;
    internal const int NAME_DISPLAY = 3;
    internal const int WTS_SESSION_INFO_EX = 25;
    internal const uint WTS_CURRENT_SESSION = 0xFFFFFFFF;
    internal const uint NOTIFY_FOR_THIS_SESSION = 0;
    internal const uint DEVICE_NOTIFY_CALLBACK = 2;
    internal const uint PBT_POWERSETTINGCHANGE = 0x8013;
    internal const uint EFFECTIVE_POWER_MODE_V2 = 2;

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint LoadLibraryEx(string path, nint file, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FreeLibrary(nint module);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetLongPathNameW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetLongPathName(string shortPath, char* longPath, uint bufferLength);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint sessionId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint sessionId);

    [LibraryImport("kernel32.dll")]
    internal static partial int GetUserDefaultGeoName(char* geoName, int geoNameCount);

    [LibraryImport("kernel32.dll")]
    internal static partial int GetUserGeoID(int geoClass);

    [LibraryImport("kernel32.dll", EntryPoint = "GetGeoInfoW")]
    internal static partial int GetGeoInfo(int location, int geoType, char* data, int dataCount, ushort languageId);

    [LibraryImport("advapi32.dll")]
    internal static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, uint notifyFilter, SafeWaitHandle eventHandle,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);

    [LibraryImport("secur32.dll", EntryPoint = "GetUserNameExW")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool GetUserNameEx(int nameFormat, char* buffer, ref uint size);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSQuerySessionInformation(nint server, uint sessionId, int infoClass, out nint buffer, out uint bytesReturned);

    [LibraryImport("wtsapi32.dll")]
    internal static partial void WTSFreeMemory(nint memory);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSRegisterSessionNotification(nint hwnd, uint flags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WTSUnRegisterSessionNotification(nint hwnd);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerSettingRegisterNotification(Guid* setting, uint flags, DeviceNotifySubscribeParameters* recipient, out nint registration);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerSettingUnregisterNotification(nint registration);

    [LibraryImport("powrprof.dll")]
    internal static partial int PowerRegisterForEffectivePowerModeNotifications(
        uint version, delegate* unmanaged[Stdcall]<int, nint, void> callback, nint context, out nint registration);

    [LibraryImport("powrprof.dll")]
    internal static partial int PowerUnregisterFromEffectivePowerModeNotifications(nint registration);
}
