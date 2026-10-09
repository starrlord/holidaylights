using System.Text;

namespace HolidayLights.Core.Settings;

/// <summary>
/// Atomic text-file writes (CONTRACTS 4): the text goes to a temporary file in the same folder, is flushed to disk and
/// then renamed over the target, so a crash or a full disk never leaves a half-written settings or theme file.
/// </summary>
internal static class AtomicFile
{
    /// <summary>The extension of temporary files (<c>&lt;file&gt;.&lt;guid&gt;.tmp</c>).</summary>
    public const string TemporaryExtension = ".tmp";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes UTF-8 text (without a byte order mark) to <paramref name="path"/> atomically.</summary>
    /// <param name="path">The target file; its folder is created when missing.</param>
    /// <param name="text">The text.</param>
    /// <exception cref="IOException">The file could not be written (disk full, locked, ...).</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or the target is not writable.</exception>
    public static void WriteAllText(string path, string text)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("The path has no folder.", nameof(path));
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}{TemporaryExtension}");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(Utf8WithoutBom.GetBytes(text));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>Deletes temporary files that a crash left behind in a folder (best effort).</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="targetPattern">The targets whose leftovers are removed, e.g. <c>settings.json</c> or <c>*.json</c>.</param>
    public static void DeleteLeftovers(string folder, string targetPattern)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (string leftover in Directory.EnumerateFiles(folder, $"{targetPattern}.*{TemporaryExtension}"))
            {
                TryDelete(leftover);
            }
        }
        catch (IOException)
        {
            // Leftovers are harmless; they are tried again at the next start.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A temporary file that cannot be deleted now is removed by DeleteLeftovers later.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
