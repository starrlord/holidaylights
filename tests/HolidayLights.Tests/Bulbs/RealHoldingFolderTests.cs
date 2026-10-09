using HolidayLights.Core.Bulbs;
using HolidayLights.Platform.Files;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.Bulbs;

/// <summary>
/// "Remove Bulb" and its undo with platform's real holding folder (CONTRACTS 6.5): the My Bulbs file leaves the folder and
/// the list, comes back on restore, and nothing reaches the Recycle Bin before the Settings window commits the session.
/// </summary>
public sealed class RealHoldingFolderTests : IDisposable
{
    private readonly TempDataRoot root = new();

    public void Dispose() => root.Dispose();

    [Fact]
    public async Task RemoveAndRestore_MoveTheFileThroughTheHoldingFolder()
    {
        var shell = new CountingShell();
        using var holding = new HoldingFolder(root.Paths, shell, new RecordingLog());
        using var catalog = new BulbCatalog(root.Paths, new InMemorySettingsStore(), holding, new RecordingLog());
        await catalog.StartAsync().WaitAsync(TimeSpan.FromSeconds(60));
        string file = Path.Combine(DataPaths.EnsureFolder(root.Paths.MyBulbsFolder), "Mine.bul");
        File.Copy(Path.Combine(BulbGoldens.BundledBulbsFolder, "Snowman.bul"), file);
        BulbInfo mine = catalog.LoadUserBulb(file)!;
        Assert.Equal(BulbIds.User("Mine"), mine.Id);

        HeldItem held = catalog.RemoveUserBulb(mine.Id);

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(held.HeldPath));
        Assert.DoesNotContain(catalog.All, b => BulbIds.Comparer.Equals(b.Id, mine.Id));

        catalog.RestoreUserBulb(held);

        Assert.True(File.Exists(file));
        Assert.Contains(catalog.All, b => BulbIds.Comparer.Equals(b.Id, mine.Id));
        Assert.Equal(0, shell.Recycled);
    }

    /// <summary>Counts Recycle Bin requests; nothing else is expected.</summary>
    private sealed class CountingShell : IShellOperations
    {
        public int Recycled { get; private set; }

        public void OpenFolder(string path) => throw new NotSupportedException();

        public void ShowInFolder(string filePath) => throw new NotSupportedException();

        public void OpenSettingsUri(string uri) => throw new NotSupportedException();

        public void OpenProjectHomePage() => throw new NotSupportedException();

        public void OpenControlPanel(string arguments) => throw new NotSupportedException();

        public bool MoveToRecycleBin(string path)
        {
            Recycled++;
            return false;
        }

        public string? ResolveShortcut(string shortcutPath) => throw new NotSupportedException();
    }
}
