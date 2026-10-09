namespace HolidayLights.Platform.Files;

/// <summary>File-or-folder helpers for the holding folder.</summary>
internal static class FileSystemEntry
{
    /// <summary>True when a file or a folder exists at the path.</summary>
    /// <param name="path">The path.</param>
    /// <returns>True when something is there.</returns>
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Moves a file or a folder (folders across drives are copied, then removed).</summary>
    /// <param name="source">What to move.</param>
    /// <param name="destination">Where to (must not exist).</param>
    /// <exception cref="FileNotFoundException">Nothing exists at <paramref name="source"/>.</exception>
    public static void Move(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Move(source, destination);
            return;
        }

        if (!Directory.Exists(source))
        {
            throw new FileNotFoundException("Nothing exists at this path.", source);
        }

        if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(source, destination);
            return;
        }

        CopyFolder(source, destination);
        Directory.Delete(source, recursive: true);
    }

    /// <summary>The path itself when it is free, else the first free "name (n).ext" with n = 2, 3, ...</summary>
    /// <param name="path">The wanted path.</param>
    /// <returns>A path where nothing exists.</returns>
    public static string FreeName(string path)
    {
        if (!Exists(path))
        {
            return path;
        }

        string folder = Path.GetDirectoryName(path) ?? "";
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int n = 2; ; n++)
        {
            string candidate = Path.Combine(folder, $"{stem} ({n}){extension}");
            if (!Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>True when the path is on a local fixed drive, the only kind with a Recycle Bin (network and removable drives delete for good).</summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>True when recycling there is safe.</returns>
    public static bool HasRecycleBin(string path)
    {
        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string folder in Directory.EnumerateDirectories(source))
        {
            CopyFolder(folder, Path.Combine(destination, Path.GetFileName(folder)));
        }
    }
}
