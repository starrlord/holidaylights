using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using HolidayLights.App.Settings;
using HolidayLights.Tests.Shared;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>
/// The "Add to Holiday Lights" drag description (PRODUCT-SPEC 3.2.7): it is written onto a real shell data object (the
/// kind Explorer drags) as the shell's <c>DROPDESCRIPTION</c>, and cleared again. On the UI thread, like a real drag.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed partial class FileDropFeedbackTests(UiThread ui)
{
    private static readonly Guid DataObjectId = new("0000010e-0000-0000-C000-000000000046");

    [Fact]
    public void TheDescriptionReadsAddToHolidayLightsOnAShellDataObject() => ui.Run(() =>
    {
        using var root = new TempDataRoot();
        string file = Path.Combine(root.Paths.LocalRoot, "Star.bul");
        Directory.CreateDirectory(root.Paths.LocalRoot);
        File.WriteAllBytes(file, [1, 2, 3]);
        Assert.True(DroppedFiles.CanAddAny([file]));
        Assert.False(DroppedFiles.CanAddAny([Path.ChangeExtension(file, ".txt")]));

        IDataObject data = ShellDataObject(file);
        FileDropFeedback.Describe(data, show: true);
        FileDropFeedback.DropDescription shown = Read(data);
        Assert.Equal(1, shown.Type);
        Assert.Equal("Add to %1", shown.Message);
        Assert.Equal("Holiday Lights", shown.Insert);

        FileDropFeedback.Describe(data, show: false);
        FileDropFeedback.DropDescription cleared = Read(data);
        Assert.Equal(-1, cleared.Type);
        Assert.Equal("", cleared.Message);
    });

    private static FileDropFeedback.DropDescription Read(IDataObject data)
    {
        var format = new FORMATETC
        {
            cfFormat = (short)System.Windows.DataFormats.GetDataFormat("DropDescription").Id,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };
        data.GetData(ref format, out STGMEDIUM medium);
        try
        {
            IntPtr pointer = GlobalLock(medium.unionmember);
            try
            {
                return Marshal.PtrToStructure<FileDropFeedback.DropDescription>(pointer);
            }
            finally
            {
                GlobalUnlock(medium.unionmember);
            }
        }
        finally
        {
            // A copy without a release object belongs to the caller; otherwise the data object frees it.
            if (medium.pUnkForRelease is null)
            {
                GlobalFree(medium.unionmember);
            }
        }
    }

    /// <summary>The data object the shell itself creates for a file (what Explorer puts on a drag).</summary>
    private static IDataObject ShellDataObject(string file)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(file, IntPtr.Zero, out IntPtr pidl, 0, out _));
        try
        {
            Guid id = DataObjectId;
            Marshal.ThrowExceptionForHR(SHCreateDataObject(IntPtr.Zero, 1, [pidl], IntPtr.Zero, ref id, out IntPtr pointer));
            try
            {
                return (IDataObject)Marshal.GetObjectForIUnknown(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHCreateDataObject(IntPtr folder, uint count, [In] IntPtr[] children, IntPtr inner, ref Guid id, out IntPtr dataObject);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalLock(IntPtr memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr memory);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalFree(IntPtr memory);
}
