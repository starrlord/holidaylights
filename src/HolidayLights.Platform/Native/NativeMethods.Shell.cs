using System.Runtime.InteropServices;

namespace HolidayLights.Platform.Native;

/// <summary>Source-generated P/Invoke declarations (shell32, shlwapi, ole32).</summary>
internal static unsafe partial class NativeMethods
{
    internal const int SHCNE_ASSOCCHANGED = 0x08000000;
    internal const uint SHCNF_IDLIST = 0x0000;
    internal const uint CLSCTX_INPROC_SERVER = 0x1;
    internal const uint CLSCTX_LOCAL_SERVER = 0x4;

    [LibraryImport("shell32.dll")]
    internal static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    [LibraryImport("shell32.dll")]
    internal static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHParseDisplayName(string name, nint bindContext, out nint itemIdList, uint attributesIn, out uint attributesOut);

    [LibraryImport("shell32.dll")]
    internal static partial int SHOpenFolderAndSelectItems(nint folder, uint count, nint* items, uint flags);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid interfaceId, out nint item);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHLoadIndirectString(string source, char* buffer, uint bufferLength, nint reserved);

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out nint instance);
}
