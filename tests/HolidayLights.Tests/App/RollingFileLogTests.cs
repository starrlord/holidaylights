using HolidayLights.App.Shell;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.App;

/// <summary>The log of PRODUCT-SPEC 6.6.5: <c>Logs\</c>, 3 rotating files of 1 MB, thread-safe, never throws.</summary>
public sealed class RollingFileLogTests : IDisposable
{
    private readonly TempDataRoot root = new();

    public void Dispose() => root.Dispose();

    private string Folder => root.Paths.LogsFolder;

    [Fact]
    public void EntriesReachTheLogsFolder()
    {
        using (var log = new RollingFileLog(root.Paths))
        {
            log.Info("Test", "Lights started.");
            log.Error("Test", "Something failed.", new InvalidOperationException("broken"));
        }

        string text = File.ReadAllText(Path.Combine(Folder, RollingFileLog.FileName));
        Assert.Contains("INFO  [", text);
        Assert.Contains("Test: Lights started.", text);
        Assert.Contains("ERROR", text);
        Assert.Contains("    System.InvalidOperationException: broken", text);
    }

    /// <summary>Review r1 #75: exception details never name the account through the profile folder.</summary>
    [Fact]
    public void ExceptionDetailsLeaveOutTheProfileFolder()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using var log = new RollingFileLog(Folder, "t", AppLogLevel.Info, RollingFileLog.MaxFileBytes);

        string text = log.Format(DateTime.Now, AppLogLevel.Warning, "Test", "Copy failed.", new IOException($"Could not find '{profile.ToUpperInvariant()}\\Documents\\Holiday Lights\\Music\\x.mid'."));

        Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%\\Documents\\Holiday Lights\\Music\\x.mid", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugEntriesAreSkippedByDefault()
    {
        using (var log = new RollingFileLog(Folder, "t", AppLogLevel.Info, RollingFileLog.MaxFileBytes))
        {
            log.Write(AppLogLevel.Debug, "Test", "noise");
            log.Info("Test", "signal");
        }

        string text = File.ReadAllText(Path.Combine(Folder, RollingFileLog.FileName));
        Assert.DoesNotContain("noise", text);
        Assert.Contains("signal", text);
    }

    [Fact]
    public void FlushWritesAtOnce()
    {
        using var log = new RollingFileLog(Folder, "t", AppLogLevel.Debug, RollingFileLog.MaxFileBytes);
        log.Info("Test", "now");
        log.Flush();
        Assert.Contains("now", File.ReadAllText(log.CurrentFile));
    }

    [Fact]
    public void FilesRotateAndOnlyThreeAreKept()
    {
        using (var log = new RollingFileLog(Folder, "t", AppLogLevel.Info, maxFileBytes: 2_000))
        {
            for (int i = 0; i < 200; i++)
            {
                log.Info("Test", $"Entry {i} {new string('x', 40)}");
                if (i % 10 == 0)
                {
                    log.Flush();
                }
            }
        }

        string[] files = [.. Directory.GetFiles(Folder).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        Assert.Equal(["HolidayLights.1.log", "HolidayLights.2.log", "HolidayLights.log"], files);
        Assert.Contains("Entry 199", File.ReadAllText(Path.Combine(Folder, "HolidayLights.log")));
        Assert.DoesNotContain("Entry 0 ", string.Concat(Directory.GetFiles(Folder).Select(File.ReadAllText)));
    }

    [Fact]
    public void ManyThreadsWriteWithoutLosingLines()
    {
        using (var log = new RollingFileLog(Folder, "t", AppLogLevel.Info, RollingFileLog.MaxFileBytes))
        {
            Parallel.For(0, 8, thread =>
            {
                for (int i = 0; i < 100; i++)
                {
                    log.Info("Thread", $"{thread}:{i}");
                }
            });
        }

        string[] lines = File.ReadAllLines(Path.Combine(Folder, RollingFileLog.FileName));
        Assert.Equal(800, lines.Count(l => l.Contains(" Thread: ", StringComparison.Ordinal)));
    }

    [Fact]
    public void TwoLogsOfTwoProcessesShareTheFiles()
    {
        using (var app = new RollingFileLog(Folder, "app", AppLogLevel.Info, RollingFileLog.MaxFileBytes))
        using (var saver = new RollingFileLog(Folder, "saver", AppLogLevel.Info, RollingFileLog.MaxFileBytes))
        {
            for (int i = 0; i < 50; i++)
            {
                app.Info("App", $"a{i}");
                saver.Info("Saver", $"s{i}");
            }
        }

        string[] lines = File.ReadAllLines(Path.Combine(Folder, RollingFileLog.FileName));
        Assert.Equal(50, lines.Count(l => l.Contains("[app] App: a", StringComparison.Ordinal)));
        Assert.Equal(50, lines.Count(l => l.Contains("[saver] Saver: s", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnUnwritableFolderNeverThrows()
    {
        string file = Path.Combine(root.Root, "not-a-folder");
        File.WriteAllText(file, "a file where the folder should be");
        using var log = new RollingFileLog(file, "t", AppLogLevel.Info, RollingFileLog.MaxFileBytes);
        log.Error("Test", "nowhere to go");
        log.Flush();
        log.Dispose();
        log.Info("Test", "after dispose");
    }
}
