using System.Diagnostics;
using HolidayLights.App.Install;
using HolidayLights.App.Shell;
using HolidayLights.Tests.App.Fakes;
using HolidayLights.Tests.Shared;
using Microsoft.Win32;

namespace HolidayLights.Tests.App;

/// <summary>
/// The per-user installer and uninstaller (PRODUCT-SPEC 6.10), in a private data root with system changes off: the
/// program folder, the screen saver copy, the marker, the Apps entry (under a test registry key) and the removals.
/// </summary>
public sealed class InstallerTests : IDisposable
{
    private readonly TempDataRoot root = new();
    private readonly string distribution;
    private readonly string testKey = TestRegistryKeys.NewPath();
    private readonly FakeRegistrations registrations = new();
    private int stops;
    private bool running;

    public InstallerTests()
    {
        distribution = Path.Combine(root.Root, "dist", "Holiday Lights 6.0.0");
        Write("HolidayLights.exe", "program");
        Write("HolidayLights.dll", "library");
        Write("Setup.exe", "installer copy");
        Write(@"Content\Bulbs\Star.bul", "bulb");
        Write(@"Content\Music\Song.mid", "song");
    }

    public void Dispose()
    {
        TestRegistryKeys.Delete(testKey);
        root.Dispose();
    }

    private void Write(string relative, string content)
    {
        string path = Path.Combine(distribution, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private FakeServices Services(string installFolder) => new()
    {
        Paths = DataPaths.ForDataRoot(root.Root, installFolder),
        Options = new AppRuntimeOptions { AllowSystemChanges = false, Session = AppSessionKind.Setup },
        Log = new RecordingLog(),
        Settings = new InMemorySettingsStore(),
        Startup = registrations,
        FileAssociation = registrations,
        ScreenSaverRegistration = registrations,
        Shell = registrations,
    };

    private Installer Installer(string installFolder) => new(Services(installFolder), new UninstallEntry(Registry.CurrentUser, testKey), Stop);

    private bool Stop(IProgress<string>? progress)
    {
        stops++;
        return running;
    }

    private string ProgramFolder => Path.Combine(root.Root, "Programs", "HolidayLights");

    [Fact]
    public void TheProgramFolderIsUnderLocalAppDataOrTheDataRoot()
    {
        Assert.Equal(ProgramFolder, InstallLocations.ProgramFolder(DataPaths.ForDataRoot(root.Root)));
        string normal = InstallLocations.ProgramFolder(new DataPaths("roaming", "local", "documents", "install"));
        Assert.EndsWith(@"AppData\Local\Programs\HolidayLights", normal, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(@"Start Menu\Programs\Holiday Lights.lnk", InstallLocations.StartMenuShortcut(DataPaths.ForDataRoot(root.Root)));
    }

    [Fact]
    public void InstallingCopiesTheProgramAndMakesTheScreenSaver()
    {
        Installer installer = Installer(distribution);
        var steps = new List<string>();
        installer.Install(new SynchronousProgress(steps.Add));

        Assert.Equal(1, stops);
        Assert.Equal(ProgramFolder, installer.TargetFolder);
        Assert.Equal("program", File.ReadAllText(Path.Combine(ProgramFolder, "HolidayLights.exe")));
        Assert.Equal("program", File.ReadAllText(Path.Combine(ProgramFolder, "Holiday Lights.scr")));
        Assert.True(File.Exists(Path.Combine(ProgramFolder, @"Content\Bulbs\Star.bul")));
        Assert.False(File.Exists(Path.Combine(ProgramFolder, "Setup.exe")));

        InstallManifest manifest = InstallManifest.Read(ProgramFolder)!;
        Assert.Equal(VersionInfo.ProgramVersion, manifest.Version);
        Assert.Equal(
            ["Content\\Bulbs\\Star.bul", "Content\\Music\\Song.mid", "Holiday Lights.scr", "HolidayLights.dll", "HolidayLights.exe"],
            manifest.Files.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Contains("Copying files…", steps);

        // No system changes: neither the Start menu entry nor the Apps entry.
        Assert.False(File.Exists(InstallLocations.StartMenuShortcut(DataPaths.ForDataRoot(root.Root))));
        Assert.Null(new UninstallEntry(Registry.CurrentUser, testKey).ReadLocation());
    }

    [Fact]
    public void InstallingAgainUpdatesTheProgram()
    {
        Installer(distribution).Install();
        Write("HolidayLights.exe", "program 2");
        Installer(distribution).Install();
        Assert.Equal("program 2", File.ReadAllText(Path.Combine(ProgramFolder, "HolidayLights.exe")));
        Assert.Equal("program 2", File.ReadAllText(Path.Combine(ProgramFolder, "Holiday Lights.scr")));
    }

    [Fact]
    public void UpdatingRemovesTheFilesThePreviousVersionNoLongerHas()
    {
        Write(@"runtimes\old\Gone.dll", "old library");
        Write("Obsolete.dll", "old library");
        Installer(distribution).Install();
        File.WriteAllText(Path.Combine(ProgramFolder, "Notes.txt"), "not installed by the installer");

        File.Delete(Path.Combine(distribution, @"runtimes\old\Gone.dll"));
        File.Delete(Path.Combine(distribution, "Obsolete.dll"));
        Write("New.dll", "new library");
        Installer(distribution).Install();

        Assert.False(File.Exists(Path.Combine(ProgramFolder, "Obsolete.dll")));
        Assert.False(Directory.Exists(Path.Combine(ProgramFolder, "runtimes")), "A folder the update emptied is removed.");
        Assert.True(File.Exists(Path.Combine(ProgramFolder, "New.dll")));
        Assert.True(File.Exists(Path.Combine(ProgramFolder, "Notes.txt")), "Only files the installer installed are removed.");
        Assert.Equal(
            [@"Content\Bulbs\Star.bul", @"Content\Music\Song.mid", "Holiday Lights.scr", "HolidayLights.dll", "HolidayLights.exe", "New.dll"],
            InstallManifest.Read(ProgramFolder)!.Files.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void LeftoversOutsideTheProgramFolderAreNeverDeleted()
    {
        string outside = Path.Combine(root.Root, "outside.txt");
        File.WriteAllText(outside, "keep");
        Directory.CreateDirectory(ProgramFolder);
        Assert.Empty(ProgramFiles.DeleteLeftovers(ProgramFolder, [@"..\..\outside.txt", InstallManifest.FileName], []));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void TheKindFollowsTheInstalledVersion()
    {
        Installer installer = Installer(distribution);
        Assert.Null(installer.InstalledVersion);
        Assert.Equal(InstallKind.New, installer.Kind);

        installer.Install();
        Assert.Equal(VersionInfo.ProgramVersion, installer.InstalledVersion);
        Assert.Equal(InstallKind.Reinstall, installer.Kind);

        InstallManifest manifest = InstallManifest.Read(ProgramFolder)!;
        (manifest with { Version = "6.0.0" }).Write(ProgramFolder);
        Assert.Equal(InstallKind.Update, installer.Kind);
        (manifest with { Version = "99.0.0" }).Write(ProgramFolder);
        Assert.Equal(InstallKind.Downgrade, installer.Kind);
    }

    [Fact]
    public void TheInstallerTellsWhetherItClosedHolidayLights()
    {
        Installer installer = Installer(distribution);
        installer.Install();
        Assert.False(installer.ClosedRunningProgram);

        running = true;
        installer.Install();
        Assert.True(installer.ClosedRunningProgram);
    }

    [Fact]
    public void AQuietInstallNeedsNoWindowAndReportsFailures()
    {
        Assert.Equal(0, PerUserSetup.InstallQuietly(Services(distribution)));
        Assert.Equal("program", File.ReadAllText(Path.Combine(ProgramFolder, "Holiday Lights.scr")));

        // A file where the program folder belongs: the install fails, and the exit code says so.
        Directory.Delete(ProgramFolder, recursive: true);
        File.WriteAllText(ProgramFolder, "in the way");
        Assert.Equal(1, PerUserSetup.InstallQuietly(Services(distribution)));
    }

    [Fact]
    public void TheCopiesRunningFromTheProgramFolderAreClosed()
    {
        // Stands in for a screen saver preview: a long-running process started from a file named like the program.
        Directory.CreateDirectory(ProgramFolder);
        string program = Path.Combine(ProgramFolder, "HolidayLights.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), program);
        using Process preview = Process.Start(new ProcessStartInfo(program, "-n 60 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Assert.Equal(1, FolderPrograms.CloseAll(ProgramFolder, TimeSpan.FromSeconds(5), new RecordingLog()));
            Assert.True(preview.HasExited);
            Assert.Equal(0, FolderPrograms.CloseAll(ProgramFolder, TimeSpan.FromSeconds(5), new RecordingLog()));
        }
        finally
        {
            if (!preview.HasExited)
            {
                preview.Kill();
            }
        }
    }

    [Theory]
    [InlineData(@"C:\Programs\HolidayLights\HolidayLights.exe", true)]
    [InlineData(@"C:\Programs\HolidayLights\runtimes\a.dll", true)]
    [InlineData(@"C:\Programs\HolidayLights2\HolidayLights.exe", false)]
    [InlineData(@"C:\Programs\HolidayLights", false)]
    [InlineData(null, false)]
    public void OnlyProgramsInsideTheFolderAreClosed(string? path, bool inside) =>
        Assert.Equal(inside, FolderPrograms.IsInside(path, @"C:\Programs\HolidayLights\"));

    [Fact]
    public void UninstallingRemovesTheProgramAndKeepsTheUsersFiles()
    {
        Installer(distribution).Install();
        DataPaths paths = DataPaths.ForDataRoot(root.Root, ProgramFolder);
        Directory.CreateDirectory(paths.MyBulbsFolder);
        File.WriteAllText(Path.Combine(paths.MyBulbsFolder, "Mine.bul"), "mine");
        Directory.CreateDirectory(paths.CacheFolder);
        File.WriteAllText(paths.BulbIndexFile, "{}");
        Directory.CreateDirectory(paths.RoamingRoot);
        File.WriteAllText(paths.SettingsFile, "{}");

        Installer installed = Installer(ProgramFolder);
        Assert.True(installed.IsInstallation);
        installed.Uninstall(new UninstallChoices(RemoveDocuments: false, RemoveSettings: false));

        Assert.False(Directory.Exists(ProgramFolder));
        Assert.True(File.Exists(Path.Combine(paths.MyBulbsFolder, "Mine.bul")));
        Assert.True(File.Exists(paths.SettingsFile));
        Assert.False(Directory.Exists(paths.CacheFolder));
        Assert.Equal(["startup off", "association off"], registrations.Calls);
        Assert.Empty(registrations.Recycled);
    }

    [Fact]
    public void UninstallingRemovesTheUsersFilesOnlyWhenAsked()
    {
        Installer(distribution).Install();
        DataPaths paths = DataPaths.ForDataRoot(root.Root, ProgramFolder);
        Directory.CreateDirectory(paths.DocumentsRoot);
        Directory.CreateDirectory(paths.RoamingRoot);
        Directory.CreateDirectory(paths.LocalRoot);

        Installer(ProgramFolder).Uninstall(new UninstallChoices(RemoveDocuments: true, RemoveSettings: true));
        Assert.Equal([paths.DocumentsRoot, paths.RoamingRoot, paths.LocalRoot], registrations.Recycled);
    }

    [Fact]
    public void RemovingTheSettingsLeavesNoLogFolderBehind()
    {
        Installer(distribution).Install();
        DataPaths paths = DataPaths.ForDataRoot(root.Root, ProgramFolder);
        using var log = new RollingFileLog(paths);
        log.Info("Test", "Before the uninstall.");
        log.Flush();
        Assert.True(File.Exists(log.CurrentFile));

        registrations.RecycleBin = Path.Combine(root.Root, "Recycle Bin");
        FakeServices services = Services(ProgramFolder);
        services.Log = log;
        new Installer(services, new UninstallEntry(Registry.CurrentUser, testKey), Stop)
            .Uninstall(new UninstallChoices(RemoveDocuments: false, RemoveSettings: true));

        // What the uninstaller's host still logs afterwards ("Stopping.", "Stopped.").
        log.Info("Shell", "Stopped.");
        log.Dispose();
        Assert.Contains(paths.LocalRoot, registrations.Recycled);
        Assert.False(Directory.Exists(paths.LocalRoot), "The logs folder must not come back after the settings went to the Recycle Bin.");
    }

    [Fact]
    public async Task TheProgramFolderGoesOnlyOnceTheUninstallerHasEnded()
    {
        Directory.CreateDirectory(ProgramFolder);
        string program = Path.Combine(ProgramFolder, "HolidayLights.exe");
        File.WriteAllText(program, "program");

        // Stands in for the running uninstaller: a process that keeps the folder busy (its current folder) for about 3 s,
        // longer than the 3 s the removal used to wait.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n 5 127.0.0.1")
        {
            WorkingDirectory = ProgramFolder,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        using Process uninstaller = Process.Start(start)!;
        Task<string> output = uninstaller.StandardOutput.ReadToEndAsync();
        Assert.True(ProgramFiles.DeleteFolderAfterExit(ProgramFolder, uninstaller.Id));

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        Assert.True(File.Exists(program), "Nothing is removed while the uninstaller runs.");

        await uninstaller.WaitForExitAsync();
        await output;
        var gone = Stopwatch.StartNew();
        while (Directory.Exists(ProgramFolder) && gone.Elapsed < TimeSpan.FromSeconds(20))
        {
            await Task.Delay(100);
        }

        Assert.False(Directory.Exists(ProgramFolder), "The program folder is removed once the uninstaller has ended.");
    }

    [Fact]
    public void TheRemovalWaitsInTheTemporaryFolderForTheGivenProcess()
    {
        string command = ProgramFiles.CleanupCommand(ProgramFolder, 4242);
        Assert.StartsWith("/d /c for /l %i in (0,0,1) do (", command);
        Assert.Contains("/fi \"PID eq 4242\"", command);
        Assert.Contains($"rd /s /q \"{ProgramFolder}\"", command);
        Assert.DoesNotContain("%SystemRoot%", command, StringComparison.OrdinalIgnoreCase);
        Assert.False(ProgramFiles.DeleteFolderAfterExit(Path.Combine(root.Root, "100%", "HolidayLights"), 4242));
    }

    [Fact]
    public void TheScreenSaverWeReplacedComesBack()
    {
        Installer(distribution).Install();
        registrations.State = ScreenSaverState.Ours;
        Installer(ProgramFolder).Uninstall(new UninstallChoices(false, false));
        Assert.Contains("screen saver restored", registrations.Calls);
    }

    [Fact]
    public void AFolderTheInstallerDidNotCreateIsNeverRemoved()
    {
        Installer notInstalled = Installer(distribution);
        Assert.False(notInstalled.IsInstallation);
        notInstalled.Uninstall(new UninstallChoices(false, false));
        Assert.True(File.Exists(Path.Combine(distribution, "HolidayLights.exe")));
        Assert.False(ProgramFiles.DeleteFolderAfterExit(distribution));
    }

    [Fact]
    public void TheAppsEntryNamesTheProgramAndItsUninstaller()
    {
        var entry = new UninstallEntry(Registry.CurrentUser, testKey);
        entry.Write(ProgramFolder, "6.0.0", 25 * 1024 * 1024, new DateOnly(2026, 10, 8));
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(testKey)!)
        {
            Assert.Equal("Holiday Lights", key.GetValue("DisplayName"));
            Assert.Equal("6.0.0", key.GetValue("DisplayVersion"));
            Assert.Equal("StarrLord", key.GetValue("Publisher"));
            Assert.Equal("https://github.com/starrlord/holidaylights", key.GetValue("URLInfoAbout"));
            Assert.Equal("https://github.com/starrlord/holidaylights", key.GetValue("HelpLink"));
            Assert.Equal($"\"{Path.Combine(ProgramFolder, "HolidayLights.exe")}\" --uninstall", key.GetValue("UninstallString"));
            Assert.Equal("20261008", key.GetValue("InstallDate"));
            Assert.Equal(25600, key.GetValue("EstimatedSize"));
            Assert.Equal(1, key.GetValue("NoModify"));
        }

        Assert.Equal(ProgramFolder, entry.ReadLocation());
        entry.Delete();
        Assert.Null(entry.ReadLocation());
    }

    [Fact]
    public void FilesOutsideTheProgramFolderAreNeverDeleted()
    {
        string outside = Path.Combine(root.Root, "outside.txt");
        File.WriteAllText(outside, "keep");
        Directory.CreateDirectory(ProgramFolder);
        var manifest = new InstallManifest { Files = [@"..\..\outside.txt", "HolidayLights.exe"] };
        ProgramFiles.Delete(ProgramFolder, manifest);
        Assert.True(File.Exists(outside));
    }

    /// <summary>Reports synchronously (Progress&lt;T&gt; would post to the thread pool).</summary>
    private sealed class SynchronousProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    /// <summary>Records the registrations the uninstaller removes; never touches Windows.</summary>
    private sealed class FakeRegistrations : IStartupRegistration, IFileAssociation, IScreenSaverRegistration, IShellOperations
    {
        public List<string> Calls { get; } = [];

        public List<string> Recycled { get; } = [];

        /// <summary>When set, recycled folders are moved here (as the Recycle Bin would take them away).</summary>
        public string? RecycleBin { get; set; }

        public ScreenSaverState State { get; set; } = ScreenSaverState.NotOurs;

        public bool IsEnabled => false;

        public bool IsDisabledByWindows => false;

        public void Enable() => Calls.Add("startup on");

        public void Disable() => Calls.Add("startup off");

        public FileAssociationState GetState() => FileAssociationState.NotRegistered;

        public void Register() => Calls.Add("association on");

        public void Unregister() => Calls.Add("association off");

        public ScreenSaverStatus GetStatus() => new(State, null, 600, false);

        public ScreenSaverPrevious Use() => new();

        public void StopUsing(ScreenSaverPrevious? previous) => Calls.Add("screen saver restored");

        public void SetTimeout(TimeSpan timeout)
        {
        }

        public void TurnOn()
        {
        }

        public void OpenWindowsSettings()
        {
        }

        public void OpenFolder(string path)
        {
        }

        public void ShowInFolder(string filePath)
        {
        }

        public void OpenSettingsUri(string uri)
        {
        }

        public void OpenProjectHomePage()
        {
        }

        public void OpenControlPanel(string arguments)
        {
        }

        public bool MoveToRecycleBin(string path)
        {
            Recycled.Add(path);
            if (RecycleBin is not null)
            {
                Directory.CreateDirectory(RecycleBin);
                Directory.Move(path, Path.Combine(RecycleBin, Guid.NewGuid().ToString("N")));
            }

            return true;
        }

        public string? ResolveShortcut(string shortcutPath) => null;
    }
}
