using System.Globalization;

namespace HolidayLights.App.Settings;

/// <summary>
/// Adding songs and pictures (PRODUCT-SPEC 3.4.3, 3.5.6, 3.2.7): the files are copied into My Music or My Pictures as one
/// undoable step ("Undo" moves the copies to the holding folder) with the snackbar "Added 2 songs." [Show].
/// </summary>
public static class MediaImport
{
    /// <summary>Adds songs.</summary>
    /// <param name="services">The services.</param>
    /// <param name="host">The Settings window.</param>
    /// <param name="paths">The files.</param>
    /// <returns>One result per file, in order.</returns>
    public static IReadOnlyList<MediaImportResult> AddSongs(IAppServices services, ISettingsHost host, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Add(host, paths, "song", services.Songs.AddFiles, services.Songs.Remove, services.Songs.Restore, SettingsPageId.MusicBox);
    }

    /// <summary>Adds pictures.</summary>
    /// <param name="services">The services.</param>
    /// <param name="host">The Settings window.</param>
    /// <param name="paths">The files.</param>
    /// <returns>One result per file, in order.</returns>
    public static IReadOnlyList<MediaImportResult> AddPictures(IAppServices services, ISettingsHost host, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Add(host, paths, "picture", services.Pictures.AddFiles, services.Pictures.Remove, services.Pictures.Restore, SettingsPageId.ScreenSaver);
    }

    /// <summary>The ids of the files that were copied (added or renamed).</summary>
    /// <param name="results">The results.</param>
    /// <returns>The new ids.</returns>
    public static IReadOnlyList<string> AddedIds(IReadOnlyList<MediaImportResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return [.. results.Where(r => r.Outcome is MediaImportOutcome.Added or MediaImportOutcome.Renamed && r.Id is not null).Select(r => r.Id!)];
    }

    /// <summary>The InfoBar sentence for files that were not added, or null: "Couldn't copy "a.mp3": ..." / "Jingle Bells is already in your Music Box.".</summary>
    /// <param name="results">The results.</param>
    /// <param name="alreadyPresent">Formats the sentence of a file whose content is already present.</param>
    /// <returns>The sentence, or null.</returns>
    public static string? Problem(IReadOnlyList<MediaImportResult> results, Func<string, string> alreadyPresent)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(alreadyPresent);
        if (results.FirstOrDefault(r => r.Outcome == MediaImportOutcome.Failed) is { } failed)
        {
            return $"Couldn't copy \"{System.IO.Path.GetFileName(failed.SourcePath)}\": {failed.Error}";
        }

        return results.FirstOrDefault(r => r.Outcome == MediaImportOutcome.AlreadyPresent) is { } present
            ? alreadyPresent(System.IO.Path.GetFileNameWithoutExtension(present.SourcePath))
            : null;
    }

    private static IReadOnlyList<MediaImportResult> Add(
        ISettingsHost host,
        IReadOnlyList<string> paths,
        string singular,
        Func<IEnumerable<string>, IReadOnlyList<MediaImportResult>> add,
        Func<string, HeldItem?> remove,
        Action<HeldItem> restore,
        SettingsPageId page)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(paths);
        IReadOnlyList<MediaImportResult> results = add(paths);
        IReadOnlyList<string> ids = AddedIds(results);
        if (ids.Count == 0)
        {
            return results;
        }

        List<HeldItem> held = [];
        string text = DroppedFiles.Added(ids.Count, singular);
        host.History.Record(new UndoStep(
            ids.Count == 1 ? $"Add 1 {singular}" : string.Create(CultureInfo.CurrentCulture, $"Add {ids.Count} {singular}s"),
            () =>
            {
                held.Clear();
                foreach (string id in ids)
                {
                    if (remove(id) is { } item)
                    {
                        held.Add(item);
                    }
                }
            },
            () =>
            {
                foreach (HeldItem item in held)
                {
                    restore(item);
                }
            }));
        host.ShowSnackbar(text, "Show", () => host.Navigate(page));
        host.Announce(text);
        return results;
    }
}
