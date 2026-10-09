namespace HolidayLights.Rendering.Shell;

/// <summary>One top-level window in z-order.</summary>
/// <param name="Hwnd">The window.</param>
/// <param name="Topmost">It has <c>WS_EX_TOPMOST</c>.</param>
/// <param name="IsTaskbar">It is a topmost <c>Shell_TrayWnd</c> or <c>Shell_SecondaryTrayWnd</c>.</param>
/// <param name="IsOurs">It is one of the presenter's own windows.</param>
/// <param name="Visible">It is visible.</param>
internal readonly record struct ZOrderEntry(nint Hwnd, bool Topmost, bool IsTaskbar, bool IsOurs, bool Visible = true);

/// <summary>
/// The top-level z-order (topmost first) and the placement rules of the top-level layers (PRODUCT-SPEC 5.1): in front of the
/// icons sits directly above Progman; on top sits in the topmost band directly below every taskbar; after Win+D an in-front
/// layer found below the desktop host moves to the bottom of the topmost band (Rainmeter's technique).
/// </summary>
internal sealed class ZOrderSnapshot
{
    private readonly IReadOnlyList<ZOrderEntry> entries;

    /// <summary>Creates the snapshot.</summary>
    public ZOrderSnapshot(IReadOnlyList<ZOrderEntry> entries) => this.entries = entries;

    /// <summary>(c) needs re-inserting when it is not topmost, missing, or a taskbar lies below it.</summary>
    public bool OnTopNeedsFix(nint layer)
    {
        int index = IndexOf(layer);
        if (index < 0 || !entries[index].Topmost)
        {
            return true;
        }

        for (int i = index + 1; i < entries.Count; i++)
        {
            if (entries[i].IsTaskbar)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Where (c) is inserted: after the lowest taskbar, or at the top of the topmost band when there is none.</summary>
    public nint OnTopInsertAfter()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].IsTaskbar)
            {
                return entries[i].Hwnd;
            }
        }

        return Interop.WindowPositions.TopMost;
    }

    /// <summary>(b) needs <c>HWND_BOTTOM</c> again when a visible window other than our layers lies between it and Progman, or Progman is not below it.</summary>
    public bool InFrontOfIconsNeedsFix(nint layer, nint progman)
    {
        int index = IndexOf(layer);
        int host = IndexOf(progman);
        if (index < 0 || host < 0 || host < index)
        {
            return true;
        }

        for (int i = index + 1; i < host; i++)
        {
            if (!entries[i].IsOurs && entries[i].Visible)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="layer"/> lies below <paramref name="host"/> (Win+D brought the desktop host forward).</summary>
    public bool IsBelow(nint layer, nint host)
    {
        int index = IndexOf(layer);
        int hostIndex = IndexOf(host);
        return index >= 0 && hostIndex >= 0 && hostIndex < index;
    }

    /// <summary>The back-most topmost window that is not ours (insert after it to sit at the bottom of the topmost band), or 0.</summary>
    public nint BottomOfTopmostBand()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].Topmost && !entries[i].IsOurs)
            {
                return entries[i].Hwnd;
            }
        }

        return 0;
    }

    private int IndexOf(nint hwnd)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Hwnd == hwnd)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>A top-level window the z-order rules move (a layer window; a simulated one in tests).</summary>
internal interface IBandWindow
{
    /// <summary>True when the window has <c>WS_EX_TOPMOST</c> now.</summary>
    bool IsTopmost { get; }

    /// <summary>Re-inserts the window after <paramref name="insertAfter"/> (a window or a special handle).</summary>
    void PlaceAfter(nint insertAfter, bool show = false);
}

/// <summary>Keeps a window in the topmost band at the place its rule picks.</summary>
internal static class TopmostBand
{
    /// <summary>
    /// Checks that a window just placed in the topmost band is still topmost and repairs it at once. Windows takes the
    /// topmost style away from a window inserted after a window that is not topmost, which drops it below every app window:
    /// Explorer demotes a taskbar while a full-screen window covers its display, and can do that between the snapshot that
    /// picked the taskbar and the insert. The window goes back to the top of the band, then after the window that
    /// <paramref name="anchor"/> picks from a fresh snapshot; when that fails again it stays at the top of the band until the
    /// 2 s check.
    /// </summary>
    /// <param name="window">The window, placed already.</param>
    /// <param name="anchor">Picks the window to insert after (<c>HWND_TOPMOST</c> or 0: the top of the band).</param>
    public static void Keep(IBandWindow window, Func<nint> anchor)
    {
        for (int attempt = 0; attempt < 2 && !window.IsTopmost; attempt++)
        {
            window.PlaceAfter(Interop.WindowPositions.TopMost);
            nint after = anchor();
            if (after != 0 && after != Interop.WindowPositions.TopMost)
            {
                window.PlaceAfter(after);
            }
        }

        if (!window.IsTopmost)
        {
            window.PlaceAfter(Interop.WindowPositions.TopMost);
        }
    }
}

/// <summary>The rule for "behind the icons" on the raised desktop: our layers lie between <c>SHELLDLL_DefView</c> and the wallpaper <c>WorkerW</c>.</summary>
internal static class BehindIconsOrder
{
    /// <summary>True when <paramref name="layer"/> is a sibling below <paramref name="defView"/> and above <paramref name="worker"/>.</summary>
    /// <param name="siblings">The children of Progman, topmost first.</param>
    /// <param name="defView">The icons.</param>
    /// <param name="worker">The wallpaper WorkerW (0 when not a sibling).</param>
    /// <param name="layer">Our layer.</param>
    public static bool IsInPlace(IReadOnlyList<nint> siblings, nint defView, nint worker, nint layer)
    {
        int view = -1;
        int ours = -1;
        int wallpaper = int.MaxValue;
        for (int i = 0; i < siblings.Count; i++)
        {
            if (siblings[i] == defView)
            {
                view = i;
            }
            else if (siblings[i] == layer)
            {
                ours = i;
            }
            else if (siblings[i] == worker)
            {
                wallpaper = i;
            }
        }

        return view >= 0 && ours > view && ours < wallpaper;
    }
}
