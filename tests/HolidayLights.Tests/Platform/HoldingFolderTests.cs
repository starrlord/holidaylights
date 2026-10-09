using HolidayLights.Platform.Files;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Platform;

/// <summary>Uses a private data root; the "Recycle Bin" is a fake that moves files into a folder of that root.</summary>
public sealed class HoldingFolderTests : IDisposable
{
    private readonly TempDataRoot data = new();
    private readonly FakeRecycleBin bin;

    public HoldingFolderTests()
    {
        bin = new FakeRecycleBin(Path.Combine(data.Root, "RecycleBin"));
        Directory.CreateDirectory(data.Paths.MyBulbsFolder);
    }

    public void Dispose() => TestFolders.Delete(data);

    [Fact]
    public void Hold_MovesTheFileAsideAndListsIt()
    {
        string bulb = CreateFile(data.Paths.MyBulbsFolder, "Snow Family.bul", "snow");
        using var holding = new HoldingFolder(data.Paths, bin, new RecordingLog());

        HeldItem item = holding.Hold(bulb);

        Assert.False(File.Exists(bulb));
        Assert.Equal("snow", File.ReadAllText(item.HeldPath));
        Assert.StartsWith(data.Paths.RemovedFolder, item.HeldPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Snow Family.bul", Path.GetFileName(item.HeldPath));
        Assert.Equal(bulb, item.OriginalPath);
        Assert.Equal([item], holding.Items);
    }

    [Fact]
    public void Restore_PutsTheFileBack()
    {
        string bulb = CreateFile(data.Paths.MyBulbsFolder, "Candy Canes.bul", "candy");
        using var holding = new HoldingFolder(data.Paths, bin, new RecordingLog());
        HeldItem item = holding.Hold(bulb);

        string restored = holding.Restore(item);

        Assert.Equal(bulb, restored);
        Assert.Equal("candy", File.ReadAllText(bulb));
        Assert.Empty(holding.Items);
        Assert.False(Directory.Exists(Path.GetDirectoryName(item.HeldPath)));
        Assert.Throws<InvalidOperationException>(() => holding.Restore(item));
    }

    [Fact]
    public void Restore_KeepsAFileThatAppearedMeanwhile()
    {
        string bulb = CreateFile(data.Paths.MyBulbsFolder, "Star.bul", "old");
        using var holding = new HoldingFolder(data.Paths, bin, new RecordingLog());
        HeldItem item = holding.Hold(bulb);
        CreateFile(data.Paths.MyBulbsFolder, "Star.bul", "new");
        CreateFile(data.Paths.MyBulbsFolder, "Star (2).bul", "newer");

        string restored = holding.Restore(item);

        Assert.Equal(Path.Combine(data.Paths.MyBulbsFolder, "Star (3).bul"), restored);
        Assert.Equal("old", File.ReadAllText(restored));
        Assert.Equal("new", File.ReadAllText(bulb));
    }

    [Fact]
    public void SameNamedFiles_AreHeldSideBySide()
    {
        string first = CreateFile(data.Paths.MyBulbsFolder, "Tree.bul", "1");
        using var holding = new HoldingFolder(data.Paths, bin, new RecordingLog());
        HeldItem one = holding.Hold(first);
        string second = CreateFile(data.Paths.MyBulbsFolder, "Tree.bul", "2");
        HeldItem two = holding.Hold(second);

        Assert.NotEqual(one.HeldPath, two.HeldPath);
        Assert.Equal("2", File.ReadAllText(holding.Restore(two)));
        Assert.Equal("1", File.ReadAllText(holding.Restore(one)));
    }

    [Fact]
    public void Folders_CanBeHeldAndRestored()
    {
        string folder = Path.Combine(data.Paths.MyPicturesFolder, "Album");
        CreateFile(folder, "a.bmp", "a");
        CreateFile(Path.Combine(folder, "inner"), "b.bmp", "b");
        using var holding = new HoldingFolder(data.Paths, bin, new RecordingLog());

        HeldItem item = holding.Hold(folder + Path.DirectorySeparatorChar);
        Assert.False(Directory.Exists(folder));

        holding.Restore(item);
        Assert.Equal("b", File.ReadAllText(Path.Combine(folder, "inner", "b.bmp")));
    }

    [Fact]
    public void Hold_OfNothingThrows()
    {
        using var holding = new HoldingFolder(data.Paths, bin, new RecordingLog());

        Assert.Throws<FileNotFoundException>(() => holding.Hold(Path.Combine(data.Paths.MyBulbsFolder, "missing.bul")));
        Assert.Empty(holding.Items);
    }

    [Fact]
    public void CommitSession_RecyclesFromTheOriginalPlaceAndStartsANewSession()
    {
        string bulb = CreateFile(data.Paths.MyBulbsFolder, "Holly.bul", "holly");
        string song = CreateFile(data.Paths.MyMusicFolder, "Carol.mid", "carol");
        using var holding = new HoldingFolder(data.Paths, bin, new RecordingLog());
        holding.Hold(bulb);
        holding.Hold(song);

        holding.CommitSession();

        // The Recycle Bin remembers My Bulbs and My Music, so "Restore" there brings them home.
        Assert.Equal([bulb, song], bin.RecycledFrom);
        Assert.False(File.Exists(bulb));
        Assert.False(File.Exists(song));
        Assert.Empty(holding.Items);
        Assert.Empty(Directory.EnumerateFileSystemEntries(data.Paths.RemovedFolder));

        HeldItem next = holding.Hold(CreateFile(data.Paths.MyBulbsFolder, "Next.bul", "n"));
        Assert.True(File.Exists(next.HeldPath));
    }

    [Fact]
    public void CommitSession_KeepsWhatCannotBeRecycledForTheNextStart()
    {
        string bulb = CreateFile(data.Paths.MyBulbsFolder, "Stuck.bul", "stuck");
        using var holding = new HoldingFolder(data.Paths, bin, new RecordingLog());
        HeldItem item = holding.Hold(bulb);
        bin.Fail = true;

        holding.CommitSession();

        Assert.False(File.Exists(bulb));
        Assert.True(File.Exists(item.HeldPath));

        bin.Fail = false;
        using (var nextStart = new HoldingFolder(data.Paths, bin, new RecordingLog()))
        {
            nextStart.RecycleLeftovers();
        }

        Assert.Equal([bulb], bin.RecycledFrom);
        Assert.Empty(Directory.EnumerateFileSystemEntries(data.Paths.RemovedFolder));
    }

    [Fact]
    public void RecycleLeftovers_RecyclesCrashedSessionsButNeverALiveOne()
    {
        string crashed = CreateFile(data.Paths.MyBulbsFolder, "Crashed.bul", "c");
        string live = CreateFile(data.Paths.MyBulbsFolder, "Live.bul", "l");
        using var crashedSession = new HoldingFolder(data.Paths, bin, new RecordingLog());
        HeldItem crashedItem = crashedSession.Hold(crashed);
        SimulateCrash(crashedItem);
        using var liveSession = new HoldingFolder(data.Paths, bin, new RecordingLog());
        HeldItem liveItem = liveSession.Hold(live);

        using (var nextStart = new HoldingFolder(data.Paths, bin, new RecordingLog()))
        {
            nextStart.RecycleLeftovers();
        }

        Assert.Equal([crashed], bin.RecycledFrom);
        Assert.True(File.Exists(liveItem.HeldPath));
        Assert.Equal(live, liveSession.Restore(liveItem));
    }

    [Fact]
    public void RecycleLeftovers_WithoutAnOriginRecyclesInPlace()
    {
        string orphan = CreateFile(Path.Combine(data.Paths.RemovedFolder, "20251224-120000-abcd", "0"), "Orphan.bul", "o");

        using (var nextStart = new HoldingFolder(data.Paths, bin, new RecordingLog()))
        {
            nextStart.RecycleLeftovers();
        }

        Assert.Equal([orphan], bin.RecycledFrom);
        Assert.Empty(Directory.EnumerateFileSystemEntries(data.Paths.RemovedFolder));
    }

    [Fact]
    public void RecycleLeftovers_WithoutAHoldingFolderDoesNothing()
    {
        using (var nextStart = new HoldingFolder(data.Paths, bin, new RecordingLog()))
        {
            nextStart.RecycleLeftovers();
        }

        Assert.Empty(bin.RecycledFrom);
    }

    [Fact]
    public void ConcurrentHolds_AreAllKept()
    {
        string[] files = [.. Enumerable.Range(0, 32).Select(i => CreateFile(data.Paths.MyBulbsFolder, $"b{i}.bul", $"{i}"))];
        using var holding = new HoldingFolder(data.Paths, bin, new RecordingLog());

        Parallel.ForEach(files, file => holding.Hold(file));

        Assert.Equal(32, holding.Items.Count);
        Assert.Equal(32, holding.Items.Select(i => i.HeldPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static string CreateFile(string folder, string name, string content)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A crashed process leaves its session folder without the lock it held.</summary>
    private static void SimulateCrash(HeldItem item)
    {
        string session = Path.GetDirectoryName(Path.GetDirectoryName(item.HeldPath)!)!;
        string copy = session + "-crashed";
        CopyFolder(session, copy);
        File.Delete(Path.Combine(copy, "session.lock"));
        File.Delete(item.HeldPath);
    }

    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            if (!file.EndsWith("session.lock", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }

        foreach (string folder in Directory.GetDirectories(source))
        {
            CopyFolder(folder, Path.Combine(destination, Path.GetFileName(folder)));
        }
    }

    private sealed class FakeRecycleBin(string folder) : IShellOperations
    {
        public List<string> RecycledFrom { get; } = [];

        public bool Fail { get; set; }

        public bool MoveToRecycleBin(string path)
        {
            if (Fail)
            {
                return false;
            }

            lock (RecycledFrom)
            {
                Directory.CreateDirectory(folder);
                File.Move(path, Path.Combine(folder, $"{RecycledFrom.Count}-{Path.GetFileName(path)}"));
                RecycledFrom.Add(path);
            }

            return true;
        }

        public void OpenFolder(string path) => throw new NotSupportedException();

        public void ShowInFolder(string filePath) => throw new NotSupportedException();

        public void OpenSettingsUri(string uri) => throw new NotSupportedException();

        public void OpenProjectHomePage() => throw new NotSupportedException();

        public void OpenControlPanel(string arguments) => throw new NotSupportedException();

        public string? ResolveShortcut(string shortcutPath) => throw new NotSupportedException();
    }
}
