namespace HolidayLights.Rendering.Shell;

/// <summary>How Explorer has built the desktop; detected from the window structure, never from the Windows version.</summary>
internal enum DesktopLayout
{
    /// <summary>The 24H2+ "raised desktop": Progman has <c>WS_EX_NOREDIRECTIONBITMAP</c>; <c>SHELLDLL_DefView</c> and the wallpaper <c>WorkerW</c> are its children.</summary>
    Raised,

    /// <summary>Windows 10 / older 11: a top-level <c>WorkerW</c> holds <c>SHELLDLL_DefView</c>, followed by the top-level wallpaper <c>WorkerW</c>.</summary>
    Classic,

    /// <summary>Progman exists but no wallpaper layer yet (message <c>0x052C</c> not processed).</summary>
    NotSplit,

    /// <summary>No Progman (Explorer is not running or restarting).</summary>
    NoShell,
}

/// <summary>The shell windows that host the desktop icons and the wallpaper.</summary>
/// <param name="Layout">The detected structure.</param>
/// <param name="Progman">The <c>Progman</c> window (0 without a shell).</param>
/// <param name="DefView">The <c>SHELLDLL_DefView</c> window (desktop icons).</param>
/// <param name="WallpaperWorker">The wallpaper <c>WorkerW</c>.</param>
internal readonly record struct DesktopHosts(DesktopLayout Layout, nint Progman, nint DefView, nint WallpaperWorker)
{
    /// <summary>No shell.</summary>
    public static DesktopHosts None => new(DesktopLayout.NoShell, 0, 0, 0);

    /// <summary>The window a "behind the icons" layer is a child of: Progman (raised) or the wallpaper WorkerW (classic); 0 when there is none.</summary>
    public nint LayerParent => Layout switch
    {
        DesktopLayout.Raised => Progman,
        DesktopLayout.Classic => WallpaperWorker,
        _ => 0,
    };
}

/// <summary>The window queries host discovery needs (a seam for tests).</summary>
internal interface IShellWindows
{
    /// <summary><c>FindWindowEx(parent, after, className, null)</c>; parent 0 searches top-level windows.</summary>
    nint Find(nint parent, nint after, string className);

    /// <summary>True when the window has <c>WS_EX_NOREDIRECTIONBITMAP</c>.</summary>
    bool HasNoRedirectionBitmap(nint hwnd);

    /// <summary>The process id of a window.</summary>
    uint ProcessIdOf(nint hwnd);

    /// <summary>The top-level windows in z-order (topmost first).</summary>
    IReadOnlyList<nint> TopLevelWindows();
}

/// <summary>Finds the desktop hosts (<c>Snippets/BulbLayer.cs</c> <c>DesktopShell.Locate</c>, without sending <c>0x052C</c>).</summary>
internal static class DesktopHostLocator
{
    /// <summary>Detects the desktop structure.</summary>
    public static DesktopHosts Locate(IShellWindows windows)
    {
        nint progman = windows.Find(0, 0, "Progman");
        if (progman == 0)
        {
            return DesktopHosts.None;
        }

        nint defView = windows.Find(progman, 0, "SHELLDLL_DefView");
        if (windows.HasNoRedirectionBitmap(progman))
        {
            nint worker = windows.Find(progman, 0, "WorkerW");
            return new DesktopHosts(defView != 0 && worker != 0 ? DesktopLayout.Raised : DesktopLayout.NotSplit, progman, defView, worker);
        }

        uint shellProcess = windows.ProcessIdOf(progman);
        foreach (nint top in windows.TopLevelWindows())
        {
            nint view = windows.Find(top, 0, "SHELLDLL_DefView");
            if (view == 0)
            {
                continue;
            }

            nint wallpaper = windows.Find(0, top, "WorkerW");
            if (wallpaper != 0 && windows.ProcessIdOf(wallpaper) != shellProcess)
            {
                wallpaper = 0;
            }

            return new DesktopHosts(wallpaper != 0 ? DesktopLayout.Classic : DesktopLayout.NotSplit, progman, view, wallpaper);
        }

        return new DesktopHosts(DesktopLayout.NotSplit, progman, defView, 0);
    }
}
