using System.Runtime.InteropServices;

namespace HolidayLights.Platform.Legacy;

/// <summary>The Win32 calls of the 5.4 import and leftovers.</summary>
internal static partial class LegacyNativeMethods
{
    /// <summary>The 5.4 main window's class.</summary>
    public const string WindowClass = "Holiday Lights";

    /// <summary>The 5.4 main window's title.</summary>
    public const string WindowTitle = "TigerTechHolidayLights";

    /// <summary>The 5.4 "Exit" command (tray menu item 106, also sent by its installer).</summary>
    public const int ExitCommand = 106;

    private const uint WmCommand = 0x0111;
    private const uint SendMessageAbortIfHung = 0x0002;
    private const uint SendTimeoutMilliseconds = 3000;

    /// <summary>Finds the running 5.4 main window.</summary>
    /// <returns>The window, or 0 when 5.4 is not running.</returns>
    public static nint FindLegacyWindow() => FindWindowW(WindowClass, WindowTitle);

    /// <summary>Sends <c>WM_COMMAND</c> with a command id and waits until the window handled it (at most 3 s; not to a hung window).</summary>
    /// <param name="window">The window.</param>
    /// <param name="command">The command id.</param>
    /// <returns>True when the message was delivered.</returns>
    public static bool SendCommand(nint window, int command) =>
        SendMessageTimeoutW(window, WmCommand, command, 0, SendMessageAbortIfHung, SendTimeoutMilliseconds, out _) != 0;

    /// <summary>Expands a path's 8.3 components (<c>GetLongPathNameW</c>).</summary>
    /// <param name="path">An existing file or folder.</param>
    /// <returns>The long form, or null when the path does not exist.</returns>
    public static string? GetLongPath(string path)
    {
        uint length = GetLongPathNameW(path, null, 0);
        if (length == 0)
        {
            return null;
        }

        char[] buffer = new char[length];
        uint written = GetLongPathNameW(path, buffer, length);
        return written == 0 || written >= length ? null : new string(buffer, 0, (int)written);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string className, string windowName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SendMessageTimeoutW(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetLongPathNameW(string shortPath, [Out] char[]? longPath, uint length);
}
