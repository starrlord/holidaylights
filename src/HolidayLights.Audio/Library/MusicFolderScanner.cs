using System.Runtime.InteropServices;

namespace HolidayLights.Audio.Library;

/// <summary>Size and last write time of a file: probe results stay valid while both are unchanged.</summary>
/// <param name="Length">Bytes.</param>
/// <param name="LastWriteUtc">Last write time.</param>
internal readonly record struct FileStamp(long Length, DateTime LastWriteUtc)
{
    /// <summary>Reads the stamp of a file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The stamp (length -1 when the file is missing).</returns>
    public static FileStamp Of(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : new FileStamp(-1, default);
    }
}

/// <summary>A song file found in a music folder.</summary>
/// <param name="FilePath">The file (the <c>.lnk</c> for shortcuts).</param>
/// <param name="Origin">Bundled or My Music.</param>
/// <param name="Kind">The type (of the target for shortcuts).</param>
/// <param name="ShortcutTarget">The resolved target of a shortcut, else null.</param>
/// <param name="Stamp">The stamp of the audio file (the target for shortcuts).</param>
internal sealed record SongFile(string FilePath, MediaOrigin Origin, SongKind Kind, string? ShortcutTarget, FileStamp Stamp)
{
    /// <summary>The file name with extension.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>The song id (<see cref="MediaIds"/>).</summary>
    public string Id => Origin == MediaOrigin.Bundled ? MediaIds.Bundled(FileName) : MediaIds.User(FileName);

    /// <summary>The file that holds the audio (the target for shortcuts).</summary>
    public string AudioPath => ShortcutTarget ?? FilePath;
}

/// <summary>
/// Lists the songs of a folder as 5.4 did: every file, skipping hidden and temporary ones, kept when
/// its extension (of the resolved target, for <c>.lnk</c> shortcuts) is one of the music types.
/// </summary>
internal static class MusicFolderScanner
{
    private const string LogSource = "Audio.Library";

    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.Temporary | FileAttributes.Directory,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
    };

    /// <summary>Lists the songs of a folder.</summary>
    /// <param name="folder">The folder (a missing folder has no songs).</param>
    /// <param name="origin">Bundled or My Music.</param>
    /// <param name="shell">Resolves shortcuts.</param>
    /// <param name="log">The log.</param>
    /// <returns>The songs in enumeration order.</returns>
    public static IReadOnlyList<SongFile> Scan(string folder, MediaOrigin origin, IShellOperations shell, IAppLog log)
    {
        var songs = new List<SongFile>();
        try
        {
            var directory = new DirectoryInfo(folder);
            if (!directory.Exists)
            {
                return songs;
            }

            foreach (FileInfo file in directory.EnumerateFiles("*", Options))
            {
                if (Describe(file, origin, shell) is { } song)
                {
                    songs.Add(song);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            log.Warn(LogSource, "A music folder couldn't be read completely.", ex);
        }

        return songs;
    }

    private static SongFile? Describe(FileInfo file, MediaOrigin origin, IShellOperations shell)
    {
        if (!SongFileTypes.IsShortcut(file.Name))
        {
            return SongFileTypes.TryGetKind(file.Name, out SongKind kind)
                ? new SongFile(file.FullName, origin, kind, null, new FileStamp(file.Length, file.LastWriteTimeUtc))
                : null;
        }

        string? target = ResolveShortcut(shell, file.FullName);
        return target is not null && SongFileTypes.TryGetKind(target, out SongKind targetKind)
            ? new SongFile(file.FullName, origin, targetKind, target, FileStamp.Of(target))
            : null;
    }

    /// <summary>Resolves a shortcut; a shortcut that cannot be resolved is not a song.</summary>
    /// <param name="shell">The shell.</param>
    /// <param name="shortcut">The <c>.lnk</c> file.</param>
    /// <returns>The target, or null.</returns>
    public static string? ResolveShortcut(IShellOperations shell, string shortcut)
    {
        try
        {
            return shell.ResolveShortcut(shortcut);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
