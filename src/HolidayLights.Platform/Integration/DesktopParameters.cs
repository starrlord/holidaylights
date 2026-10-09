using System.ComponentModel;
using System.Runtime.InteropServices;
using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Integration;

/// <summary>The two screen saver switches that Windows changes through <c>SystemParametersInfo</c> (a seam: tests never call it).</summary>
internal interface IDesktopParameters
{
    /// <summary><c>SPI_SETSCREENSAVEACTIVE</c>.</summary>
    /// <param name="active">The new state.</param>
    void SetScreenSaveActive(bool active);

    /// <summary><c>SPI_SETSCREENSAVETIMEOUT</c>.</summary>
    /// <param name="seconds">Idle time before the saver starts.</param>
    void SetScreenSaveTimeout(int seconds);
}

/// <summary>
/// Writes through <c>SystemParametersInfo(SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)</c>: the values are stored in
/// <c>HKCU\Control Panel\Desktop</c> and every top-level window hears <c>WM_SETTINGCHANGE</c>.
/// </summary>
internal sealed unsafe class SystemDesktopParameters : IDesktopParameters
{
    /// <summary>The only instance.</summary>
    public static SystemDesktopParameters Instance { get; } = new();

    /// <inheritdoc />
    public void SetScreenSaveActive(bool active) => Set(NativeMethods.SPI_SETSCREENSAVEACTIVE, active ? 1u : 0u);

    /// <inheritdoc />
    public void SetScreenSaveTimeout(int seconds) => Set(NativeMethods.SPI_SETSCREENSAVETIMEOUT, (uint)seconds);

    private static void Set(uint action, uint value)
    {
        if (!NativeMethods.SystemParametersInfo(action, value, null, NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE))
        {
            // Reported like a registry failure so SystemChanges logs it.
            throw new IOException("SystemParametersInfo failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }
}
