namespace HolidayLights.Platform.Legacy;

/// <summary>
/// The Startup-folder shortcuts 5.4 recognized as its own: <c>*Holiday Lights*.lnk</c>,
/// with their resolved targets.
/// </summary>
internal static class LegacyStartupShortcuts
{
    /// <summary>The pattern 5.4 searched for.</summary>
    public const string Pattern = "*Holiday Lights*.lnk";

    /// <summary>Lists the matching shortcuts, A-Z.</summary>
    /// <param name="folder">The Startup folder.</param>
    /// <param name="shell">Resolves the shortcuts.</param>
    /// <returns>Shortcut paths with their targets (null when unresolvable); empty when the folder is missing.</returns>
    public static IReadOnlyList<KeyValuePair<string, string?>> Find(string folder, IShellOperations shell)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        var options = new EnumerationOptions { MatchType = MatchType.Win32, AttributesToSkip = FileAttributes.Directory, IgnoreInaccessible = true };
        return
        [
            .. Directory.EnumerateFiles(folder, Pattern, options)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(path => new KeyValuePair<string, string?>(path, shell.ResolveShortcut(path))),
        ];
    }
}
