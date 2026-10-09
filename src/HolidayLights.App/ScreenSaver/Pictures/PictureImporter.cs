using System.IO;
using System.Security.Cryptography;

namespace HolidayLights.App.ScreenSaver.Pictures;

/// <summary>
/// "Add..." on the Screen Saver page (PRODUCT-SPEC 3.5.6): copies pictures into My Pictures (5.4 copied too). The same
/// content already listed is not copied again; a different file with the same name is saved as "&lt;name&gt; (2)". One
/// instance serves one batch, so duplicates inside the batch are found as well.
/// </summary>
internal sealed class PictureImporter
{
    private readonly string folder;
    private readonly List<(string Id, string Path)> known;
    private readonly Dictionary<string, byte[]> hashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prepares a batch.</summary>
    /// <param name="folder">My Pictures (existing).</param>
    /// <param name="listed">The pictures listed now.</param>
    public PictureImporter(string folder, IEnumerable<PictureInfo> listed)
    {
        ArgumentNullException.ThrowIfNull(listed);
        this.folder = folder;
        known = [.. listed.Select(p => (p.Id, p.FilePath))];
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

        if (!PictureFiles.IsPicture(path))
        {
            return new MediaImportResult(source, MediaImportOutcome.Unsupported, null, null);
        }

        try
        {
            if (FindSame(path) is { } existing)
            {
                return new MediaImportResult(source, MediaImportOutcome.AlreadyPresent, existing, null);
            }

            string originalName = Path.GetFileName(path);
            string name = FreeName(originalName);
            string destination = Path.Combine(folder, name);
            File.Copy(path, destination, overwrite: false);
            File.SetAttributes(destination, File.GetAttributes(destination) & ~(FileAttributes.Hidden | FileAttributes.Temporary | FileAttributes.ReadOnly));
            string id = MediaIds.User(name);
            known.Add((id, destination));
            return new MediaImportResult(source, name == originalName ? MediaImportOutcome.Added : MediaImportOutcome.Renamed, id, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MediaImportResult(source, MediaImportOutcome.Failed, null, ex.Message);
        }
    }

    /// <summary>The id of a listed picture with the same content (or the same file).</summary>
    private string? FindSame(string path)
    {
        long length = new FileInfo(path).Length;
        foreach ((string id, string picturePath) in known)
        {
            if (PathEquals(picturePath, path) || SameContent(path, length, picturePath))
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

    /// <summary>The file name, or "&lt;name&gt; (2)", "(3)" ... when My Pictures already has a file of that name.</summary>
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
