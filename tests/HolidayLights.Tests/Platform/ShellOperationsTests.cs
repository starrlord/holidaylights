using System.Security.Principal;
using System.Text;
using HolidayLights.Platform.Native;
using HolidayLights.Platform.Shell;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Platform;

/// <summary>
/// Shell helpers that open no window. The Recycle Bin test recycles a uniquely named temporary file and then removes
/// exactly that entry from the user's Recycle Bin again, so nothing is left behind.
/// </summary>
public sealed class ShellOperationsTests : IDisposable
{
    private readonly TempDataRoot data = new();
    private readonly RecordingLog log = new();
    private readonly ShellOperations shell;

    public ShellOperationsTests() => shell = new ShellOperations(log);

    public void Dispose() => TestFolders.Delete(data);

    [Fact]
    public void MoveToRecycleBin_RecyclesAFile()
    {
        string file = Path.Combine(data.Root, $"Holiday Lights test {Guid.NewGuid():N}.bul");
        File.WriteAllText(file, "a bulb");
        try
        {
            Assert.True(shell.MoveToRecycleBin(file));
            Assert.False(File.Exists(file));
        }
        finally
        {
            Assert.True(PurgeFromRecycleBin(file), "The test entry was not found in the Recycle Bin.");
        }
    }

    [Fact]
    public void MoveToRecycleBin_OfNothingIsFalse()
    {
        Assert.False(shell.MoveToRecycleBin(Path.Combine(data.Root, "missing.bul")));
    }

    [Fact]
    public void MoveToRecycleBin_NeverDeletesWhereThereIsNoRecycleBin()
    {
        Assert.False(shell.MoveToRecycleBin(@"\\localhost\no-such-share-for-holiday-lights\x.bul"));
    }

    [Fact]
    public async Task ResolveShortcut_ReadsTheTarget()
    {
        string target = Path.Combine(data.Root, "Holiday Lights.exe");
        File.WriteAllText(target, "program");
        string shortcut = Path.Combine(data.Root, "Holiday Lights.lnk");
        CreateShortcut(shortcut, target);

        Assert.Equal(target, shell.ResolveShortcut(shortcut), ignoreCase: true);
        Assert.Equal(target, await Task.Run(() => shell.ResolveShortcut(shortcut)), ignoreCase: true);
    }

    [Fact]
    public void ResolveShortcut_KeepsTheStoredPathOfAMissingTarget()
    {
        string target = Path.Combine(data.Root, "Gone", "Holiday Lights.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "program");
        string shortcut = Path.Combine(data.Root, "Old.lnk");
        CreateShortcut(shortcut, target);
        File.Delete(target);

        Assert.Equal(target, shell.ResolveShortcut(shortcut), ignoreCase: true);
    }

    [Fact]
    public void ResolveShortcut_OfSomethingElseIsNull()
    {
        string notAShortcut = Path.Combine(data.Root, "text.lnk");
        File.WriteAllText(notAShortcut, "not a shortcut");

        Assert.Null(shell.ResolveShortcut(notAShortcut));
        Assert.Null(shell.ResolveShortcut(Path.Combine(data.Root, "missing.lnk")));
    }

    [Theory]
    [InlineData("https://www.tigertech.com/")]
    [InlineData("http://example.com")]
    [InlineData("file:///C:/Windows")]
    public void OpenSettingsUri_RefusesAnythingButWindowsSettings(string uri)
    {
        shell.OpenSettingsUri(uri);

        Assert.Contains(log.Entries, e => e.Level == AppLogLevel.Warning && e.Message.Contains("offline", StringComparison.Ordinal));
    }

    private static void CreateShortcut(string shortcut, string target) => ComApartment.Run(() =>
    {
        IShellLinkW link = ComApartment.Create<IShellLinkW>(ShellClassIds.ShellLink);
        try
        {
            Assert.Equal(0, link.SetPath(target));
            Assert.Equal(0, ((IPersistFile)link).Save(shortcut, true));
            return 0;
        }
        finally
        {
            ComApartment.Release(link);
        }
    });

    /// <summary>Deletes the Recycle Bin entry ($I info file and $R data file) whose original path is the given one.</summary>
    private static bool PurgeFromRecycleBin(string originalPath)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string bin = Path.Combine(Path.GetPathRoot(originalPath)!, "$Recycle.Bin", identity.User!.Value);
        bool found = false;
        foreach (string info in Directory.GetFiles(bin, "$I*"))
        {
            if (string.Equals(OriginalPathOf(File.ReadAllBytes(info)), originalPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(Path.Combine(bin, "$R" + Path.GetFileName(info)[2..]));
                File.Delete(info);
                found = true;
            }
        }

        return found;
    }

    /// <summary>The original path in a $I file: version 2 has a length at offset 24 and the text at 28; version 1 a fixed field at 24.</summary>
    private static string OriginalPathOf(byte[] info)
    {
        if (info.Length < 28)
        {
            return "";
        }

        long version = BitConverter.ToInt64(info, 0);
        int start = version >= 2 ? 28 : 24;
        string text = Encoding.Unicode.GetString(info, start, info.Length - start);
        int end = text.IndexOf('\0', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }
}
