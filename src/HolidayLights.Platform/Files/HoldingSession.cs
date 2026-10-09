using System.Globalization;

namespace HolidayLights.Platform.Files;

/// <summary>
/// One session folder of the holding folder, marked as alive by <c>session.lock</c>: opened without sharing and deleted
/// on close, so it disappears when the process ends (also in a crash) and an unopenable lock means "in use".
/// </summary>
internal sealed class HoldingSession
{
    private const string LockFileName = "session.lock";

    private readonly FileStream lockFile;

    private HoldingSession(string folder, FileStream lockFile)
    {
        Folder = folder;
        this.lockFile = lockFile;
    }

    /// <summary>The session folder.</summary>
    public string Folder { get; }

    /// <summary>Creates a new session folder under the holding root and locks it.</summary>
    /// <param name="root">The holding root.</param>
    /// <returns>The session.</returns>
    public static HoldingSession Create(string root)
    {
        for (int attempt = 0; ; attempt++)
        {
            string name = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Random.Shared.Next(0x10000):x4}");
            string folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            try
            {
                var lockFile = new FileStream(
                    Path.Combine(folder, LockFileName), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                return new HoldingSession(folder, lockFile);
            }
            catch (IOException) when (attempt < 3)
            {
                // Another session took this name in the same second: pick another one.
            }
        }
    }

    /// <summary>True when a live process holds the session's lock. A stale lock (power loss) is removed.</summary>
    /// <param name="folder">A session folder.</param>
    /// <returns>True when the folder belongs to a running session.</returns>
    public static bool IsInUse(string folder)
    {
        string path = Path.Combine(folder, LockFileName);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }

            File.Delete(path);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Removes a session folder when nothing is left in it.</summary>
    /// <param name="folder">The folder.</param>
    public static void DeleteIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Retried by the next RecycleLeftovers.
        }
    }

    /// <summary>Releases the lock (the lock file deletes itself) and removes the folder when it is empty.</summary>
    public void Close()
    {
        lockFile.Dispose();
        DeleteIfEmpty(Folder);
    }
}
