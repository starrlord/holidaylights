using System.Runtime.InteropServices;
using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Displays;

/// <summary>Reads each display's wallpaper through <c>IDesktopWallpaper</c> (see <see cref="IWallpaperProvider"/>). Owner: platform.</summary>
/// <remarks>
/// The display is matched to the shell's monitor by device path (<see cref="DisplayInfo.DeviceId"/>), else by its
/// rectangle; a spanned wallpaper or an unmatched display uses the wallpaper shared by all monitors. A path that does
/// not exist on disk is reported as no image. The COM calls run in a single-threaded apartment (inline on the UI thread).
/// When the shell cannot be asked, the result has no image and no colour (with the Windows default position, Fill), so
/// light stages draw the night gradient.
/// </remarks>
public sealed class WallpaperProvider : IWallpaperProvider
{
    private const string LogSource = "Platform.Wallpaper";
    private static readonly WallpaperInfo Unknown = new(null, WallpaperPosition.Fill, null);

    private readonly IAppLog log;
    private int failuresLogged;

    /// <summary>Creates the provider.</summary>
    /// <param name="log">The log.</param>
    public WallpaperProvider(IAppLog log) => this.log = log;

    /// <inheritdoc />
    public WallpaperInfo GetWallpaper(DisplayInfo display)
    {
        ArgumentNullException.ThrowIfNull(display);
        try
        {
            return ComApartment.Run(() => Read(display));
        }
        catch (Exception e)
        {
            // The contract never throws: any shell failure (no Explorer, policy, COM errors) gives the night-sky backdrop.
            // Only the first failure is logged: light stages ask again on every redraw of a backdrop.
            if (Interlocked.Exchange(ref failuresLogged, 1) == 0)
            {
                log.Warn(LogSource, "Cannot read the desktop wallpaper; previews use the night sky.", e);
            }

            return Unknown;
        }
    }

    private static WallpaperInfo Read(DisplayInfo display)
    {
        IDesktopWallpaper wallpaper = ComApartment.Create<IDesktopWallpaper>(ShellClassIds.DesktopWallpaper);
        try
        {
            WallpaperPosition position = wallpaper.GetPosition(out int rawPosition) == 0 && Enum.IsDefined((WallpaperPosition)rawPosition)
                ? (WallpaperPosition)rawPosition
                : WallpaperPosition.Fill;
            RgbColor? background = wallpaper.GetBackgroundColor(out uint colorRef) == 0 ? RgbColor.FromColorRef(colorRef) : null;
            string? monitorId = position == WallpaperPosition.Span ? null : FindMonitorId(wallpaper, display);
            string? image = ReadWallpaperPath(wallpaper, monitorId) ?? (monitorId is null ? null : ReadWallpaperPath(wallpaper, null));
            return new WallpaperInfo(image, position, background);
        }
        finally
        {
            ComApartment.Release(wallpaper);
        }
    }

    private static string? FindMonitorId(IDesktopWallpaper wallpaper, DisplayInfo display)
    {
        if (wallpaper.GetMonitorDevicePathCount(out uint count) != 0)
        {
            return null;
        }

        string? byRectangle = null;
        for (uint index = 0; index < count; index++)
        {
            string? id = TakeString(wallpaper.GetMonitorDevicePathAt(index, out nint text), text);
            if (id is null)
            {
                continue;
            }

            if (string.Equals(id, display.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }

            if (byRectangle is null && wallpaper.GetMonitorRECT(id, out NativeRect rect) == 0 && rect.ToRectI() == display.Bounds)
            {
                byRectangle = id;
            }
        }

        return byRectangle;
    }

    private static string? ReadWallpaperPath(IDesktopWallpaper wallpaper, string? monitorId)
    {
        string? path = TakeString(wallpaper.GetWallpaper(monitorId, out nint text), text);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    /// <summary>Reads and frees a callee-allocated string (null when the call failed).</summary>
    private static string? TakeString(int hresult, nint text)
    {
        try
        {
            return hresult == 0 && text != 0 ? Marshal.PtrToStringUni(text) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
        }
    }
}
