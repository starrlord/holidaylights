using System.IO;
using System.Security;
using HolidayLights.App.Shell;
using HolidayLights.Platform.Instance;
using HolidayLights.Platform.Integration;

namespace HolidayLights.App.Install;

/// <summary>What the uninstaller also removes (PRODUCT-SPEC 6.10).</summary>
/// <param name="RemoveDocuments">"Also remove your bulbs, songs and pictures in Documents\Holiday Lights" (default No).</param>
/// <param name="RemoveSettings">"Also remove my settings and themes" (default unchecked).</param>
public sealed record UninstallChoices(bool RemoveDocuments, bool RemoveSettings);

/// <summary>
/// The work of the per-user installer and uninstaller (PRODUCT-SPEC 6.10), without user interface: no administrator rights;
/// the program into <c>%LOCALAPPDATA%\Programs\HolidayLights\</c> with <c>Holiday Lights.scr</c> beside it, the Start menu
/// entry, the <c>.bul</c> association and the Windows Settings &gt; Apps entry; uninstalling restores the previous screen
/// saver, removes the Run value, the association, the shortcut and the entry, and removes user files only when asked. It
/// never touches Holiday Lights 5.4. Under <c>--no-system-changes</c> the registry and shortcut writes are skipped and logged.
/// </summary>
public sealed class Installer
{
    private const string LogSource = "Install";
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(15);

    private readonly IAppServices services;
    private readonly UninstallEntry entry;
    private readonly Action<IProgress<string>?> stopRunningInstance;

    /// <summary>Creates the installer.</summary>
    /// <param name="services">The services (paths, options, log, platform registrations).</param>
    public Installer(IAppServices services)
        : this(services, new UninstallEntry(), null)
    {
    }

    /// <summary>Creates the installer with its uninstall entry and the way a running Holiday Lights is ended (tests never touch a real one).</summary>
    /// <param name="services">The services.</param>
    /// <param name="entry">The Windows Settings &gt; Apps entry.</param>
    /// <param name="stopRunningInstance">Ends a running Holiday Lights before its files change, or null for the instance pipe.</param>
    internal Installer(IAppServices services, UninstallEntry entry, Action<IProgress<string>?>? stopRunningInstance)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(entry);
        this.services = services;
        this.entry = entry;
        this.stopRunningInstance = stopRunningInstance ?? StopRunningInstance;
    }

    /// <summary>The folder the installer runs from (the distribution folder, or an installation).</summary>
    public string SourceFolder => services.Paths.InstallFolder;

    /// <summary>The folder it installs into.</summary>
    public string TargetFolder => InstallLocations.ProgramFolder(services.Paths);

    /// <summary>The installed program.</summary>
    public string InstalledProgram => Path.Combine(TargetFolder, InstallLocations.ProgramFileName);

    /// <summary>Installs (or repairs and updates) Holiday Lights for this user.</summary>
    /// <param name="progress">Receives what is being done.</param>
    public void Install(IProgress<string>? progress = null)
    {
        string target = TargetFolder;
        services.Log.Info(LogSource, $"Installing Holiday Lights {VersionInfo.ProgramVersion}.");
        stopRunningInstance(progress);

        progress?.Report("Copying files…");
        Directory.CreateDirectory(target);
        bool inPlace = string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(SourceFolder)), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        List<string> files = inPlace
            ? [.. (InstallManifest.Read(target)?.Files ?? []).Where(f => !string.Equals(f, InstallLocations.ScreenSaverFileName, StringComparison.OrdinalIgnoreCase))]
            : [.. ProgramFiles.Copy(SourceFolder, target)];
        files.Add(ProgramFiles.CreateScreenSaver(target));
        new InstallManifest { Version = VersionInfo.ProgramVersion, InstalledAt = DateTimeOffset.Now, Files = files }.Write(target);

        progress?.Report("Registering Holiday Lights…");
        AppRuntimeOptions options = services.Options;
        DataPaths installed = InstallLocations.ForInstallFolder(services.Paths, target);
        new FileAssociation(installed, options, services.Log).Register();
        if (options.AllowSystemChanges)
        {
            ShellLink.Create(InstallLocations.StartMenuShortcut(services.Paths), InstalledProgram, "Festive lights around your screen");
            entry.Write(target, VersionInfo.ProgramVersion, SizeOf(target, files), DateOnly.FromDateTime(DateTime.Now));
        }
        else
        {
            services.Log.Info(LogSource, "No system changes: the Start menu entry and the Apps entry were not written.");
        }

        services.Log.Info(LogSource, $"Installed {files.Count} files.");
    }

    /// <summary>Starts the installed program (the installer's "Start Holiday Lights").</summary>
    public void StartInstalledProgram()
    {
        var start = new System.Diagnostics.ProcessStartInfo(InstalledProgram) { UseShellExecute = false, WorkingDirectory = TargetFolder };
        if (services.Paths.DataRoot is { } root)
        {
            start.ArgumentList.Add("--data-root");
            start.ArgumentList.Add(root);
        }

        if (!services.Options.AllowSystemChanges)
        {
            start.ArgumentList.Add("--no-system-changes");
        }

        using (System.Diagnostics.Process.Start(start))
        {
            // The program runs on its own.
        }
    }

    /// <summary>True when the folder this program runs from was installed by the installer (only then are program files removed).</summary>
    public bool IsInstallation => InstallManifest.Read(SourceFolder) is not null;

    /// <summary>Uninstalls the installation this program runs from.</summary>
    /// <param name="choices">What else to remove.</param>
    /// <param name="progress">Receives what is being done.</param>
    public void Uninstall(UninstallChoices choices, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(choices);
        services.Log.Info(LogSource, "Uninstalling Holiday Lights.");
        stopRunningInstance(progress);

        progress?.Report("Removing Holiday Lights from Windows…");
        RestoreScreenSaver();
        BestEffort("The Run value", services.Startup.Disable);
        BestEffort("The .bul association", services.FileAssociation.Unregister);
        if (services.Options.AllowSystemChanges)
        {
            BestEffort("The Start menu entry", () =>
            {
                string shortcut = InstallLocations.StartMenuShortcut(services.Paths);
                if (File.Exists(shortcut))
                {
                    File.Delete(shortcut);
                }
            });
            BestEffort("The Apps entry", entry.Delete);
        }

        progress?.Report("Removing files…");
        RemoveUserFiles(choices);
        if (InstallManifest.Read(SourceFolder) is { } manifest)
        {
            // The running uninstaller's own files can only go once it has ended (however long its last page stays open).
            ProgramFiles.Delete(SourceFolder, manifest);
            if (!ProgramFiles.TryDeleteEmptyFolder(SourceFolder) && !ProgramFiles.DeleteFolderAfterExit(SourceFolder))
            {
                services.Log.Warn(LogSource, "The program folder could not be scheduled for removal.");
            }
        }

        services.Log.Info(LogSource, "Uninstalled.");
    }

    /// <summary>Asks a running Holiday Lights to exit and waits for it (its files are replaced or removed next).</summary>
    private void StopRunningInstance(IProgress<string>? progress)
    {
        // With a data root only an instance on the same data root is asked to exit (InstanceNames.MutexFor).
        string? dataRoot = services.Paths.DataRoot;
        if (!RunningInstance.IsPresent(dataRoot))
        {
            return;
        }

        progress?.Report("Closing Holiday Lights…");
        using (var instance = new SingleInstance(services.Log, dataRoot))
        {
            InstanceClient.ForwardAsync(instance, InstanceCommand.Simple(InstanceCommandKind.Exit), services.Log).GetAwaiter().GetResult();
        }

        if (!RunningInstance.WaitForExit(dataRoot, ExitTimeout))
        {
            throw new InvalidOperationException("Holiday Lights is still running. Exit it from the red bulb in the notification area, then try again.");
        }
    }

    /// <summary>When Holiday Lights is the screen saver, the one used before comes back (or none).</summary>
    private void RestoreScreenSaver()
    {
        try
        {
            ScreenSaverStatus status = services.ScreenSaverRegistration.GetStatus();
            if (status.State is ScreenSaverState.Ours or ScreenSaverState.OursButTurnedOff)
            {
                services.ScreenSaverRegistration.StopUsing(services.Settings.Current.Saver.Previous);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            services.Log.Warn(LogSource, "The previous screen saver could not be restored.", e);
        }
    }

    /// <summary>The caches always go; the user's files and settings go to the Recycle Bin only when asked.</summary>
    private void RemoveUserFiles(UninstallChoices choices)
    {
        DataPaths paths = services.Paths;
        try
        {
            if (Directory.Exists(paths.CacheFolder))
            {
                Directory.Delete(paths.CacheFolder, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            services.Log.Warn(LogSource, "The cache could not be removed.", e);
        }

        if (choices.RemoveDocuments && Directory.Exists(paths.DocumentsRoot))
        {
            services.Shell.MoveToRecycleBin(paths.DocumentsRoot);
        }

        if (choices.RemoveSettings)
        {
            if (services.Log is RollingFileLog log && Directory.Exists(paths.LocalRoot)
                && IsInside(paths.LogsFolder, paths.LocalRoot))
            {
                // The log lives in the folder that goes now: a later line (this uninstaller's last ones) would create it again.
                services.Log.Info(LogSource, "Removing the settings and the logs; this log ends here.");
                log.StopWritingFiles();
            }

            foreach (string folder in new[] { paths.RoamingRoot, paths.LocalRoot }.Where(Directory.Exists))
            {
                services.Shell.MoveToRecycleBin(folder);
            }
        }
    }

    /// <summary>One step of the uninstaller: a failure is logged and the next step still runs.</summary>
    private void BestEffort(string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            services.Log.Warn(LogSource, $"{what} could not be removed.", e);
        }
    }

    private static bool IsInside(string path, string folder) =>
        (Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar)
            .StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static long SizeOf(string folder, IEnumerable<string> files) =>
        files.Select(f => new FileInfo(Path.Combine(folder, f))).Where(f => f.Exists).Sum(f => f.Length);
}
