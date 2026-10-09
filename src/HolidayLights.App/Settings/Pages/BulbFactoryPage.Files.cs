using System.Globalization;
using HolidayLights.App.Controls;

namespace HolidayLights.App.Settings.Pages;

/// <summary>Adding bulb files and GIFs (PRODUCT-SPEC 3.2.10).</summary>
public partial class BulbFactoryPage
{
    /// <summary>
    /// Adds files in order: a new <c>.bul</c> is copied into My Bulbs, an equal one selects the bulb already present, a
    /// damaged one is refused; a GIF becomes a new bulb (Bulb Editing opens when it is the only file). Each added bulb is
    /// undoable ("Undo removes it"). Dropped on a box, the bulbs are then placed there. Other files go to the window
    /// (songs, pictures) or are refused.
    /// </summary>
    /// <param name="paths">The files.</param>
    /// <param name="destination">The box the files were dropped on, or null.</param>
    /// <param name="bulbsOnly">True for "Add Bulb..." and "Import Bulb Files from a Folder...": any other file is refused
    /// with the 5.4 message instead of going to the Music Box or the screen saver pictures.</param>
    /// <returns>A task that completes when the files were added.</returns>
    private async Task ImportAsync(IReadOnlyList<string> paths, BoxPosition? destination, bool bulbsOnly = false)
    {
        if (host is null)
        {
            return;
        }

        if (paths.Count == 0)
        {
            ShowMessage("That folder has no bulb files (.bul).", InfoBarSeverity.Informational);
            return;
        }

        string[] bulbFiles = [.. paths.Where(DroppedFiles.IsBulbFile)];
        string[] otherFiles = [.. paths.Where(p => !DroppedFiles.IsBulbFile(p))];
        if (otherFiles.Length > 0 && bulbsOnly)
        {
            ShowMessage(BulbImportTexts.UnsupportedText, InfoBarSeverity.Error);
        }
        else if (otherFiles.Length > 0)
        {
            host.AddFiles(otherFiles);
        }

        if (bulbFiles.Length == 0)
        {
            return;
        }

        var added = new List<string>();
        var gifs = new List<string>();
        var present = new List<string>();
        var problems = new List<string>();
        foreach (string path in bulbFiles)
        {
            if (DroppedFiles.IsGif(path))
            {
                // Writing the bulb waits for the add-on index on a first start: off the UI thread (bulb-factory's implementation is thread-safe).
                GifBulbCreation creation = await Task.Run(() => Services.BulbFactory.CreateBulbFromGif(path)).ConfigureAwait(true);
                if (creation is { Succeeded: true, BulbId: { } gifId })
                {
                    if (creation.FilePath is { } file)
                    {
                        Services.Bulbs.LoadUserBulb(file);
                    }

                    added.Add(gifId);
                    gifs.Add(gifId);
                }
                else
                {
                    problems.Add(creation.ErrorTitle is { } title ? $"{title}: {creation.ErrorText}" : BulbImportTexts.GifText);
                }

                continue;
            }

            BulbImportResult result = await Task.Run(() => Services.Bulbs.ImportFile(path)).ConfigureAwait(true);
            switch (result.Outcome)
            {
                case BulbImportOutcome.Added when result.BulbId is { } id:
                    added.Add(id);
                    break;
                case BulbImportOutcome.AlreadyPresent when result.BulbId is { } id:
                    present.Add(id);
                    break;
                case BulbImportOutcome.Damaged:
                    problems.Add(BulbImportTexts.DamagedText);
                    break;
                default:
                    problems.Add(BulbImportTexts.CopyFailed(path, result.Error));
                    break;
            }
        }

        // A file equal to a removed (hidden) bundled bulb brings that bulb back, in the same undo step (review r1 #68).
        string[] restored = [.. present.Where(id => Services.Settings.Current.Bulbs.Hidden.Contains(id, BulbIds.Comparer))];
        RecordAdded(added, destination, [.. added, .. present], restored);
        Report(bulbFiles.Length, added, gifs, present, problems, restored);
        if (bulbFiles.Length == 1 && gifs.Count == 1)
        {
            BulbEditingResult edited = await Services.BulbFactory.EditBulbAsync(host.Window, gifs[0]);
            if (edited.Saved)
            {
                List.Reload();
                List.Select(edited.BulbId);
            }
        }
    }

    /// <summary>Records the added bulbs (and their placement on a box) as one undo step: Undo moves the new files to the holding folder.</summary>
    private void RecordAdded(IReadOnlyList<string> added, BoxPosition? destination, IReadOnlyList<string> toPlace, IReadOnlyList<string> restored)
    {
        if (host is null || (added.Count == 0 && restored.Count == 0 && (destination is null || toPlace.Count == 0)))
        {
            return;
        }

        string description = (added.Count, restored.Count) switch
        {
            (0, 1) => $"Restore {NameOf(restored[0])}",
            (0, > 1) => string.Create(CultureInfo.CurrentCulture, $"Restore {restored.Count} bulbs"),
            (0, _) => "Add bulbs",
            (1, _) => $"Add {NameOf(added[0])}",
            _ => string.Create(CultureInfo.CurrentCulture, $"Add {added.Count} bulbs"),
        };
        using (host.History.BeginGroup(description))
        {
            foreach (string id in restored)
            {
                Services.Bulbs.Unhide(id);
            }

            foreach (string id in added)
            {
                HeldItem? held = null;
                host.History.Record(
                    new UndoStep(
                        $"Add {NameOf(id)}",
                        () => held = Services.Bulbs.RemoveUserBulb(id),
                        () =>
                        {
                            if (held is not null)
                            {
                                Services.Bulbs.RestoreUserBulb(held);
                            }
                        }));
            }

            if (destination is not null && toPlace.Count > 0)
            {
                Apply(destination.Side is { } side
                    ? ArrangementEdits.Insert(Arrangement, side, destination.Index, toPlace, NameOf)
                    : ArrangementEdits.SetCorner(Arrangement, destination.Corner, toPlace[0], NameOf));
            }
        }
    }

    /// <summary>Selects the bulbs and tells what happened: one file in its own words, several in one summary.</summary>
    private void Report(int files, IReadOnlyList<string> added, IReadOnlyList<string> gifs, IReadOnlyList<string> present, IReadOnlyList<string> problems, IReadOnlyList<string> restored)
    {
        if (host is null)
        {
            return;
        }

        if (gifs.Count > 1)
        {
            List.SelectMany(gifs, showMyBulbs: true);
        }
        else if (added.Count + present.Count > 0)
        {
            List.Reload();
            string first = added.Count > 0 ? added[0] : present[0];
            List.Select(first);
        }

        if (problems.Count > 0)
        {
            ShowMessage(
                problems.Count == 1 ? problems[0] : string.Create(CultureInfo.CurrentCulture, $"{problems.Count} files couldn't be added. {problems[0]}"),
                InfoBarSeverity.Error);
        }

        if (files == 1)
        {
            if (added.Count == 1 && gifs.Count == 0)
            {
                host.ShowSnackbar($"Added {NameOf(added[0])}.", "Undo", host.UndoLast);
                ShowMessage(BulbImportTexts.NewBulbTip, InfoBarSeverity.Informational);
            }
            else if (present.Count == 1 && restored.Count == 1)
            {
                host.ShowSnackbar($"Restored {NameOf(restored[0])}.", "Undo", host.UndoLast);
                host.Announce($"Restored {NameOf(restored[0])}.");
            }
            else if (present.Count == 1)
            {
                ShowMessage(BulbImportTexts.AlreadyPresent(NameOf(present[0])), InfoBarSeverity.Informational);
            }

            return;
        }

        if (BulbImportTexts.Summary(added.Count, present.Count) is { } summary)
        {
            IReadOnlyList<string> show = added;
            host.ShowSnackbar(summary, "Show", () => List.SelectMany(show, showMyBulbs: true));
            host.Announce(summary);
        }
        else if (present.Count > 0 && problems.Count == 0)
        {
            ShowMessage(
                present.Count == 1 ? BulbImportTexts.AlreadyPresent(NameOf(present[0])) : string.Create(CultureInfo.CurrentCulture, $"These {present.Count} bulbs are already in your bulbs."),
                InfoBarSeverity.Informational);
        }
    }
}
