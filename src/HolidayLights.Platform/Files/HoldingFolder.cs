using System.Globalization;
using System.Text;

namespace HolidayLights.Platform.Files;

/// <summary>The removal holding folder with Recycle Bin commit (see <see cref="IHoldingFolder"/>). Owner: platform.</summary>
/// <remarks>
/// <para>Layout: <c>Removed\&lt;session&gt;\&lt;n&gt;\&lt;file&gt;</c> plus <c>&lt;n&gt;.origin</c> (the original path, UTF-8) and
/// <c>session.lock</c>, held open (delete-on-close) while the session lives so another process never recycles files a
/// running Settings window can still bring back. The session folder is created on the first removal.</para>
/// <para>Committing moves each file back to where it was and recycles it from there, so the Recycle Bin's "Restore"
/// returns it to My Bulbs, My Music or My Pictures. Files on drives without a Recycle Bin (network, removable) are
/// recycled from the holding folder instead, and a file is never deleted permanently: one that cannot be recycled stays
/// held and is tried again by <see cref="RecycleLeftovers"/> at the next start.</para>
/// <para>Recycling is synchronous and the shell needs a moment per file (about 20-150 ms with <c>IFileOperation</c>), so a
/// caller that may hold many files can call <see cref="CommitSession"/> off the UI thread (the class is thread-safe).</para>
/// <para>Disposing releases the session lock without recycling (the process is ending); what is still held is picked up
/// by <see cref="RecycleLeftovers"/> at the next start.</para>
/// </remarks>
public sealed class HoldingFolder : IHoldingFolder, IDisposable
{
    private const string LogSource = "Platform.Holding";
    private const string OriginExtension = ".origin";

    private readonly string root;
    private readonly IShellOperations shell;
    private readonly IAppLog log;
    private readonly Lock gate = new();
    private readonly List<HeldItem> items = [];
    private HoldingSession? session;
    private int nextSlot;

    /// <summary>Creates the holding folder for a new session.</summary>
    /// <param name="paths">The holding root (<see cref="DataPaths.RemovedFolder"/>).</param>
    /// <param name="shell">Sends files to the Recycle Bin.</param>
    /// <param name="log">The log.</param>
    public HoldingFolder(DataPaths paths, IShellOperations shell, IAppLog log)
    {
        root = paths.RemovedFolder;
        this.shell = shell;
        this.log = log;
    }

    /// <inheritdoc />
    public IReadOnlyList<HeldItem> Items
    {
        get
        {
            lock (gate)
            {
                return [.. items];
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="FileNotFoundException">Nothing exists at <paramref name="path"/>.</exception>
    /// <exception cref="IOException">The file is in use or cannot be moved.</exception>
    public HeldItem Hold(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string original = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!FileSystemEntry.Exists(original))
        {
            throw new FileNotFoundException("There is nothing to remove at this path.", original);
        }

        lock (gate)
        {
            session ??= HoldingSession.Create(root);
            string slot = Path.Combine(session.Folder, (nextSlot++).ToString(CultureInfo.InvariantCulture));
            string held = Path.Combine(slot, Path.GetFileName(original));
            Directory.CreateDirectory(slot);
            try
            {
                File.WriteAllText(slot + OriginExtension, original, Encoding.UTF8);
                FileSystemEntry.Move(original, held);
            }
            catch
            {
                DeleteSlot(slot);
                throw;
            }

            var item = new HeldItem(original, held, DateTimeOffset.Now);
            items.Add(item);
            log.Info(LogSource, $"Moved a {Kind(original)} file to the holding folder.");
            return item;
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The item is not held in this session.</exception>
    public string Restore(HeldItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (gate)
        {
            int index = items.IndexOf(item);
            if (index < 0)
            {
                throw new InvalidOperationException("This file is not in the holding folder of this session.");
            }

            string target = FileSystemEntry.FreeName(item.OriginalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            FileSystemEntry.Move(item.HeldPath, target);
            items.RemoveAt(index);
            DeleteSlot(Path.GetDirectoryName(item.HeldPath)!);
            log.Info(LogSource, $"Restored a {Kind(target)} file.");
            return target;
        }
    }

    /// <inheritdoc />
    public void CommitSession()
    {
        lock (gate)
        {
            if (session is null)
            {
                return;
            }

            int recycled = 0;
            foreach (HeldItem item in items)
            {
                if (Recycle(item.HeldPath, item.OriginalPath))
                {
                    recycled++;
                    DeleteSlot(Path.GetDirectoryName(item.HeldPath)!);
                }
            }

            log.Info(LogSource, $"Sent {recycled} of {items.Count} removed file(s) to the Recycle Bin.");
            items.Clear();
            session.Close();
            session = null;
            nextSlot = 0;
        }
    }

    /// <summary>Releases the session lock; held files stay for <see cref="RecycleLeftovers"/>.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            session?.Close();
            session = null;
            items.Clear();
            nextSlot = 0;
        }
    }

    /// <inheritdoc />
    public void RecycleLeftovers()
    {
        lock (gate)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (string folder in Directory.GetDirectories(root))
            {
                if ((session is not null && string.Equals(folder, session.Folder, StringComparison.OrdinalIgnoreCase)) || HoldingSession.IsInUse(folder))
                {
                    continue;
                }

                try
                {
                    RecycleLeftoverSession(folder);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    log.Warn(LogSource, "Files left by an earlier session could not be recycled; trying again next time.", e);
                }
            }
        }
    }

    private void RecycleLeftoverSession(string folder)
    {
        int recycled = 0;
        foreach (string slot in Directory.GetDirectories(folder))
        {
            string originFile = slot + OriginExtension;
            string? original = File.Exists(originFile) ? File.ReadAllText(originFile, Encoding.UTF8).Trim() : null;
            bool clean = true;
            foreach (string held in Directory.GetFileSystemEntries(slot))
            {
                bool done = Recycle(held, string.IsNullOrEmpty(original) ? held : original);
                clean &= done;
                recycled += done ? 1 : 0;
            }

            if (clean)
            {
                DeleteSlot(slot);
            }
        }

        foreach (string originFile in Directory.GetFiles(folder, "*" + OriginExtension))
        {
            // An origin whose slot is gone (a crash between restoring and cleaning up).
            DeleteSlot(Path.ChangeExtension(originFile, null));
        }

        HoldingSession.DeleteIfEmpty(folder);
        log.Info(LogSource, $"Sent {recycled} file(s) left by an earlier session to the Recycle Bin.");
    }

    /// <summary>Recycles a held file from its original place when possible, else from the holding folder; never deletes.</summary>
    private bool Recycle(string heldPath, string originalPath)
    {
        string from = heldPath;
        if (!string.Equals(heldPath, originalPath, StringComparison.OrdinalIgnoreCase) &&
            FileSystemEntry.HasRecycleBin(originalPath) &&
            !FileSystemEntry.Exists(originalPath) &&
            Directory.Exists(Path.GetDirectoryName(originalPath)))
        {
            try
            {
                FileSystemEntry.Move(heldPath, originalPath);
                from = originalPath;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Warn(LogSource, $"Could not put a {Kind(originalPath)} file back before recycling it.", e);
            }
        }

        if (shell.MoveToRecycleBin(from))
        {
            return true;
        }

        if (!string.Equals(from, heldPath, StringComparison.OrdinalIgnoreCase))
        {
            // Keep it held, so the next start tries again instead of it reappearing for good.
            try
            {
                FileSystemEntry.Move(from, heldPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Warn(LogSource, $"A {Kind(from)} file could not be recycled and stays where it was.", e);
                return false;
            }
        }

        log.Warn(LogSource, $"A {Kind(from)} file could not be sent to the Recycle Bin; it stays in the holding folder.");
        return false;
    }

    /// <summary>Removes an emptied slot folder and its origin file (only when nothing else is left in it).</summary>
    private static void DeleteSlot(string slot)
    {
        try
        {
            if (Directory.Exists(slot) && !Directory.EnumerateFileSystemEntries(slot).Any())
            {
                Directory.Delete(slot);
            }

            if (!Directory.Exists(slot))
            {
                File.Delete(slot + OriginExtension);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the next RecycleLeftovers; nothing of the user's is in an emptied slot.
        }
    }

    /// <summary>
    /// What a file is, for the log: its extension only. The names of the user's songs, pictures and bulbs are personal
    /// (CONTRACTS 4; review r1 #75).
    /// </summary>
    private static string Kind(string path) => Path.GetExtension(path) is { Length: > 1 } extension ? extension.ToLowerInvariant() : "removed";
}
