using HolidayLights.Rendering.Composition;
using HolidayLights.Rendering.Interop;
using HolidayLights.Rendering.Shell;
using Vortice.DirectComposition;

namespace HolidayLights.Rendering.Layers;

/// <summary>
/// One window that shows DirectComposition content over a display (PRODUCT-SPEC 5.1, ARCHITECTURE 5):
/// <list type="bullet">
/// <item>(a) behind the icons: a <c>WS_EX_NOREDIRECTIONBITMAP</c> child of the desktop layer parent, below <c>SHELLDLL_DefView</c>;</item>
/// <item>(b) in front of the icons: a top-level layered, transparent, tool, no-activate window owned by Progman at <c>HWND_BOTTOM</c>, excluded from Peek;</item>
/// <item>(c) on top: the same window, topmost and not owned, below the taskbars.</item>
/// </list>
/// Overlays (pill, Identify) are on-top windows sized to their content. Lights thread only.
/// </summary>
internal sealed unsafe class LayerWindow : IDisposable, IBandWindow
{
    /// <summary>
    /// The window property (value 1) that tells Explorer's full-screen ("rude window") detection to ignore a window. Every
    /// top-level window of the Lights thread has it: layers in front of the icons and on top, the pill and Identify.
    /// </summary>
    internal const string NonRudeProperty = "NonRudeHWND";

    private const uint ChildExStyles = ExtendedWindowStyles.NoRedirectionBitmap | ExtendedWindowStyles.NoActivate | ExtendedWindowStyles.NoParentNotify;
    private const uint TopLevelExStyles = ExtendedWindowStyles.NoRedirectionBitmap | ExtendedWindowStyles.Layered | ExtendedWindowStyles.Transparent
        | ExtendedWindowStyles.ToolWindow | ExtendedWindowStyles.NoActivate;

    private IDCompositionTarget? target;

    private LayerWindow(nint hwnd, LayerMode mode, RectI bounds, nint parent, IDCompositionTarget target)
    {
        Hwnd = hwnd;
        Mode = mode;
        Bounds = bounds;
        Parent = parent;
        this.target = target;
    }

    /// <summary>The window.</summary>
    public nint Hwnd { get; private set; }

    /// <summary>The layer mode the window implements.</summary>
    public LayerMode Mode { get; }

    /// <summary>The screen rectangle the window covers (physical pixels).</summary>
    public RectI Bounds { get; private set; }

    /// <summary>The parent (behind the icons) or 0.</summary>
    public nint Parent { get; }

    /// <summary>True while the window is shown.</summary>
    public bool IsShown { get; private set; }

    /// <summary>True when the window has <c>WS_EX_TOPMOST</c> now (Windows removes it when the window is inserted after a non-topmost one).</summary>
    public bool IsTopmost => Hwnd != 0 && (User32.ExtendedStyleOf(Hwnd) & ExtendedWindowStyles.Topmost) != 0;

    /// <summary>True until the window is destroyed (by us, or by Explorer destroying its parent or owner).</summary>
    public bool IsAlive => Hwnd != 0 && !Destroyed && User32.IsWindow(Hwnd);

    /// <summary>Set when <c>WM_DESTROY</c> arrived.</summary>
    public bool Destroyed { get; private set; }

    /// <summary>Creates a hidden window in a mode, or returns null when that mode is unavailable (the caller falls back).</summary>
    /// <param name="mode">The layer mode.</param>
    /// <param name="bounds">The display rectangle.</param>
    /// <param name="hosts">The desktop hosts.</param>
    /// <param name="device">The composition device.</param>
    public static LayerWindow? TryCreate(LayerMode mode, RectI bounds, DesktopHosts hosts, CompositionDevice device)
    {
        WindowClasses.EnsureRegistered();
        nint parent = 0;
        nint hwnd;
        switch (mode)
        {
            case LayerMode.BehindIcons:
                parent = hosts.LayerParent;
                if (parent == 0 || hosts.DefView == 0)
                {
                    return null;
                }

                POINT origin = ToClient(parent, bounds);
                hwnd = User32.CreateWindowExW(
                    ChildExStyles, WindowClasses.LayerClass, null, WindowStyles.Child | WindowStyles.ClipSiblings | WindowStyles.Disabled,
                    origin.X, origin.Y, bounds.Width, bounds.Height, parent, 0, WindowClasses.Instance, 0);
                break;
            case LayerMode.InFrontOfIcons:
                if (hosts.Progman == 0)
                {
                    return null;
                }

                hwnd = CreateTopLevel(TopLevelExStyles, bounds, owner: hosts.Progman);
                break;
            default:
                hwnd = CreateTopLevel(TopLevelExStyles | ExtendedWindowStyles.Topmost, bounds, owner: 0);
                break;
        }

        if (hwnd == 0)
        {
            return null;
        }

        if (mode != LayerMode.BehindIcons)
        {
            int excluded = 1;
            Dwmapi.DwmSetWindowAttribute(hwnd, Win32Constants.DwmwaExcludedFromPeek, &excluded, sizeof(int));
        }

        IDCompositionTarget? compositionTarget = device.TryCreateTarget(hwnd);
        if (compositionTarget is null)
        {
            User32.DestroyWindow(hwnd);
            return null;
        }

        return new LayerWindow(hwnd, mode, bounds, parent, compositionTarget);
    }

    /// <summary>Creates a topmost overlay window (pill, Identify) of exactly <paramref name="bounds"/>.</summary>
    public static LayerWindow? TryCreateOverlay(RectI bounds, CompositionDevice device) =>
        TryCreate(LayerMode.OnTop, bounds, DesktopHosts.None, device);

    /// <summary>Puts a visual tree into the window (null detaches it).</summary>
    /// <returns>False when DirectComposition refused it.</returns>
    public bool SetRoot(IDCompositionVisual? root) => target is not null && target.SetRoot(root!).Success;

    /// <summary>Shows the window without activating it, at the z-order position given by <paramref name="insertAfter"/>.</summary>
    /// <param name="insertAfter">For (a): <c>SHELLDLL_DefView</c> (raised) or 0 (classic); (b): <c>HWND_BOTTOM</c>; (c): the lowest taskbar or <c>HWND_TOPMOST</c>.</param>
    public void Show(nint insertAfter)
    {
        if (Mode == LayerMode.BehindIcons && insertAfter == 0)
        {
            User32.ShowWindow(Hwnd, Win32Constants.SwShowNoActivate);
        }
        else
        {
            PlaceAfter(insertAfter, show: true);
        }

        IsShown = true;
    }

    /// <summary>Hides the window (it commits nothing while hidden).</summary>
    public void Hide()
    {
        if (IsShown && Hwnd != 0 && !Destroyed)
        {
            User32.ShowWindow(Hwnd, Win32Constants.SwHide);
        }

        IsShown = false;
    }

    /// <summary>Re-inserts the window in the z-order.</summary>
    public void PlaceAfter(nint insertAfter, bool show = false)
    {
        uint flags = WindowPositions.NoMove | WindowPositions.NoSize | WindowPositions.NoActivate | (show ? WindowPositions.ShowWindow : 0);
        User32.SetWindowPos(Hwnd, insertAfter, 0, 0, 0, 0, flags);
    }

    /// <summary>Moves the window to new display bounds.</summary>
    public void MoveTo(RectI bounds)
    {
        Bounds = bounds;
        ApplyBounds();
    }

    /// <summary>Re-applies the bounds when the window is elsewhere (a topology change moved Progman's client origin, or a DPI change).</summary>
    /// <returns>True when the window had to be moved.</returns>
    public bool EnsureBounds()
    {
        if (!User32.GetWindowRect(Hwnd, out RECT actual) || actual.ToRectI() == Bounds)
        {
            return false;
        }

        ApplyBounds();
        return true;
    }

    /// <summary>Marks the window destroyed (<c>WM_DESTROY</c>).</summary>
    public void MarkDestroyed() => Destroyed = true;

    /// <summary>Releases the composition target and destroys the window.</summary>
    public void Dispose()
    {
        if (target is not null)
        {
            // Detach the tree even when Explorer already destroyed the window, so the visuals can join a new window.
            target.SetRoot(null!);
            target.Dispose();
            target = null;
        }

        if (Hwnd != 0)
        {
            if (!Destroyed && User32.IsWindow(Hwnd))
            {
                User32.DestroyWindow(Hwnd);
            }

            Hwnd = 0;
        }

        IsShown = false;
    }

    private void ApplyBounds()
    {
        int x = Bounds.Left;
        int y = Bounds.Top;
        if (Parent != 0)
        {
            POINT origin = ToClient(Parent, Bounds);
            x = origin.X;
            y = origin.Y;
        }

        User32.SetWindowPos(Hwnd, 0, x, y, Bounds.Width, Bounds.Height, WindowPositions.NoZOrder | WindowPositions.NoActivate | WindowPositions.NoOwnerZOrder);
    }

    /// <summary>
    /// Creates a hidden top-level window and marks it as not a full-screen app. Without <see cref="NonRudeProperty"/>, a
    /// window that covers its monitor makes Explorer report <c>QUNS_BUSY</c> (Windows holds back notifications, Holiday
    /// Lights' own rules pause the music) and drop that monitor's taskbar out of the topmost band, below the on-top bulbs.
    /// </summary>
    internal static nint CreateTopLevel(uint exStyle, RectI bounds, nint owner)
    {
        nint hwnd = User32.CreateWindowExW(
            exStyle, WindowClasses.LayerClass, null, WindowStyles.Popup | WindowStyles.Disabled,
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, owner, 0, WindowClasses.Instance, 0);
        if (hwnd != 0)
        {
            // Set before the window is first shown: Explorer classifies a window when it appears.
            User32.SetPropW(hwnd, NonRudeProperty, 1);
        }

        return hwnd;
    }

    private static POINT ToClient(nint parent, RectI bounds)
    {
        var point = new POINT(bounds.Left, bounds.Top);
        User32.MapWindowPoints(0, parent, &point, 1);
        return point;
    }
}
