using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using WpfDataObject = System.Windows.IDataObject;

namespace HolidayLights.App.Settings;

/// <summary>
/// The shell's feedback for files dragged in from Explorer (PRODUCT-SPEC 3.2.7): the drag picture keeps following the
/// pointer over the window and, while the files include something Holiday Lights can add, the cursor reads
/// "Add to Holiday Lights" (the drag image manager, <c>IDropTargetHelper</c>, with a <c>DROPDESCRIPTION</c>).
/// </summary>
/// <remarks>
/// The window's own drag handlers decide the effect; this class only mirrors it to the shell, after them (it listens to
/// handled events too). When the shell's helper is not available the drag works as before, with the standard cursor.
/// </remarks>
internal sealed partial class FileDropFeedback
{
    /// <summary>The description's text; the shell draws <see cref="Product"/> in place of "%1", emphasized.</summary>
    public const string Message = "Add to %1";

    /// <summary>The emphasized part of the description.</summary>
    public const string Product = "Holiday Lights";

    private const string LogSource = "Settings.Window";
    private const int DropImageCopy = 1;
    private const int DropImageInvalid = -1;

    private readonly Window window;
    private readonly IAppLog log;
    private readonly Func<IReadOnlyList<string>, bool> canAdd;
    private IDropTargetHelper? helper;
    private object? leaving;

    /// <summary>Starts mirroring file drags over a window to the shell.</summary>
    /// <param name="window">The window that accepts dropped files.</param>
    /// <param name="log">The log (the helper's failures).</param>
    /// <param name="canAdd">True when some of the dragged files can be added (bulbs, GIFs, songs, pictures).</param>
    public FileDropFeedback(Window window, IAppLog log, Func<IReadOnlyList<string>, bool> canAdd)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(canAdd);
        this.window = window;
        this.log = log;
        this.canAdd = canAdd;
        window.AddHandler(UIElement.DragEnterEvent, new DragEventHandler(OnDragEnter), handledEventsToo: true);
        window.AddHandler(UIElement.DragOverEvent, new DragEventHandler(OnDragOver), handledEventsToo: true);
        window.AddHandler(UIElement.DragLeaveEvent, new DragEventHandler(OnDragLeave), handledEventsToo: true);
        window.AddHandler(UIElement.DropEvent, new DragEventHandler(OnDrop), handledEventsToo: true);
    }

    /// <summary>The <c>DROPDESCRIPTION</c> structure of the shell (shlobj_core.h).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DropDescription
    {
        /// <summary>The <c>DROPIMAGETYPE</c> (1 = copy, -1 = invalid: no description).</summary>
        public int Type;

        /// <summary>The text, with "%1" where <see cref="Insert"/> goes.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Message;

        /// <summary>The emphasized insert.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Insert;
    }

    /// <summary>The shell's drag image manager on the drop target side.</summary>
    [ComImport]
    [Guid("4657278B-411B-11D2-839A-00C04FD918D0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTargetHelper
    {
        void DragEnter(IntPtr hwndTarget, ComDataObject dataObject, ref NativePoint point, int effect);

        void DragLeave();

        void DragOver(ref NativePoint point, int effect);

        void Drop(ComDataObject dataObject, ref NativePoint point, int effect);

        void Show([MarshalAs(UnmanagedType.Bool)] bool show);
    }

    private static bool IsFileDrag(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) && !e.Data.GetDataPresent(Controls.BulbDrag.Format);

    private static IReadOnlyList<string> FilesOf(WpfDataObject data) => data.GetData(DataFormats.FileDrop) as string[] ?? [];

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!IsFileDrag(e) || e.Data is not ComDataObject data)
        {
            return;
        }

        // WPF raises DragEnter (after a DragLeave) for every element the pointer enters; the shell hears it once per window.
        leaving = null;
        if (helper is not null)
        {
            OnDragOver(sender, e);
            return;
        }

        Run(() =>
        {
            helper ??= (IDropTargetHelper)new DragDropHelper();
            NativePoint point = PointOf(e);
            helper.DragEnter(new WindowInteropHelper(window).Handle, data, ref point, (int)e.Effects);
            Describe(data, e.Effects != DragDropEffects.None && canAdd(FilesOf(e.Data)));
        });
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        leaving = null;
        if (helper is null || !IsFileDrag(e) || e.Data is not ComDataObject data)
        {
            return;
        }

        Run(() =>
        {
            NativePoint point = PointOf(e);
            helper.DragOver(ref point, (int)e.Effects);
            Describe(data, e.Effects != DragDropEffects.None && canAdd(FilesOf(e.Data)));
        });
    }

    /// <summary>
    /// Leaving one element for another raises DragLeave too, followed at once by DragEnter on the next element; only a
    /// DragLeave that nothing follows (the pointer left the window, or Esc) ends the shell's feedback.
    /// </summary>
    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (helper is null)
        {
            return;
        }

        var leave = new object();
        leaving = leave;
        ComDataObject? data = e.Data as ComDataObject;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!ReferenceEquals(leaving, leave) || helper is not { } current)
            {
                return;
            }

            leaving = null;
            helper = null;
            Run(() =>
            {
                if (data is not null)
                {
                    Describe(data, show: false);
                }

                current.DragLeave();
            });
        });
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        leaving = null;
        if (helper is null || e.Data is not ComDataObject data)
        {
            return;
        }

        Run(() =>
        {
            NativePoint point = PointOf(e);
            helper.Drop(data, ref point, (int)e.Effects);
        });
        helper = null;
    }

    /// <summary>Sets (or clears) the "Add to Holiday Lights" description on the source's data object.</summary>
    internal static void Describe(ComDataObject data, bool show)
    {
        var description = new DropDescription
        {
            Type = show ? DropImageCopy : DropImageInvalid,
            Message = show ? Message : "",
            Insert = show ? Product : "",
        };
        var format = new FORMATETC
        {
            cfFormat = (short)DataFormats.GetDataFormat("DropDescription").Id,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<DropDescription>());
        try
        {
            Marshal.StructureToPtr(description, memory, fDeleteOld: false);
            var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
            data.SetData(ref format, ref medium, true);
        }
        catch
        {
            Marshal.FreeHGlobal(memory);
            throw;
        }
    }

    private NativePoint PointOf(DragEventArgs e)
    {
        Point screen = window.PointToScreen(e.GetPosition(window));
        return new NativePoint { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) };
    }

    /// <summary>Runs a call to the shell; a failure ends the feedback for this drag (the drop itself is unaffected).</summary>
    private void Run(Action call)
    {
        try
        {
            call();
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or NotImplementedException or InvalidOperationException)
        {
            log.Warn(LogSource, "The shell's drag feedback is not available for this drag.", exception);
            helper = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    /// <summary>The shell's <c>CLSID_DragDropHelper</c>.</summary>
    [ComImport]
    [Guid("4657278A-411B-11D2-839A-00C04FD918D0")]
    private class DragDropHelper;
}
