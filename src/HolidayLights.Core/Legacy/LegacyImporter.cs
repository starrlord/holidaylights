using System.Runtime.CompilerServices;
using HolidayLights.Core.Settings;
using HolidayLights.Core.Themes;

namespace HolidayLights.Core.Legacy;

/// <summary>
/// The Holiday Lights 5.4 import (PRODUCT-SPEC 6.8; see <see cref="ILegacyImporter"/>): the factory-default rule, value
/// mapping, add-on id re-creation in NTFS name order, content matching of <c>.bul</c> files, theme import, files that are
/// not bundled, the report. Owner: core-settings.
/// </summary>
/// <remarks>
/// <para><b>Starting values.</b> <see cref="Import"/> keeps everything of <c>current</c> that 5.4 had no say in. On a
/// first run, pass the newcomer settings (<see cref="NewcomerSettings.Create"/>): a factory-default import keeps their look
/// (today's Automatic theme, music off, start with Windows on) and only adds the 5.4 values to Recent Settings as
/// "Holiday Lights 5.4 Settings" (the values of "Use My 2003 Lights"), while a customized import replaces the 13 values
/// with the 5.4 ones, turns Automatic themes off, turns "Play Holiday Music" on unless 5.4 played "Never", and takes Bulb
/// Drawing and the startup state from 5.4. Both import the hot key, the custom colours, the category overrides, the
/// themes and the files.</para>
/// <para><b>Preconditions.</b> The bulb catalog, the song library and the picture library must have loaded (bundled
/// content decides what is "already included"), and the theme library must be loaded.</para>
/// <para>Nothing under the 5.4 key, its folders or its files is changed.</para>
/// </remarks>
public sealed class LegacyImporter : ILegacyImporter
{
    /// <summary>The Recent Settings label of the 5.4 current settings (PRODUCT-SPEC 2.5.2, 3.6.6).</summary>
    public const string RecentSettingsLabel = "Holiday Lights 5.4 Settings";

    /// <summary>The Recent Settings label of the values "Import Again..." replaces.</summary>
    public const string BeforeImportLabel = "Before Import";

    private const string LogSource = "Legacy";

    private readonly ILegacyRegistrySource registry;
    private readonly IBulbCatalog bulbs;
    private readonly ISongLibrary songs;
    private readonly IPictureLibrary pictures;
    private readonly IThemeLibrary themes;
    private readonly IAppLog log;
    private readonly LegacyAnalyzer analyzer;
    private readonly TimeProvider clock;
    private readonly ConditionalWeakTable<LegacyImportPreview, LegacyAnalysis> analyses = new();

    /// <summary>Creates the importer.</summary>
    /// <param name="registry">Reads the 5.4 registry (read-only).</param>
    /// <param name="bulbs">Content matching and copying of 5.4 add-on bulbs.</param>
    /// <param name="songs">Copying songs that are not bundled.</param>
    /// <param name="pictures">Copying pictures that are not bundled.</param>
    /// <param name="themes">Saving imported themes.</param>
    /// <param name="themeService">The theme service of the composition (themes are compared with the same rules it uses).</param>
    /// <param name="shell">Resolving <c>.lnk</c> files of the 5.4 folders.</param>
    /// <param name="log">The log.</param>
    public LegacyImporter(
        ILegacyRegistrySource registry,
        IBulbCatalog bulbs,
        ISongLibrary songs,
        IPictureLibrary pictures,
        IThemeLibrary themes,
        IThemeService themeService,
        IShellOperations shell,
        IAppLog log)
        : this(registry, bulbs, songs, pictures, themes, themeService, shell, log, new BulFileHeaderReader(), TimeProvider.System)
    {
    }

    /// <summary>Creates the importer with its file and clock dependencies (tests).</summary>
    /// <param name="registry">Reads the 5.4 registry.</param>
    /// <param name="bulbs">The bulb catalog.</param>
    /// <param name="songs">The song library.</param>
    /// <param name="pictures">The picture library.</param>
    /// <param name="themes">The theme library.</param>
    /// <param name="themeService">The theme service.</param>
    /// <param name="shell">Resolves shortcuts.</param>
    /// <param name="log">The log.</param>
    /// <param name="headers">Reads <c>.bul</c> headers.</param>
    /// <param name="clock">The local date of the import record.</param>
    internal LegacyImporter(
        ILegacyRegistrySource registry,
        IBulbCatalog bulbs,
        ISongLibrary songs,
        IPictureLibrary pictures,
        IThemeLibrary themes,
        IThemeService themeService,
        IShellOperations shell,
        IAppLog log,
        ILegacyBulbHeaderReader headers,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(bulbs);
        ArgumentNullException.ThrowIfNull(songs);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(log);
        this.registry = registry;
        this.bulbs = bulbs;
        this.songs = songs;
        this.pictures = pictures;
        this.themes = themes;
        this.log = log;
        this.clock = clock;
        analyzer = new LegacyAnalyzer(bulbs, songs, pictures, shell, headers);
    }

    /// <inheritdoc />
    public bool IsLegacyInstallPresent() => registry.Read() is not null;

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="LegacyImportPreview.LegacyValues"/> names a 5.4 add-on bulb, song or picture that would be copied by its
    /// My Bulbs, My Music or My Pictures id; the import confirms the ids (they differ only when a different file of the
    /// same name is already there).
    /// </remarks>
    public LegacyImportPreview? Analyze()
    {
        if (registry.Read() is not { } snapshot)
        {
            return null;
        }

        LegacyAnalysis analysis = analyzer.Analyze(snapshot);
        var mapper = new LegacySettingsMapper(LegacyResolvers.Create(analysis, LegacyFileImport.Predict(analysis), songs, pictures));
        var preview = new LegacyImportPreview
        {
            IsFactoryDefault = analysis.IsFactoryDefault,
            LegacyFolder = snapshot.ProgramFolder,
            LegacyValues = mapper.MapCurrent(analysis.Main, new LegacyMappingIssues()),
            ThemeCount = snapshot.Themes.Count,
            BulbFileCount = analysis.AddOns.Loaded.Count + analysis.AddOns.Damaged.Count,
            SongFileCount = analysis.Songs.Count(s => s.BundledId is null),
            PictureFileCount = analysis.Pictures.Count(p => p.BundledId is null),
        };
        analyses.AddOrUpdate(preview, analysis);
        log.Info(LogSource, $"Holiday Lights 5.4 found ({(analysis.IsFactoryDefault ? "factory defaults" : "customized")}, {preview.ThemeCount} themes, "
            + $"{preview.BulbFileCount} bulb files, {preview.SongFileCount} songs and {preview.PictureFileCount} pictures to import).");
        return preview;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The preview is from another importer and 5.4 is gone meanwhile.</exception>
    public LegacyImportResult Import(LegacyImportPreview preview, AppSettings current, LegacyImportMode mode)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(current);
        LegacyAnalysis analysis = analyses.TryGetValue(preview, out LegacyAnalysis? cached)
            ? cached
            : analyzer.Analyze(registry.Read() ?? throw new InvalidOperationException("Holiday Lights 5.4 is no longer installed."));

        LegacyFileImport files = LegacyFileImport.Run(analysis, bulbs, songs, pictures);
        var mapper = new LegacySettingsMapper(LegacyResolvers.Create(analysis, files, songs, pictures));
        var report = new LegacyImportReport();
        var issues = new LegacyMappingIssues();
        ThemeableSettings legacyValues = mapper.MapCurrent(analysis.Main, issues);
        HotKeyBinding hotKey = LegacySettingsMapper.MapLocationHotKey(analysis.Main, out bool hotKeyImported);
        report.AddMainValues(analysis.Main, issues, hotKeyImported);
        IReadOnlyDictionary<string, IReadOnlyList<string>> overrides = ImportCategories(analysis.Snapshot, current, files.CategoryOverrides, report);
        int themeCount = ImportThemes(analysis.Snapshot, mapper, mode, report);
        report.Items.AddRange(files.Report);
        AddStartupItems(analysis.Snapshot, mode, report);

        AppSettings imported = current with
        {
            HotKeys = current.HotKeys with { Location = hotKey },
            Colors = new ColorSettings { Custom = LegacySettingsMapper.MapCustomColors(analysis.Main) },
            Bulbs = current.Bulbs with { CategoryOverrides = overrides },
        };
        AppSettings settings = ApplyLook(imported, current, analysis, legacyValues, mode);
        var record = new Import54Record
        {
            Date = DateOnly.FromDateTime(clock.GetLocalNow().DateTime),
            FactoryDefaults = analysis.IsFactoryDefault,
            Themes = themeCount,
            Bulbs = files.Bulbs,
            Songs = files.Songs,
            Pictures = files.Pictures,
            Report = report.Items,
        };
        log.Info(LogSource, $"Imported Holiday Lights 5.4 ({mode}): {themeCount} themes, {files.Bulbs} bulbs, {files.Songs} songs, {files.Pictures} pictures.");
        return new LegacyImportResult(settings with { Import54 = record }, record);
    }

    /// <summary>Which values apply (PRODUCT-SPEC 6.8.4): the newcomer look for factory defaults, the 5.4 values otherwise.</summary>
    private AppSettings ApplyLook(AppSettings imported, AppSettings current, LegacyAnalysis analysis, ThemeableSettings legacyValues, LegacyImportMode mode)
    {
        DateTimeOffset now = clock.GetLocalNow();
        if (mode == LegacyImportMode.FirstRun && analysis.IsFactoryDefault)
        {
            return imported with { RecentSettings = RecentSettingsHistory.Record(current.RecentSettings, RecentSettingsLabel, now, legacyValues) };
        }

        AppSettings applied = imported with
        {
            Current = legacyValues,
            Lights = current.Lights with { Drawing = LegacySettingsMapper.MapDrawing(analysis.Main), BehindIcons = true },
            Music = current.Music with { Enabled = legacyValues.Music.Mode != PlayMode.Never },
            Calendar = current.Calendar with { Enabled = false, ActiveEntryId = null },
        };
        return mode == LegacyImportMode.FirstRun
            ? applied with
            {
                Startup = new StartupSettings { Auto = analysis.Snapshot.StartupShortcuts.Count > 0 },
                RecentSettings = RecentSettingsHistory.Record(current.RecentSettings, RecentSettingsLabel, now, legacyValues),
            }
            : applied with { RecentSettings = RecentSettingsHistory.Record(current.RecentSettings, BeforeImportLabel, now, current.Current) };
    }

    /// <summary>
    /// "Included Bulb Categories" (built-in bulbs) and the categories of add-on files that map to installed bulbs
    /// (<see cref="LegacyFileImport.CategoryOverrides"/>): 5.4's overrides replace 6.0's for the same bulbs.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ImportCategories(
        LegacyRegistrySnapshot snapshot, AppSettings current, IReadOnlyDictionary<string, IReadOnlyList<string>> fileCategories, LegacyImportReport report)
    {
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IReadOnlyList<string>> imported = LegacySettingsMapper.MapCategoryOverrides(snapshot.IncludedBulbCategories, unknown);
        foreach (string name in snapshot.IncludedBulbCategories.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            report.Add($@"Included Bulb Categories\{name}", unknown.Contains(name) ? ImportItemStatus.NotImported : ImportItemStatus.Imported,
                unknown.Contains(name) ? "no built-in bulb has this name" : null);
        }

        var merged = new Dictionary<string, IReadOnlyList<string>>(current.Bulbs.CategoryOverrides, BulbIds.Comparer);
        foreach ((string id, IReadOnlyList<string> categories) in imported.Concat(fileCategories))
        {
            merged[id] = categories;
        }

        return merged;
    }

    /// <summary>
    /// Saves the 5.4 themes under their own names (PRODUCT-SPEC 6.8.2): equal to the theme of that name -> "Already
    /// included"; on a first run a different same-named theme (a seeded one) is replaced; "Import Again" keeps existing
    /// themes and adds the missing ones.
    /// </summary>
    /// <returns>The number of 5.4 themes now in 6.0.</returns>
    private int ImportThemes(LegacyRegistrySnapshot snapshot, LegacySettingsMapper mapper, LegacyImportMode mode, LegacyImportReport report)
    {
        int count = 0;
        var names = new HashSet<string>(ThemeNames.Comparer);
        foreach ((string originalName, IReadOnlyDictionary<string, LegacyValue> values) in snapshot.Themes)
        {
            string item = $@"Themes\{originalName}";
            string name = UniqueName(names, ThemeNames.MakeValid(originalName));
            var issues = new LegacyMappingIssues();
            ThemeDefinition theme = ThemeNormalizer.Normalize(mapper.MapTheme(name, new LegacyValueSet(values), issues), name);
            ThemeDefinition? existing = themes.Find(name);
            if (existing is not null && ThemeValues.Equal(existing, theme))
            {
                report.Add(item, ImportItemStatus.AlreadyIncluded);
                count++;
            }
            else if (existing is not null && mode == LegacyImportMode.Again)
            {
                report.Add(item, ImportItemStatus.NotImported, "you already have a theme with this name");
            }
            else if (TrySave(theme, out string? error))
            {
                report.Add(item, ImportItemStatus.Imported);
                count++;
            }
            else
            {
                report.Add(item, ImportItemStatus.NotImported, $"couldn't save it: {error}");
            }

            report.AddUnresolvedBulbs(item, issues);
        }

        return count;
    }

    private static string UniqueName(HashSet<string> taken, string name)
    {
        string candidate = name;
        for (int n = 2; !taken.Add(candidate); n++)
        {
            string suffix = $" ({n})";
            candidate = name[..Math.Min(name.Length, ThemeNames.MaxLength - suffix.Length)].TrimEnd() + suffix;
        }

        return candidate;
    }

    private static void AddStartupItems(LegacyRegistrySnapshot snapshot, LegacyImportMode mode, LegacyImportReport report)
    {
        foreach ((string shortcut, _) in snapshot.StartupShortcuts)
        {
            report.Add($@"Startup\{Path.GetFileName(shortcut)}", mode == LegacyImportMode.FirstRun ? ImportItemStatus.Imported : ImportItemStatus.AlreadyIncluded);
        }
    }

    private bool TrySave(ThemeDefinition theme, out string? error)
    {
        try
        {
            themes.Save(theme);
            error = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn(LogSource, "A 5.4 theme could not be saved.", e);
            error = e.Message;
            return false;
        }
    }
}
