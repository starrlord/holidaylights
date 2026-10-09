using System.Runtime.InteropServices;

namespace HolidayLights.App.Shell;

/// <summary>The few Win32 functions the shell calls directly.</summary>
internal static partial class NativeMethods
{
    /// <summary><c>ASFW_ANY</c>: every process may take the foreground.</summary>
    public const int AllowAnyProcess = -1;

    /// <summary><c>ATTACH_PARENT_PROCESS</c>.</summary>
    public const int AttachParentProcess = -1;

    /// <summary><c>SM_CXSMICON</c>: the width of a small icon (the notification area).</summary>
    public const int SmallIconWidthMetric = 49;

    /// <summary>Lets another process (the running instance) bring its window to the foreground after this process received the user's input.</summary>
    /// <param name="processId">A process id, or <see cref="AllowAnyProcess"/>.</param>
    /// <returns>True on success.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(int processId);

    /// <summary>Attaches to the console of the process that started this one (<c>--diagnostics</c> without a file).</summary>
    /// <param name="processId"><see cref="AttachParentProcess"/>.</param>
    /// <returns>True when attached.</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(int processId);

    /// <summary>A system metric at a DPI.</summary>
    /// <param name="index">The metric.</param>
    /// <param name="dpi">The DPI.</param>
    /// <returns>The value in pixels.</returns>
    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    /// <summary>The DPI of the system (the notification area's).</summary>
    /// <returns>The DPI.</returns>
    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();
}
