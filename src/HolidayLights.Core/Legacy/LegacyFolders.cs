namespace HolidayLights.Core.Legacy;

/// <summary>The 5.4 content folders inside its program folder and how 5.4 listed them.</summary>
internal static class LegacyFolders
{
    /// <summary>Add-on bulbs.</summary>
    public const string Bulbs = "Holiday Lights Bulbs";

    /// <summary>Songs.</summary>
    public const string Music = "Holiday Lights Music";

    /// <summary>Screen saver pictures.</summary>
    public const string Pictures = "Holiday Lights Pictures";

    /// <summary>The 11 suffixes 5.4 played, tested on the shortcut-resolved name.</summary>
    public static IReadOnlyList<string> MusicExtensions { get; } = [".mid", ".rmi", ".aif", ".aifc", ".aiff", ".au", ".mp3", ".mpeg", ".snd", ".wma", ".wav"];

    private const FileAttributes Skipped = FileAttributes.Hidden | FileAttributes.Directory | FileAttributes.Temporary;

    /// <summary>
    /// Lists the files 5.4 saw in one of its folders: no hidden, directory or temporary entries (that hides
    /// <c>reset.mid</c>), in NTFS name order (case-insensitive ordinal), the order 5.4's <c>FindFirstFile</c> returned.
    /// </summary>
    /// <param name="programFolder">The 5.4 program folder, or null.</param>
    /// <param name="folder">One of <see cref="Bulbs"/>, <see cref="Music"/>, <see cref="Pictures"/>.</param>
    /// <param name="pattern">The search pattern (<c>*.bul</c> or <c>*</c>).</param>
    /// <returns>Full paths; empty when the folder does not exist or cannot be read.</returns>
    public static IReadOnlyList<string> ListFiles(string? programFolder, string folder, string pattern)
    {
        if (programFolder is null)
        {
            return [];
        }

        try
        {
            var directory = new DirectoryInfo(Path.Combine(programFolder, folder));
            if (!directory.Exists)
            {
                return [];
            }

            var options = new EnumerationOptions { AttributesToSkip = Skipped, IgnoreInaccessible = true, MatchType = MatchType.Win32 };
            return [.. directory.EnumerateFiles(pattern, options).Select(f => f.FullName).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>True when 5.4 would list a song (its own or its shortcut target's suffix).</summary>
    /// <param name="resolvedPath">The file, or a shortcut's target.</param>
    /// <returns>True for a supported song.</returns>
    public static bool IsMusic(string resolvedPath) =>
        MusicExtensions.Any(e => resolvedPath.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
