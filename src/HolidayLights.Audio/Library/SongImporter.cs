using System.Security.Cryptography;

namespace HolidayLights.Audio.Library;

/// <summary>
/// "Add Song..." and dropped files (PRODUCT-SPEC 3.4.3): copies music files and <c>.lnk</c> shortcuts into My Music
/// (5.4 copied too). The same content already listed is skipped; a different file with the same name is saved as
/// "&lt;name&gt; (2)". One instance serves one batch, so duplicates inside the batch are found as well.
/// </summary>
internal sealed class SongImporter
{
    private readonly string folder;
    private readonly IShellOperations shell;
    private readonly List<(string Id, string Path, string? ShortcutTarget)> known;
    private readonly Dictionary<string, byte[]> hashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prepares a batch.</summary>
    /// <param name="folder">My Music (existing).</param>
    /// <param name="listed">The songs listed now.</param>
    /// <param name="shell">Resolves shortcuts.</param>
    public SongImporter(string folder, IEnumerable<SongInfo> listed, IShellOperations shell)
    {
        this.folder = folder;
        this.shell = shell;
        known = listed.Select(s => (s.Id, s.FilePath, s.ShortcutTarget)).ToList();
    }

    /// <summary>Adds one file.</summary>
    /// <param name="source">The file chosen or dropped.</param>
    /// <returns>What happened.</returns>
    public MediaImportResult Add(string source)
    {
        string path;
        try
        {
            path = Path.GetFullPath(source);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new MediaImportResult(source, MediaImportOutcome.Failed, null, ex.Message);
        }

        bool isShortcut = SongFileTypes.IsShortcut(path);
        string? target = isShortcut ? MusicFolderScanner.ResolveShortcut(shell, path) : null;
        if ((isShortcut && target is null) || !SongFileTypes.TryGetKind(target ?? path, out _))
        {
            return new MediaImportResult(source, MediaImportOutcome.Unsupported, null, null);
        }

        try
        {
            if (FindSame(path, target) is { } existing)
            {
                return new MediaImportResult(source, MediaImportOutcome.AlreadyPresent, existing, null);
            }

            string originalName = Path.GetFileName(path);
            string name = FreeName(originalName);
            string destination = Path.Combine(folder, name);
            File.Copy(path, destination, overwrite: false);
            File.SetAttributes(destination, File.GetAttributes(destination) & ~(FileAttributes.Hidden | FileAttributes.Temporary));
            string id = MediaIds.User(name);
            known.Add((id, destination, target));
            return new MediaImportResult(
                source, name == originalName ? MediaImportOutcome.Added : MediaImportOutcome.Renamed, id, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MediaImportResult(source, MediaImportOutcome.Failed, null, ex.Message);
        }
    }

    /// <summary>The id of a listed song with the same content (a shortcut: the same target).</summary>
    private string? FindSame(string path, string? shortcutTarget)
    {
        if (shortcutTarget is not null)
        {
            return known.FirstOrDefault(k => k.ShortcutTarget is { } t && PathEquals(t, shortcutTarget)).Id;
        }

        long length = new FileInfo(path).Length;
        foreach (var (id, songPath, target) in known)
        {
            if (target is null && (PathEquals(songPath, path) || SameContent(path, length, songPath)))
            {
                return id;
            }
        }

        return null;
    }

    private bool SameContent(string path, long length, string other)
    {
        var info = new FileInfo(other);
        return info.Exists && info.Length == length && Hash(path).AsSpan().SequenceEqual(Hash(other));
    }

    private byte[] Hash(string path)
    {
        if (!hashes.TryGetValue(path, out byte[]? hash))
        {
            using FileStream stream = File.OpenRead(path);
            hash = SHA256.HashData(stream);
            hashes[path] = hash;
        }

        return hash;
    }

    /// <summary>The file name, or "&lt;name&gt; (2)", "(3)" ... when My Music already has a file of that name.</summary>
    private string FreeName(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        string candidate = fileName;
        for (int n = 2; File.Exists(Path.Combine(folder, candidate)); n++)
        {
            candidate = $"{stem} ({n}){extension}";
        }

        return candidate;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
