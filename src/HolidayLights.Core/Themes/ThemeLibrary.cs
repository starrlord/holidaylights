using HolidayLights.Core.Settings;

namespace HolidayLights.Core.Themes;

/// <summary>
/// Theme files in <see cref="DataPaths.ThemesFolder"/> plus the 19 embedded shipped originals (<c>assets/themes</c>,
/// PRODUCT-SPEC 6.4, 7.2). See <see cref="IThemeLibrary"/>. Owner: core-settings.
/// </summary>
/// <remarks>
/// <para>Queries read an immutable snapshot and are safe on any thread; mutations are serialized and raise
/// <see cref="Changed"/> on the calling thread. Files are written atomically and deleted themes go to the holding
/// folder, so Undo and Cancel can bring them back.</para>
/// <para>A theme is a shipped theme when its name is one of the 19 shipped names (<see cref="ThemeDefinition.Shipped"/> is
/// kept in step with the name): replacing "Halloween" with your own values keeps it in the 5.4 group with a "Changed"
/// badge, as 5.4 let you replace its installer themes.</para>
/// </remarks>
public sealed class ThemeLibrary : IThemeLibrary
{
    private const string LogSource = "Themes";

    private readonly DataPaths paths;
    private readonly IHoldingFolder holding;
    private readonly IAppLog log;
    private readonly object gate = new();
    private volatile ThemeLibrarySnapshot snapshot = ThemeLibrarySnapshot.Empty;

    /// <summary>Creates the library; call <see cref="Load"/>.</summary>
    /// <param name="paths">The themes folder.</param>
    /// <param name="holding">Where deleted theme files go.</param>
    /// <param name="log">The log.</param>
    public ThemeLibrary(DataPaths paths, IHoldingFolder holding, IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(holding);
        ArgumentNullException.ThrowIfNull(log);
        this.paths = paths;
        this.holding = holding;
        this.log = log;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<ThemeDefinition> Themes => snapshot.Themes;

    /// <inheritdoc />
    public IReadOnlyList<ThemeFileProblem> Problems => snapshot.Problems;

    /// <inheritdoc />
    public IReadOnlyList<ThemeDefinition> ShippedOriginals => ShippedThemes.All;

    /// <inheritdoc />
    /// <remarks>Folder errors are logged; the library then holds the themes it could read.</remarks>
    public void Load()
    {
        lock (gate)
        {
            string folder = paths.ThemesFolder;
            if (!Directory.Exists(folder))
            {
                Seed(folder);
            }

            snapshot = ReadFolder(folder);
        }

        OnChanged();
    }

    /// <inheritdoc />
    public ThemeDefinition? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return snapshot.Find(name)?.Theme;
    }

    /// <inheritdoc />
    /// <remarks>Replacing a theme keeps its name's spelling and its file, like 5.4's registry key.</remarks>
    /// <exception cref="ArgumentException">The name is not valid.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Save(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        RequireValidName(theme.Name, nameof(theme));
        lock (gate)
        {
            SaveLocked(theme);
        }

        OnChanged();
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">There is no theme of that name.</exception>
    public HeldItem Delete(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        HeldItem held;
        lock (gate)
        {
            ThemeLibrarySnapshot current = snapshot;
            ThemeLibraryEntry entry = current.Find(name) ?? throw new ArgumentException($"There is no theme named \"{name}\".", nameof(name));
            held = holding.Hold(entry.FilePath);
            snapshot = current.Replace(entry, null);
        }

        log.Info(LogSource, "A theme was deleted (moved to the holding folder).");
        OnChanged();
        return held;
    }

    /// <inheritdoc />
    /// <remarks>When a theme of the same name appeared meanwhile, the restored theme is renamed "&lt;name&gt; (2)".</remarks>
    public void Restore(HeldItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (gate)
        {
            string path = holding.Restore(item);
            ThemeLibrarySnapshot current = snapshot;
            ThemeReadResult read = ThemeFile.Read(path);
            if (read.Theme is not { } theme)
            {
                snapshot = current.WithProblem(new ThemeFileProblem(path, read.Problem ?? "the file is unreadable"));
            }
            else
            {
                if (current.Find(theme.Name) is not null)
                {
                    theme = ThemeNormalizer.Normalize(theme, UniqueName(current, theme.Name));
                    ThemeFile.Write(path, theme);
                }

                snapshot = current.Replace(null, new ThemeLibraryEntry(theme, path));
            }
        }

        OnChanged();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Another theme of the new name is never replaced (<see cref="ArgumentException"/>); to replace it after the "Replace
    /// Theme?" confirmation, <see cref="Delete"/> it first (which also makes the replacement undoable).
    /// </remarks>
    /// <exception cref="ArgumentException">The old theme does not exist, the new name is invalid or taken.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    public void Rename(string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(oldName);
        RequireValidName(newName, nameof(newName));
        string name = newName.Trim();
        lock (gate)
        {
            ThemeLibrarySnapshot current = snapshot;
            ThemeLibraryEntry entry = current.Find(oldName) ?? throw new ArgumentException($"There is no theme named \"{oldName}\".", nameof(oldName));
            if (current.Find(name) is { } other && !ReferenceEquals(other, entry))
            {
                throw new ArgumentException($"A theme named \"{name}\" already exists.", nameof(newName));
            }

            ThemeDefinition renamed = ThemeNormalizer.Normalize(entry.Theme, name);
            ThemeFile.Write(entry.FilePath, renamed);
            string path = MoveToNaturalPath(current, entry.FilePath, name);
            snapshot = current.Replace(entry, new ThemeLibraryEntry(renamed, path));
        }

        OnChanged();
    }

    /// <inheritdoc />
    public bool IsChangedFromOriginal(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return ShippedThemes.Find(theme.Name) is { } original && !ThemeValues.Equal(original, theme);
    }

    /// <inheritdoc />
    public IReadOnlyList<ThemeDefinition> GetMissingOrChangedShipped()
    {
        ThemeLibrarySnapshot current = snapshot;
        return [.. ShippedThemes.All.Where(original => current.Find(original.Name) is not { } entry || !ThemeValues.Equal(original, entry.Theme))];
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">A name is not a shipped theme (nothing is written then).</exception>
    /// <exception cref="IOException">A file could not be written.</exception>
    public void RestoreShipped(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        ThemeDefinition[] originals =
        [
            .. names.Distinct(ThemeNames.Comparer)
                .Select(name => ShippedThemes.Find(name) ?? throw new ArgumentException($"\"{name}\" is not a shipped theme.", nameof(names))),
        ];
        // A later file that cannot be written must not hide the themes already restored (review r1 #40): Changed is
        // raised whenever any theme was written, also when the write of another one throws.
        int written = 0;
        bool completed = false;
        try
        {
            lock (gate)
            {
                foreach (ThemeDefinition original in originals)
                {
                    SaveLocked(original);
                    written++;
                }
            }

            completed = true;
        }
        finally
        {
            if (completed || written > 0)
            {
                OnChanged();
            }
        }
    }

    private static void RequireValidName(string? name, string parameter)
    {
        ThemeNameCheck check = ThemeNames.Validate(name);
        if (check != ThemeNameCheck.Valid)
        {
            throw new ArgumentException($"The theme name is not valid ({check}).", parameter);
        }
    }

    private static string UniqueName(ThemeLibrarySnapshot current, string name)
    {
        for (int n = 2; ; n++)
        {
            string suffix = $" ({n})";
            string candidate = name[..Math.Min(name.Length, ThemeNames.MaxLength - suffix.Length)].TrimEnd() + suffix;
            if (current.Find(candidate) is null)
            {
                return candidate;
            }
        }
    }

    private void SaveLocked(ThemeDefinition theme)
    {
        ThemeLibrarySnapshot current = snapshot;
        ThemeLibraryEntry? existing = current.Find(theme.Name);
        string name = existing?.Theme.Name ?? theme.Name.Trim();
        ThemeDefinition normalized = ThemeNormalizer.Normalize(theme, name);
        string path = existing?.FilePath ?? ThemeFileNames.ChoosePath(paths.ThemesFolder, name, current.IsPathUsed);
        ThemeFile.Write(path, normalized);
        snapshot = current.Replace(existing, new ThemeLibraryEntry(normalized, path));
    }

    /// <summary>Renames a theme's file after its new name; a failed rename only leaves the old file name (the name inside the file counts).</summary>
    private string MoveToNaturalPath(ThemeLibrarySnapshot current, string path, string name)
    {
        if (ThemeFileNames.IsNaturalPathFor(path, name))
        {
            return path;
        }

        string target = ThemeFileNames.ChoosePath(paths.ThemesFolder, name, current.IsPathUsed);
        try
        {
            File.Move(path, target);
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn(LogSource, "A renamed theme keeps its old file name.", e);
            return path;
        }
    }

    /// <summary>Writes the shipped themes into a new folder, staged so that a failure leaves no half-seeded folder behind.</summary>
    private void Seed(string folder)
    {
        string staging = $"{folder}.seed-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(staging);
            foreach (ThemeDefinition original in ShippedThemes.All)
            {
                ThemeFile.Write(Path.Combine(staging, ThemeFileNames.StemFor(original.Name) + ThemeFileNames.Extension), original);
            }

            Directory.Move(staging, folder);
            log.Info(LogSource, $"Seeded the {ShippedThemes.All.Count} shipped themes.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Error(LogSource, "Couldn't create the themes folder.", e);
            DeleteStaging(staging);
        }
    }

    private void DeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn(LogSource, "Couldn't remove a staging folder of the shipped themes.", e);
        }
    }

    private ThemeLibrarySnapshot ReadFolder(string folder)
    {
        var entries = new List<ThemeLibraryEntry>();
        var problems = new List<ThemeFileProblem>();
        AtomicFile.DeleteLeftovers(folder, "*" + ThemeFileNames.Extension);
        foreach (string file in ListThemeFiles(folder))
        {
            ThemeReadResult read = ThemeFile.Read(file);
            if (read.Theme is not { } theme)
            {
                log.Warn(LogSource, $"Theme file {Path.GetFileName(file)} couldn't be read: {read.Problem}");
                problems.Add(new ThemeFileProblem(file, read.Problem ?? "the file is unreadable"));
            }
            else if (entries.Any(e => ThemeNames.Comparer.Equals(e.Theme.Name, theme.Name)))
            {
                problems.Add(new ThemeFileProblem(file, $"another theme file is already named \"{theme.Name}\""));
            }
            else
            {
                if (read.RemovedValues.Count > 0)
                {
                    log.Warn(LogSource, $"Theme file {Path.GetFileName(file)}: values that could not be read use their defaults.");
                }

                entries.Add(new ThemeLibraryEntry(theme, file));
            }
        }

        return new ThemeLibrarySnapshot(entries, problems);
    }

    private IEnumerable<string> ListThemeFiles(string folder)
    {
        try
        {
            return
            [
                .. Directory.EnumerateFiles(folder, "*" + ThemeFileNames.Extension)
                    .Where(f => string.Equals(Path.GetExtension(f), ThemeFileNames.Extension, StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase),
            ];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Error(LogSource, "Couldn't read the themes folder.", e);
            return [];
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
