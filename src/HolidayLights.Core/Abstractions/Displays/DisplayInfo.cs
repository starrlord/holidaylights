namespace HolidayLights.Core.Abstractions;

/// <summary>One display (monitor) as Windows reports it, in physical pixels of the virtual screen.</summary>
/// <remarks>Produced by <see cref="IDisplayService"/> (platform). Coordinates can be negative (a display left of or above the main display).</remarks>
public sealed record DisplayInfo
{
    /// <summary>
    /// Stable identifier of the physical monitor (device interface path from <c>EnumDisplayDevices(EDD_GET_DEVICE_INTERFACE_NAME)</c>
    /// or an equivalent that survives restarts and re-plugging). Settings remember per-display choices by this id
    /// (<c>lights.displays.disabled</c>).
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>GDI device name, e.g. <c>\\.\DISPLAY1</c> (changes when the configuration changes; not for persistence).</summary>
    public required string DeviceName { get; init; }

    /// <summary>Friendly monitor name for tooltips ("DELL U2723QE"); falls back to "Display &lt;n&gt;".</summary>
    public string FriendlyName { get; init; } = "";

    /// <summary>1-based number shown to the user ("Display 2") and drawn by Identify. The main display is 1; the others follow in a stable order.</summary>
    public required int Number { get; init; }

    /// <summary>The whole display (<c>MONITORINFO.rcMonitor</c>), physical pixels.</summary>
    public required RectI Bounds { get; init; }

    /// <summary>The work area (<c>MONITORINFO.rcWork</c>): the display minus docked taskbars and app bars, physical pixels.</summary>
    public required RectI WorkArea { get; init; }

    /// <summary>Effective DPI (<c>GetDpiForMonitor(MDT_EFFECTIVE_DPI)</c>); 96 = 100 %.</summary>
    public required int Dpi { get; init; }

    /// <summary>True for the main display (<c>MONITORINFOF_PRIMARY</c>).</summary>
    public bool IsPrimary { get; init; }

    /// <summary>Display scale: <see cref="Dpi"/> / 96 (1.5 at 150 %).</summary>
    public double Scale => Dpi / 96.0;

    /// <summary>The user-facing caption, e.g. "Display 1 - 3840 x 2160 - 150 %" (PRODUCT-SPEC 3.2.3).</summary>
    /// <returns>The caption.</returns>
    public string Describe() => $"Display {Number} - {Bounds.Width} x {Bounds.Height} - {Math.Round(Scale * 100)} %";
}

/// <summary>Arguments of <see cref="IDisplayService.DisplaysChanged"/>.</summary>
public sealed class DisplaysChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="previous">The displays before the change.</param>
    /// <param name="current">The displays after the change.</param>
    public DisplaysChangedEventArgs(IReadOnlyList<DisplayInfo> previous, IReadOnlyList<DisplayInfo> current)
    {
        Previous = previous;
        Current = current;
    }

    /// <summary>The displays before the change.</summary>
    public IReadOnlyList<DisplayInfo> Previous { get; }

    /// <summary>The displays after the change.</summary>
    public IReadOnlyList<DisplayInfo> Current { get; }
}

/// <summary>
/// The current display topology and its changes (PRODUCT-SPEC 5.2). Implemented by platform.
/// </summary>
/// <remarks>
/// Change sources: <c>WM_DISPLAYCHANGE</c>, <c>WM_DPICHANGED</c>, <c>WM_SETTINGCHANGE(SPI_SETWORKAREA)</c>, <c>TaskbarCreated</c>
/// and a 2 s poll that diffs <c>{device, rcMonitor, rcWork, dpi}</c> per display. Changes are debounced 300 ms and
/// <see cref="DisplaysChanged"/> is raised only when that tuple differs. The service owns a hidden top-level message window
/// on the thread that created it; create it on the UI thread, where the event is raised. <see cref="Displays"/>,
/// <see cref="Primary"/> and <see cref="FromPoint"/> read an immutable snapshot and may be called from any thread; the
/// window, timers and <see cref="DisplaysChanged"/> stay on the creating thread.
/// </remarks>
public interface IDisplayService : IDisposable
{
    /// <summary>The displays, main display first, then by <see cref="DisplayInfo.Number"/>.</summary>
    IReadOnlyList<DisplayInfo> Displays { get; }

    /// <summary>The main display.</summary>
    DisplayInfo Primary { get; }

    /// <summary>Raised (debounced) on the creating thread when the topology, a work area or a scale changed.</summary>
    event EventHandler<DisplaysChangedEventArgs>? DisplaysChanged;

    /// <summary>Returns the display that contains a point (nearest display when none does).</summary>
    /// <param name="point">A virtual-screen point in physical pixels.</param>
    /// <returns>The display.</returns>
    DisplayInfo FromPoint(PointI point);

    /// <summary>Returns the display under the mouse pointer (for the on-screen pill and window placement).</summary>
    /// <returns>The display.</returns>
    DisplayInfo FromCursor();
}

/// <summary>How Windows positions the wallpaper image (<c>DESKTOP_WALLPAPER_POSITION</c>).</summary>
public enum WallpaperPosition
{
    /// <summary>Centred at its own size.</summary>
    Center,

    /// <summary>Repeated from the top-left.</summary>
    Tile,

    /// <summary>Stretched to the display (aspect not kept).</summary>
    Stretch,

    /// <summary>Scaled to fit inside the display (letterboxed with the background colour).</summary>
    Fit,

    /// <summary>Scaled to fill the display (cropped).</summary>
    Fill,

    /// <summary>One image spanning all displays.</summary>
    Span,
}

/// <summary>What the desktop shows behind the icons on one display (for light stages, PRODUCT-SPEC 3.0.2).</summary>
/// <param name="ImagePath">The image file, or null (solid colour, Spotlight, or unknown: draw the night gradient when <paramref name="BackgroundColor"/> is also null).</param>
/// <param name="Position">How the image is positioned.</param>
/// <param name="BackgroundColor">The desktop background colour, or null when unknown.</param>
public sealed record WallpaperInfo(string? ImagePath, WallpaperPosition Position, RgbColor? BackgroundColor);

/// <summary>Reads the wallpaper of each display (<c>IDesktopWallpaper</c>). Implemented by platform; callable from any thread.</summary>
public interface IWallpaperProvider
{
    /// <summary>Returns the wallpaper of one display (a slideshow returns its current image).</summary>
    /// <param name="display">The display.</param>
    /// <returns>The wallpaper; never throws (errors give an all-null result).</returns>
    WallpaperInfo GetWallpaper(DisplayInfo display);
}
