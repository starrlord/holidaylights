using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace HolidayLights.App.Install;

/// <summary>Copies and removes the program files of the per-user installation (PRODUCT-SPEC 6.10).</summary>
public static class ProgramFiles
{
    /// <summary>The installer copy of the program in the distribution folder; it is not installed.</summary>
    public const string SetupFileName = "Setup.exe";

    /// <summary>Copies every file of the distribution folder (subfolders included) into the program folder, replacing older copies.</summary>
    /// <param name="source">The distribution folder (where the installer runs from).</param>
    /// <param name="target">The program folder.</param>
    /// <param name="progress">Receives each file's relative path.</param>
    /// <returns>The relative paths of the installed files.</returns>
    public static IReadOnlyList<string> Copy(string source, string target, IProgress<string>? progress = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(target);
        var installed = new List<string>();
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            if (string.Equals(relative, SetupFileName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(relative, InstallManifest.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            installed.Add(relative);
            progress?.Report(relative);
        }

        return installed;
    }

    /// <summary>Creates <c>Holiday Lights.scr</c>, the screen saver: a copy of the program under that name (Windows lists it as "Holiday Lights").</summary>
    /// <param name="folder">The program folder.</param>
    /// <returns>The relative path of the screen saver.</returns>
    public static string CreateScreenSaver(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        File.Copy(Path.Combine(folder, InstallLocations.ProgramFileName), Path.Combine(folder, InstallLocations.ScreenSaverFileName), overwrite: true);
        return InstallLocations.ScreenSaverFileName;
    }

    /// <summary>Deletes the installed files (those in use, such as the running uninstaller, stay for <see cref="DeleteFolderAfterExit(string, int)"/>).</summary>
    /// <param name="folder">The program folder.</param>
    /// <param name="manifest">The installation's marker.</param>
    /// <returns>The files that could not be deleted yet.</returns>
    public static IReadOnlyList<string> Delete(string folder, InstallManifest manifest)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(manifest);
        var remaining = new List<string>();
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        foreach (string relative in manifest.Files.Append(InstallManifest.FileName))
        {
            // Only files inside the program folder, whatever the marker says.
            string path = Path.GetFullPath(Path.Combine(folder, relative));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                remaining.Add(relative);
            }
        }

        return remaining;
    }

    /// <summary>Removes the program folder now when nothing is left in it but empty folders.</summary>
    /// <param name="folder">The program folder.</param>
    /// <returns>True when the folder is gone.</returns>
    public static bool TryDeleteEmptyFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (!Directory.Exists(folder))
        {
            return true;
        }

        if (Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any())
        {
            return false;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the program folder once this process (the running uninstaller, whose files stay locked while it runs) has
    /// ended. Only a folder named <see cref="InstallLocations.ProgramFolderName"/> is ever removed.
    /// </summary>
    /// <param name="folder">The program folder.</param>
    /// <returns>True when the removal was scheduled.</returns>
    public static bool DeleteFolderAfterExit(string folder) => DeleteFolderAfterExit(folder, Environment.ProcessId);

    /// <summary>
    /// Removes the program folder once a process has ended, however long that takes (the uninstaller's "removed" page can
    /// stay open). A hidden command checks every second whether the process still runs; once it has ended it removes the
    /// folder, trying again for a few seconds while Windows releases the files. The command runs in the temporary folder,
    /// so it never keeps the folder busy itself. Only a folder named <see cref="InstallLocations.ProgramFolderName"/> is
    /// ever removed.
    /// </summary>
    /// <param name="folder">The program folder.</param>
    /// <param name="processId">The process to wait for.</param>
    /// <returns>True when the removal was scheduled.</returns>
    public static bool DeleteFolderAfterExit(string folder, int processId)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(full), InstallLocations.ProgramFolderName, StringComparison.OrdinalIgnoreCase)
            || Path.GetPathRoot(full) == full || full.IndexOfAny(['"', '%']) >= 0)
        {
            return false;
        }

        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            Arguments = CleanupCommand(full, processId),
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath(),
        };
        using Process? process = Process.Start(start);
        return process is not null;
    }

    /// <summary>The arguments of the hidden <c>cmd.exe</c> that waits for a process and then removes a folder.</summary>
    /// <param name="folder">The full path of the folder (without quotes or percent signs, which cmd would not keep).</param>
    /// <param name="processId">The process to wait for.</param>
    /// <returns>The arguments.</returns>
    internal static string CleanupCommand(string folder, int processId)
    {
        string system = Environment.SystemDirectory;
        string tasklist = Path.Combine(system, "tasklist.exe");
        string find = Path.Combine(system, "find.exe");
        string ping = Path.Combine(system, "PING.EXE");
        string pid = processId.ToString(CultureInfo.InvariantCulture);
        string wait = $"\"{ping}\" -n 2 127.0.0.1 >nul";

        // Forever, once a second: while tasklist still lists the process, wait; then up to 10 tries to remove the folder.
        string removeFolder = $"for /l %j in (1,1,10) do (if exist \"{folder}\\\" (rd /s /q \"{folder}\" 2>nul & {wait}))";
        return $"/d /c for /l %i in (0,0,1) do (\"{tasklist}\" /nh /fi \"PID eq {pid}\" 2>nul | \"{find}\" \" {pid} \" >nul || ({removeFolder} & exit) & {wait})";
    }
}
