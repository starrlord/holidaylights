namespace HolidayLights.Core.Themes;

/// <summary>A loaded theme and its file.</summary>
/// <param name="Theme">The normalized theme.</param>
/// <param name="FilePath">The theme file.</param>
internal sealed record ThemeLibraryEntry(ThemeDefinition Theme, string FilePath);

/// <summary>An immutable state of the theme library: its themes A-Z and the files that could not be read.</summary>
internal sealed class ThemeLibrarySnapshot
{
    /// <summary>Creates a snapshot.</summary>
    /// <param name="entries">The themes in any order.</param>
    /// <param name="problems">The unreadable files.</param>
    public ThemeLibrarySnapshot(IEnumerable<ThemeLibraryEntry> entries, IReadOnlyList<ThemeFileProblem> problems)
    {
        Entries = [.. entries.OrderBy(e => e.Theme.Name, StringComparer.CurrentCultureIgnoreCase)];
        Themes = [.. Entries.Select(e => e.Theme)];
        Problems = problems;
    }

    /// <summary>The state before <see cref="ThemeLibrary.Load"/>.</summary>
    public static ThemeLibrarySnapshot Empty { get; } = new([], []);

    /// <summary>The themes with their files, A-Z (current culture, case-insensitive).</summary>
    public IReadOnlyList<ThemeLibraryEntry> Entries { get; }

    /// <summary>The themes, A-Z.</summary>
    public IReadOnlyList<ThemeDefinition> Themes { get; }

    /// <summary>The unreadable files.</summary>
    public IReadOnlyList<ThemeFileProblem> Problems { get; }

    /// <summary>Finds a theme by name.</summary>
    /// <param name="name">The name (case-insensitive, surrounding spaces ignored).</param>
    /// <returns>The entry, or null.</returns>
    public ThemeLibraryEntry? Find(string name)
    {
        string trimmed = name.Trim();
        return Entries.FirstOrDefault(e => ThemeNames.Comparer.Equals(e.Theme.Name, trimmed));
    }

    /// <summary>True when a theme uses the file.</summary>
    /// <param name="path">A file path.</param>
    /// <returns>True when taken.</returns>
    public bool IsPathUsed(string path) => Entries.Any(e => string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Removes and/or adds an entry.</summary>
    /// <param name="removed">The entry to remove, or null.</param>
    /// <param name="added">The entry to add, or null.</param>
    /// <returns>The new snapshot.</returns>
    public ThemeLibrarySnapshot Replace(ThemeLibraryEntry? removed, ThemeLibraryEntry? added) =>
        new([.. Entries.Where(e => !ReferenceEquals(e, removed)), .. added is null ? [] : new[] { added }], Problems);

    /// <summary>Adds an unreadable file.</summary>
    /// <param name="problem">The problem.</param>
    /// <returns>The new snapshot.</returns>
    public ThemeLibrarySnapshot WithProblem(ThemeFileProblem problem) => new(Entries, [.. Problems, problem]);
}
