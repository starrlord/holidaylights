using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace HolidayLights.Platform.Native;

/// <summary>Class ids of the shell objects used by the platform services.</summary>
internal static class ShellClassIds
{
    /// <summary><c>CLSID_DesktopWallpaper</c>.</summary>
    public static readonly Guid DesktopWallpaper = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    /// <summary><c>CLSID_FileOperation</c>.</summary>
    public static readonly Guid FileOperation = new("3AD05575-8857-4850-9277-11B85BDB8E09");

    /// <summary><c>CLSID_ShellLink</c>.</summary>
    public static readonly Guid ShellLink = new("00021401-0000-0000-C000-000000000046");

    /// <summary><c>IID_IShellItem</c>.</summary>
    public static readonly Guid ShellItemInterface = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
}

/// <summary><c>IDesktopWallpaper</c> (shobjidl_core.h). Every method is declared to keep the vtable order; only the getters are called.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
internal partial interface IDesktopWallpaper
{
    [PreserveSig]
    int SetWallpaper(string? monitorId, string wallpaper);

    /// <summary>The wallpaper of a monitor (or of all monitors when <paramref name="monitorId"/> is null); free with <c>CoTaskMemFree</c>.</summary>
    [PreserveSig]
    int GetWallpaper(string? monitorId, out nint wallpaper);

    /// <summary>The device path of the n-th monitor; free with <c>CoTaskMemFree</c>.</summary>
    [PreserveSig]
    int GetMonitorDevicePathAt(uint monitorIndex, out nint monitorId);

    [PreserveSig]
    int GetMonitorDevicePathCount(out uint count);

    [PreserveSig]
    int GetMonitorRECT(string monitorId, out NativeRect displayRect);

    [PreserveSig]
    int SetBackgroundColor(uint color);

    [PreserveSig]
    int GetBackgroundColor(out uint color);

    [PreserveSig]
    int SetPosition(int position);

    [PreserveSig]
    int GetPosition(out int position);

    [PreserveSig]
    int SetSlideshow(nint items);

    [PreserveSig]
    int GetSlideshow(out nint items);

    [PreserveSig]
    int SetSlideshowOptions(uint options, uint slideshowTick);

    [PreserveSig]
    int GetSlideshowOptions(out uint options, out uint slideshowTick);

    [PreserveSig]
    int AdvanceSlideshow(string? monitorId, int direction);

    [PreserveSig]
    int GetStatus(out uint state);

    [PreserveSig]
    int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

/// <summary><c>IFileOperation</c> (shobjidl_core.h). Every method is declared to keep the vtable order; interface pointers are passed as <c>nint</c>.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
internal partial interface IFileOperation
{
    [PreserveSig]
    int Advise(nint sink, out uint cookie);

    [PreserveSig]
    int Unadvise(uint cookie);

    [PreserveSig]
    int SetOperationFlags(uint flags);

    [PreserveSig]
    int SetProgressMessage(string message);

    [PreserveSig]
    int SetProgressDialog(nint dialog);

    [PreserveSig]
    int SetProperties(nint properties);

    [PreserveSig]
    int SetOwnerWindow(nint owner);

    [PreserveSig]
    int ApplyPropertiesToItem(nint item);

    [PreserveSig]
    int ApplyPropertiesToItems(nint items);

    [PreserveSig]
    int RenameItem(nint item, string newName, nint sink);

    [PreserveSig]
    int RenameItems(nint items, string newName);

    [PreserveSig]
    int MoveItem(nint item, nint destinationFolder, string? newName, nint sink);

    [PreserveSig]
    int MoveItems(nint items, nint destinationFolder);

    [PreserveSig]
    int CopyItem(nint item, nint destinationFolder, string? copyName, nint sink);

    [PreserveSig]
    int CopyItems(nint items, nint destinationFolder);

    [PreserveSig]
    int DeleteItem(nint item, nint sink);

    [PreserveSig]
    int DeleteItems(nint items);

    [PreserveSig]
    int NewItem(nint destinationFolder, uint fileAttributes, string name, string? templateName, nint sink);

    [PreserveSig]
    int PerformOperations();

    [PreserveSig]
    int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
}

/// <summary><c>IShellLinkW</c> (shobjidl_core.h). Every method is declared to keep the vtable order.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    [PreserveSig]
    int GetPath(char* file, int fileLength, nint findData, uint flags);

    [PreserveSig]
    int GetIDList(out nint itemIdList);

    [PreserveSig]
    int SetIDList(nint itemIdList);

    [PreserveSig]
    int GetDescription(char* name, int nameLength);

    [PreserveSig]
    int SetDescription(string name);

    [PreserveSig]
    int GetWorkingDirectory(char* directory, int directoryLength);

    [PreserveSig]
    int SetWorkingDirectory(string directory);

    [PreserveSig]
    int GetArguments(char* arguments, int argumentsLength);

    [PreserveSig]
    int SetArguments(string arguments);

    [PreserveSig]
    int GetHotkey(out ushort hotkey);

    [PreserveSig]
    int SetHotkey(ushort hotkey);

    [PreserveSig]
    int GetShowCmd(out int showCommand);

    [PreserveSig]
    int SetShowCmd(int showCommand);

    [PreserveSig]
    int GetIconLocation(char* iconPath, int iconPathLength, out int iconIndex);

    [PreserveSig]
    int SetIconLocation(string iconPath, int iconIndex);

    [PreserveSig]
    int SetRelativePath(string relativePath, uint reserved);

    [PreserveSig]
    int Resolve(nint window, uint flags);

    [PreserveSig]
    int SetPath(string file);
}

/// <summary><c>IPersistFile</c> (objidl.h), including the inherited <c>IPersist.GetClassID</c>.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    [PreserveSig]
    int GetClassID(out Guid classId);

    [PreserveSig]
    int IsDirty();

    [PreserveSig]
    int Load(string fileName, uint mode);

    [PreserveSig]
    int Save(string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);

    [PreserveSig]
    int SaveCompleted(string? fileName);

    [PreserveSig]
    int GetCurFile(out nint fileName);
}
