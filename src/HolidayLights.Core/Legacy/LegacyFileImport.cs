using HolidayLights.Core.Settings;

namespace HolidayLights.Core.Legacy;

/// <summary>
/// The 5.4 files in 6.0 (PRODUCT-SPEC 6.8.2): each add-on bulb maps to the installed bulb with the same content or is
/// copied into My Bulbs; songs and pictures that are not bundled are copied into My Music and My Pictures. Either predicted
/// (<see cref="Predict"/>, for the preview: nothing changes) or done (<see cref="Run"/>).
/// </summary>
internal sealed class LegacyFileImport
{
    private readonly Dictionary<string, string?> addOnIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> songIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> pictureIds = new(StringComparer.OrdinalIgnoreCase);

    private LegacyFileImport()
    {
    }

    /// <summary>One report line per file, in folder order (bulbs, songs, pictures).</summary>
    public List<ImportReportItem> Report { get; } = [];

    /// <summary>Bulb files now available in 6.0.</summary>
    public int Bulbs { get; private set; }

    /// <summary>Songs that were not bundled and are now in My Music.</summary>
    public int Songs { get; private set; }

    /// <summary>Pictures that were not bundled and are now in My Pictures.</summary>
    public int Pictures { get; private set; }

    /// <summary>
    /// The categories a 5.4 user gave to add-on bulbs that map to an installed bulb (5.4's Edit Categories rewrote the
    /// file's <c>categ:</c> record, which the content identity ignores), by bulb id: they become category overrides.
    /// </summary>
    public Dictionary<string, IReadOnlyList<string>> CategoryOverrides { get; } = new(BulbIds.Comparer);

    /// <summary>Predicts the ids without copying: a file that would be copied gets the My Bulbs/My Music/My Pictures id of its name.</summary>
    /// <param name="analysis">The analysis.</param>
    /// <returns>The prediction.</returns>
    public static LegacyFileImport Predict(LegacyAnalysis analysis)
    {
        var import = new LegacyFileImport();
        foreach (LegacyAddOn addOn in analysis.AddOns.Loaded)
        {
            import.addOnIds[addOn.FilePath] = analysis.ContentMatches.GetValueOrDefault(addOn.FilePath)
                ?? BulbIds.User(Path.GetFileNameWithoutExtension(addOn.FilePath));
        }

        foreach (LegacyMediaFile song in analysis.Songs)
        {
            import.songIds[song.FilePath] = song.BundledId ?? MediaIds.User(song.FileName);
        }

        foreach (LegacyMediaFile picture in analysis.Pictures)
        {
            import.pictureIds[picture.FilePath] = picture.BundledId ?? (picture.TargetPath is { } target ? MediaIds.User(Path.GetFileName(target)) : null);
        }

        return import;
    }

    /// <summary>Copies the files that are not installed yet.</summary>
    /// <param name="analysis">The analysis.</param>
    /// <param name="bulbs">Copies bulb files into My Bulbs.</param>
    /// <param name="songs">Copies songs into My Music.</param>
    /// <param name="pictures">Copies pictures into My Pictures.</param>
    /// <returns>The outcome.</returns>
    public static LegacyFileImport Run(LegacyAnalysis analysis, IBulbCatalog bulbs, ISongLibrary songs, IPictureLibrary pictures)
    {
        var import = new LegacyFileImport();
        import.ImportBulbs(analysis, bulbs);
        import.ImportSongs(analysis, songs);
        import.ImportPictures(analysis, pictures);
        return import;
    }

    /// <summary>The bulb id of a loaded add-on file, or null when it could not be imported.</summary>
    /// <param name="addOn">The file.</param>
    /// <returns>The id.</returns>
    public string? AddOnId(LegacyAddOn addOn) => addOnIds.GetValueOrDefault(addOn.FilePath);

    /// <summary>The song id of a file of "Holiday Lights Music", or null.</summary>
    /// <param name="song">The file.</param>
    /// <returns>The id.</returns>
    public string? SongId(LegacyMediaFile song) => songIds.GetValueOrDefault(song.FilePath);

    /// <summary>The picture id of a file of "Holiday Lights Pictures", or null.</summary>
    /// <param name="picture">The file.</param>
    /// <returns>The id.</returns>
    public string? PictureId(LegacyMediaFile picture) => pictureIds.GetValueOrDefault(picture.FilePath);

    private static string Item(string folder, string path) => $@"{folder}\{Path.GetFileName(path)}";

    private void ImportBulbs(LegacyAnalysis analysis, IBulbCatalog bulbs)
    {
        IEnumerable<(string FilePath, LegacyAddOn? AddOn)> files = analysis.AddOns.Loaded.Select(a => (FilePath: a.FilePath, AddOn: (LegacyAddOn?)a))
            .Concat(analysis.AddOns.Damaged.Select(path => (FilePath: path, AddOn: (LegacyAddOn?)null)))
            .OrderBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase);
        foreach ((string path, LegacyAddOn? addOn) in files)
        {
            string item = Item(LegacyFolders.Bulbs, path);
            if (addOn is null)
            {
                Report.Add(new ImportReportItem { Item = item, Status = ImportItemStatus.NotImported, Reason = "the file is damaged" });
            }
            else if (analysis.ContentMatches.TryGetValue(path, out string? existing))
            {
                Record(addOnIds, path, existing, item, ImportItemStatus.AlreadyIncluded, null);
                KeepCategories(addOn, existing, bulbs, item);
                Bulbs++;
            }
            else
            {
                BulbImportResult result = Attempt(() => bulbs.ImportFile(path), e => new BulbImportResult(path, BulbImportOutcome.Failed, null, e.Message));
                (ImportItemStatus status, string? reason) = result.Outcome switch
                {
                    BulbImportOutcome.Added => (ImportItemStatus.Imported, (string?)null),
                    BulbImportOutcome.AlreadyPresent => (ImportItemStatus.AlreadyIncluded, null),
                    BulbImportOutcome.Damaged => (ImportItemStatus.NotImported, "the file is damaged"),
                    _ => (ImportItemStatus.NotImported, CopyFailed(result.Error)),
                };
                Record(addOnIds, path, result.BulbId, item, status, reason);
                if (result.Outcome == BulbImportOutcome.AlreadyPresent)
                {
                    KeepCategories(addOn, result.BulbId, bulbs, item);
                }

                Bulbs += status == ImportItemStatus.NotImported ? 0 : 1;
            }
        }
    }

    /// <summary>
    /// A 5.4 file that maps to an installed bulb keeps the categories 5.4 showed for it when they differ from that bulb's
    /// own (case-insensitive set comparison): they become a category override of the bulb, reported on a line of their
    /// own. A file without categories changes nothing (an old download without a <c>categ:</c> record is not a choice).
    /// </summary>
    private void KeepCategories(LegacyAddOn addOn, string? id, IBulbCatalog bulbs, string item)
    {
        if (id is null || addOn.Categories is not { Count: > 0 } categories || !bulbs.TryGetInfo(id, out BulbInfo? info))
        {
            return;
        }

        IReadOnlyList<string> legacy = SettingsSanitizer.CleanCategories(categories);
        if (legacy.Count == 0 || legacy.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(info.OriginalCategories))
        {
            return;
        }

        CategoryOverrides[id] = legacy;
        Report.Add(new ImportReportItem { Item = $"{item} (categories)", Status = ImportItemStatus.Imported });
    }

    private void ImportSongs(LegacyAnalysis analysis, ISongLibrary songs)
    {
        foreach (LegacyMediaFile song in analysis.Songs)
        {
            string item = Item(LegacyFolders.Music, song.FilePath);
            if (song.BundledId is { } bundled)
            {
                Record(songIds, song.FilePath, bundled, item, ImportItemStatus.AlreadyIncluded, null);
                continue;
            }

            MediaImportResult result = AddFile(song.FilePath, path => songs.AddFiles([path]));
            (ImportItemStatus status, string? reason) = Status(result, "it isn't a song Holiday Lights can play");
            Record(songIds, song.FilePath, result.Id, item, status, reason);
            Songs += status == ImportItemStatus.NotImported ? 0 : 1;
        }
    }

    private void ImportPictures(LegacyAnalysis analysis, IPictureLibrary pictures)
    {
        foreach (LegacyMediaFile picture in analysis.Pictures)
        {
            string item = Item(LegacyFolders.Pictures, picture.FilePath);
            if (picture.BundledId is { } bundled)
            {
                Record(pictureIds, picture.FilePath, bundled, item, ImportItemStatus.AlreadyIncluded, null);
                continue;
            }

            if (picture.TargetPath is not { } target)
            {
                Record(pictureIds, picture.FilePath, null, item, ImportItemStatus.NotImported, "the shortcut's picture is missing");
                continue;
            }

            MediaImportResult result = AddFile(target, path => pictures.AddFiles([path]));
            (ImportItemStatus status, string? reason) = Status(result, "it isn't a picture Holiday Lights can show");
            Record(pictureIds, picture.FilePath, result.Id, item, status, reason);
            Pictures += status == ImportItemStatus.NotImported ? 0 : 1;
        }
    }

    private static MediaImportResult AddFile(string path, Func<string, IReadOnlyList<MediaImportResult>> add) =>
        Attempt(
            () => add(path) is [var result] ? result : new MediaImportResult(path, MediaImportOutcome.Failed, null, null),
            e => new MediaImportResult(path, MediaImportOutcome.Failed, null, e.Message));

    private static (ImportItemStatus Status, string? Reason) Status(MediaImportResult result, string unsupported) => result.Outcome switch
    {
        MediaImportOutcome.Added or MediaImportOutcome.Renamed => (ImportItemStatus.Imported, null),
        MediaImportOutcome.AlreadyPresent => (ImportItemStatus.AlreadyIncluded, null),
        MediaImportOutcome.Unsupported => (ImportItemStatus.NotImported, unsupported),
        _ => (ImportItemStatus.NotImported, CopyFailed(result.Error)),
    };

    private static string CopyFailed(string? error) => $"couldn't copy it: {error ?? "unknown reason"}";

    /// <summary>Runs a library call; an I/O error becomes a failed result instead of stopping the import.</summary>
    private static T Attempt<T>(Func<T> action, Func<Exception, T> failure)
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return failure(e);
        }
    }

    private void Record(Dictionary<string, string?> ids, string path, string? id, string item, ImportItemStatus status, string? reason)
    {
        ids[path] = status == ImportItemStatus.NotImported ? null : id;
        Report.Add(new ImportReportItem { Item = item, Status = status, Reason = reason });
    }
}
