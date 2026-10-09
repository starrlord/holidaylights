using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace HolidayLights.App.Install;

/// <summary>
/// The Holiday Lights processes that run from a program folder other than the running instance, which the instance pipe
/// already asked to exit: above all the screen saver preview that Windows' Screen Saver Settings keeps running
/// (<c>Holiday Lights.scr /p</c>). They lock the program files, so the installer ends them before it replaces the files.
/// </summary>
internal static class FolderPrograms
{
    private const string LogSource = "Install";

    /// <summary>The process names of the program and its screen saver copy.</summary>
    private static readonly string[] ProcessNames =
        [Path.GetFileNameWithoutExtension(InstallLocations.ProgramFileName), InstallLocations.ScreenSaverFileName];

    /// <summary>Asks every Holiday Lights process that runs from a folder (this process excepted) to close, and ends those that do not.</summary>
    /// <param name="folder">The program folder.</param>
    /// <param name="timeout">How long a process with a window may take to close before it is ended.</param>
    /// <param name="log">The log.</param>
    /// <returns>How many processes were running from the folder.</returns>
    public static int CloseAll(string folder, TimeSpan timeout, IAppLog log)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(log);
        List<Process> running = [.. ProcessNames.SelectMany(Process.GetProcessesByName)];
        try
        {
            List<Process> inFolder = [.. running.Where(p => p.Id != Environment.ProcessId && IsInside(ImagePath(p), folder))];
            foreach (Process process in inFolder)
            {
                // A window (a settings-only session) may close itself; a preview has no main window of its own.
                if (process.MainWindowHandle == 0 || !process.CloseMainWindow())
                {
                    TryKill(process);
                }
            }

            foreach (Process process in inFolder)
            {
                if (!process.WaitForExit(timeout))
                {
                    TryKill(process);
                    process.WaitForExit(timeout);
                }
            }

            if (inFolder.Count > 0)
            {
                log.Info(LogSource, $"Closed {inFolder.Count} Holiday Lights process(es) running from the program folder.");
            }

            return inFolder.Count;
        }
        finally
        {
            running.ForEach(p => p.Dispose());
        }
    }

    /// <summary>True when a file is inside a folder (or one of its subfolders).</summary>
    /// <param name="path">The file, or null when unknown.</param>
    /// <param name="folder">The folder.</param>
    /// <returns>True when inside.</returns>
    internal static bool IsInside(string? path, string folder) =>
        path is not null
        && Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string? ImagePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Ended meanwhile, or another user's: not ours to close.
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Already ended, or not ours; copying then reports a file in use.
        }
    }
}
