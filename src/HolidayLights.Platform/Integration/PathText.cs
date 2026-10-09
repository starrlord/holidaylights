using HolidayLights.Platform.Native;

namespace HolidayLights.Platform.Integration;

/// <summary>Paths inside registry values: the program of a command line, normalization and comparison.</summary>
internal static class PathText
{
    /// <summary>
    /// The program of a command line: the quoted first token (<c>"C:\x\HolidayLights.exe" --autostart</c>), else the text
    /// up to the first <c>.exe</c>/<c>.scr</c> that ends a token (<c>C:\Program Files\x.exe /s</c>), else up to the first space.
    /// </summary>
    /// <param name="commandLine">The command line.</param>
    /// <returns>The program path, or null for an empty command.</returns>
    public static string? ProgramOf(string? commandLine)
    {
        string text = commandLine?.Trim() ?? "";
        if (text.Length == 0)
        {
            return null;
        }

        if (text[0] == '"')
        {
            int close = text.IndexOf('"', 1);
            string quoted = close < 0 ? text[1..] : text[1..close];
            return quoted.Length > 0 ? quoted : null;
        }

        foreach (string extension in (ReadOnlySpan<string>)[".exe", ".scr"])
        {
            for (int at = text.IndexOf(extension, StringComparison.OrdinalIgnoreCase); at >= 0;
                 at = text.IndexOf(extension, at + 1, StringComparison.OrdinalIgnoreCase))
            {
                int end = at + extension.Length;
                if (end == text.Length || char.IsWhiteSpace(text[end]))
                {
                    return text[..end];
                }
            }
        }

        int space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }

    /// <summary>A path for comparison: quotes removed, environment variables expanded, absolute, and long names for 8.3 names of existing files.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The normalized path, or null when it is empty or invalid.</returns>
    public static string? Normalize(string? path)
    {
        string text = Environment.ExpandEnvironmentVariables(path?.Trim().Trim('"') ?? "");
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            string full = Path.GetFullPath(text);
            return File.Exists(full) || Directory.Exists(full) ? LongName(full) : full;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>True when two paths name the same file (case-insensitive after <see cref="Normalize"/>).</summary>
    /// <param name="a">One path.</param>
    /// <param name="b">The other path.</param>
    /// <returns>True when they match.</returns>
    public static bool SameFile(string? a, string? b)
    {
        string? left = Normalize(a);
        return left is not null && string.Equals(left, Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The long form of an existing path (<c>C:\WINDOWS\system32\HOLIDA~1.SCR</c> becomes its real name).</summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The long path, or <paramref name="path"/> when Windows cannot expand it.</returns>
    public static unsafe string LongName(string path)
    {
        // The first call returns the size needed (with the terminator), the second the length written (without it).
        uint length = NativeMethods.GetLongPathName(path, null, 0);
        if (length == 0)
        {
            return path;
        }

        var buffer = new char[length];
        fixed (char* text = buffer)
        {
            uint written = NativeMethods.GetLongPathName(path, text, length);
            return written > 0 && written < length ? new string(text, 0, (int)written) : path;
        }
    }
}
