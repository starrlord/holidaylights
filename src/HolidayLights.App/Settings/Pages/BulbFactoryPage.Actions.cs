using System.Globalization;
using HolidayLights.App.Controls;
using HolidayLights.App.Gallery;
using HolidayLights.App.Settings.Undo;

namespace HolidayLights.App.Settings.Pages;

/// <summary>Using, adding and removing bulbs (PRODUCT-SPEC 3.2.4, 3.2.6, 3.2.11).</summary>
public partial class BulbFactoryPage
{
    /// <summary>
    /// Applies an arrangement edit: one undo step with its Undo text, the next target, the UIA notification, the snackbar
    /// of the big changes and the InfoBar of a partial change.
    /// </summary>
    /// <param name="edit">The edit.</param>
    private void Apply(ArrangementEdit edit)
    {
        SlotAssignment before = Arrangement;
        if (!Equals(edit.Result, before))
        {
            Services.Settings.Update(s => s with { Current = s.Current with { Arrangement = edit.Result } }, SettingsChange.Edit(edit.UndoText));
            ShowAutomaticHintOnce();
        }

        if (edit.NextTarget is { } next)
        {
            Editor.Target = next.Normalize(edit.Result);
        }

        host?.Announce(edit.Announcement);
        if (edit.Snackbar is { } snackbar && host is not null)
        {
            host.ShowSnackbar(snackbar, "Undo", host.UndoLast);
        }

        if (edit.Problem is { } problem)
        {
            ShowMessage(problem, InfoBarSeverity.Warning);
        }
    }

    /// <summary>
    /// "Automatic themes are on: on Nov 1 your lights change to Thanksgiving." the first time the arrangement is changed
    /// by hand while Automatic themes are on (PRODUCT-SPEC 5.11).
    /// </summary>
    private void ShowAutomaticHintOnce()
    {
        AppSettings settings = Services.Settings.Current;
        if (!settings.Calendar.Enabled || settings.Onboarding.AutomaticEditHintShown)
        {
            return;
        }

        Services.Settings.Update(s => s with { Onboarding = s.Onboarding with { AutomaticEditHintShown = true } }, SettingsChange.Internal);
        if (Services.Calendar.GetNextChange(settings.Calendar, DateOnly.FromDateTime(DateTime.Now)) is not { } change)
        {
            return;
        }

        AutomaticBar.Message = $"Automatic themes are on: on {LightsStatusText.ShortDate(change.Date)} your lights change to {change.Resolution.ThemeName}.";
        AutomaticBar.IsOpen = true;
    }

    /// <summary>Shows what using a bulb for the target would do ("Trying On: Candy Canes - the whole frame"), or the real arrangement.</summary>
    private void ShowTryOn(string? bulbId)
    {
        if (bulbId is null)
        {
            Editor.ShowTryOn(null, null);
            return;
        }

        SlotAssignment arrangement = Arrangement;
        ArrangementTarget target = Editor.Target;
        ArrangementEdit edit = ArrangementEdits.Use(arrangement, target, bulbId, NameOf);
        Editor.ShowTryOn(edit.Result, $"Trying On: {NameOf(bulbId)} - {target.Sentence(arrangement)}");
    }

    /// <summary>Ctrl+V on a box: an edge appends the selected bulbs, a corner takes the first.</summary>
    private void Paste(BoxPosition box)
    {
        IReadOnlyList<BulbItem> selection = List.SelectedItems;
        if (selection.Count == 0)
        {
            host?.Announce("Select bulbs in the Bulb List first.");
            return;
        }

        string[] ids = [.. selection.Select(i => i.Id)];
        Apply(box.Side is { } side
            ? ArrangementEdits.AddTo(Arrangement, side, ids, NameOf)
            : ArrangementEdits.SetCorner(Arrangement, box.Corner, ids[0], NameOf));
    }

    /// <summary>The chip menu's bulb commands.</summary>
    private void OnBulbCommand(BulbCommandRequest request)
    {
        switch (request.Command)
        {
            case BulbCommand.Edit:
                _ = EditAsync(request.BulbId);
                break;
            case BulbCommand.Credits:
                if (host is not null)
                {
                    Services.BulbFactory.ShowCredits(host.Window, request.BulbId);
                }

                break;
            case BulbCommand.FindInList:
                if (List.Select(request.BulbId))
                {
                    List.FocusList();
                }

                break;
        }
    }

    /// <summary>Carries out a command of the Bulb List, its menu or the selected-bulb bar.</summary>
    /// <param name="request">The command and its bulbs.</param>
    internal void Run(BulbActionRequest request)
    {
        if (request.BulbIds.Count == 0)
        {
            return;
        }

        SlotAssignment arrangement = Arrangement;
        IReadOnlyList<string> ids = request.BulbIds;
        string first = ids[0];
        switch (request.Action)
        {
            case BulbAction.Use:
                ArrangementTarget target = Editor.Target;
                Apply(ArrangementEdits.Use(arrangement, target, first, NameOf));
                if (!target.IsQuick)
                {
                    Editor.Settle(Editor.Target);
                }

                break;
            case BulbAction.AddToEdge:
                Apply(ArrangementEdits.AddTo(arrangement, request.Side, ids, NameOf));
                break;
            case BulbAction.AddToEveryEdge:
                Apply(ArrangementEdits.AddToEveryEdge(arrangement, ids, NameOf));
                break;
            case BulbAction.UseInAllCorners:
                Apply(ArrangementEdits.Use(arrangement, ArrangementTarget.AllCorners, first, NameOf));
                break;
            case BulbAction.UseEverywhere:
                Apply(ArrangementEdits.Use(arrangement, ArrangementTarget.WholeFrame, first, NameOf));
                break;
            case BulbAction.UseInCorner:
                Apply(ArrangementEdits.SetCorner(arrangement, request.Corner, first, NameOf));
                break;
            case BulbAction.Edit:
                _ = EditAsync(first);
                break;
            case BulbAction.Credits:
                if (host is not null)
                {
                    Services.BulbFactory.ShowCredits(host.Window, first);
                }

                break;
            case BulbAction.Export:
                _ = ExportAsync(first);
                break;
            case BulbAction.ShowInFolder:
                if (Services.Bulbs.TryGetInfo(first, out BulbInfo? info) && info.FilePath is { } path)
                {
                    Services.Shell.ShowInFolder(path);
                }

                break;
            case BulbAction.UseAsSaverAnimation:
                UseAsSaverAnimation(first);
                break;
            case BulbAction.Remove:
                RemoveBulbs(ids);
                break;
        }
    }

    /// <summary>"Edit Bulb..." (Bulb Editing) or "Edit Categories..." (PRODUCT-SPEC 3.3).</summary>
    private async Task EditAsync(string bulbId)
    {
        if (host is null || !Services.Bulbs.TryGetInfo(bulbId, out BulbInfo? info))
        {
            return;
        }

        if (!info.IsEditable)
        {
            await Services.BulbFactory.EditCategoriesAsync(host.Window, bulbId);
            return;
        }

        BulbEditingResult result = await Services.BulbFactory.EditBulbAsync(host.Window, bulbId);
        if (!result.Saved)
        {
            return;
        }

        string name = NameOf(result.BulbId);
        string text = $"Saved {name}. To share it, right-click it and choose Export Bulb File…";
        if (result.CharactersReplaced)
        {
            text += " Some characters can't be saved in a bulb file and were replaced.";
        }

        ShowMessage(text, result.CharactersReplaced ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        List.Reload();
        List.Select(result.BulbId);
    }

    private async Task ExportAsync(string bulbId)
    {
        if (host is null)
        {
            return;
        }

        if (await Services.BulbFactory.ExportBulbFileAsync(host.Window, bulbId) is not null)
        {
            host.Announce($"Exported {NameOf(bulbId)}.");
        }
    }

    /// <summary>"Use as Screen Saver Animation": the screen saver animation becomes this bulb (one undo step).</summary>
    private void UseAsSaverAnimation(string bulbId)
    {
        string animation = SaverAnimations.ForBulb(bulbId);
        string name = NameOf(bulbId);
        Services.Settings.Update(
            // Keeps the user's movement Style, as the Screen Saver page's Choose a Bulb does (PRODUCT-SPEC 3.5.5).
            s => s with { Current = s.Current with { Saver = s.Current.Saver with { Animation = animation } } },
            SettingsChange.Edit($"Use {name} as the screen saver animation"));
        host?.Announce($"{name} is now the screen saver animation.");
    }

    /// <summary>
    /// "Remove Bulb" (PRODUCT-SPEC 3.2.11): your bulb goes to the holding folder, a bundled bulb is hidden; the
    /// arrangement drops it and is saved; themes keep their reference; one undo step; snackbar "Removed &lt;name&gt;." [Undo].
    /// </summary>
    private void RemoveBulbs(IReadOnlyList<string> ids)
    {
        string[] removable = [.. ids.Where(id => BulbIds.TryGetOrigin(id, out BulbOrigin origin) && origin != BulbOrigin.BuiltIn)];
        if (removable.Length == 0 || host is null)
        {
            return;
        }

        // Each removal stands on its own: a file that can't be moved (locked, read-only) is reported, and the arrangement
        // drops only the bulbs that really went, so nothing is recorded half-done (PRODUCT-SPEC 5.13).
        Dictionary<string, string> names = removable.Distinct().ToDictionary(id => id, NameOf);
        var removed = new List<string>();
        var failed = new List<string>();
        Exception? failure = null;
        using (host.History.BeginGroup(Description(removable)))
        {
            foreach (string id in removable)
            {
                try
                {
                    if (BulbIds.TryGetOrigin(id, out BulbOrigin origin) && origin == BulbOrigin.UserAddOn)
                    {
                        RemoveUserBulb(id);
                    }
                    else
                    {
                        Services.Bulbs.Hide(id);
                    }

                    removed.Add(id);
                }
                catch (Exception exception) when (FileProblems.Is(exception))
                {
                    failure ??= exception;
                    failed.Add(id);
                }
            }

            if (removed.Count > 0)
            {
                string description = Description(removed);
                host.History.DescribeGroup(description);
                SlotAssignment arrangement = removed.Aggregate(Arrangement, ArrangementEdits.Without);
                if (!Equals(arrangement, Arrangement))
                {
                    Services.Settings.Update(s => s with { Current = s.Current with { Arrangement = arrangement } }, SettingsChange.Edit(description));
                }
            }
        }

        if (removed.Count > 0)
        {
            string snackbar = removed.Count == 1
                ? $"Removed {names[removed[0]]}."
                : string.Create(CultureInfo.CurrentCulture, $"Removed {removed.Count} bulbs.");
            host.ShowSnackbar(snackbar, "Undo", host.UndoLast);
            host.Announce(snackbar);
        }

        if (failure is not null)
        {
            string what = failed.Count == 1
                ? $"Couldn't remove {names[failed[0]]}"
                : string.Create(CultureInfo.CurrentCulture, $"Couldn't remove {failed.Count} bulbs");
            Services.Log.Warn("Settings.BulbFactory", what + ".", failure);
            ShowMessage(FileProblems.Sentence(what, failure), InfoBarSeverity.Error);
        }

        string Description(IReadOnlyList<string> ids) => ids.Count == 1
            ? $"Remove {names[ids[0]]}"
            : string.Create(CultureInfo.CurrentCulture, $"Remove {ids.Count} bulbs");
    }

    /// <summary>Moves a My Bulbs file to the holding folder as an undoable step that Cancel also reverts.</summary>
    private void RemoveUserBulb(string id)
    {
        if (host is null)
        {
            return;
        }

        HeldItem held = Services.Bulbs.RemoveUserBulb(id);
        string name = NameOf(id);
        host.History.Record(
            new UndoStep(
                $"Remove {name}",
                () => Services.Bulbs.RestoreUserBulb(held),
                () => held = Services.Bulbs.RemoveUserBulb(id)),
            UndoCancelBehavior.RevertOnCancel);
    }
}
