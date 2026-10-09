using System.Globalization;
using System.IO;
using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// Bulb Editing (PRODUCT-SPEC 3.3.1): the "Bulb Information" and "Animation Frames" groups, "Save" (a 5.4-compatible file
/// replacing the bulb's file, one Undo step in Settings) and "Cancel" (asks "Discard Changes?" when something changed).
/// </summary>
/// <remarks>UI thread.</remarks>
internal sealed class BulbEditingViewModel : ObservableObject
{
    private readonly BulbEditorSession session;
    private readonly IBulbEditingPrompts prompts;
    private bool isSaving;
    private string? errorMessage;

    /// <summary>Creates the editor for an opened bulb.</summary>
    /// <param name="session">The opened bulb file.</param>
    /// <param name="knownCategories">Every known category (the check list).</param>
    /// <param name="prompts">The window's dialogs.</param>
    public BulbEditingViewModel(BulbEditorSession session, IEnumerable<string> knownCategories, IBulbEditingPrompts prompts)
    {
        this.session = session;
        this.prompts = prompts;
        Information = new BulbInformationViewModel(session.Original, knownCategories, prompts);
        Frames = new AnimationFramesViewModel(session.Original, session.BulbId, session.Catalog, prompts);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !isSaving && ValidationMessage is null);
        Result = new BulbEditingResult(false, session.BulbId, false);
        Information.Changed += (_, _) => OnContentChanged();
        Frames.Changed += (_, _) => OnContentChanged();
    }

    /// <summary>Raised when the editor should close (after a successful save).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>"Bulb Editing - Star" (the name as typed; the opened name while it is empty).</summary>
    public string Title => $"{BulbFactoryStrings.EditingCaption} - {(string.IsNullOrWhiteSpace(Information.Name) ? OriginalName : Information.Name.Trim())}";

    /// <summary>The bulb's name when it was opened.</summary>
    public string OriginalName => session.Original.Name;

    /// <summary>"Bulb Information".</summary>
    public BulbInformationViewModel Information { get; }

    /// <summary>"Animation Frames".</summary>
    public AnimationFramesViewModel Frames { get; }

    /// <summary>Why "Save" is not possible now (no name, a missing animation, too many categories), or null.</summary>
    public string? ValidationMessage
    {
        get
        {
            if (Information.NameError is { } nameError)
            {
                return nameError;
            }

            if (Frames.Document.MissingSlots.Select(s => SlotChoice.For(s).Name).FirstOrDefault() is { } missing)
            {
                return string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.MissingAnimationFormat, missing);
            }

            return Information.CategoryError;
        }
    }

    /// <summary>Why the last save failed, or null.</summary>
    public string? ErrorMessage
    {
        get => errorMessage;
        set => SetProperty(ref errorMessage, value);
    }

    /// <summary>True while the file is being written.</summary>
    public bool IsSaving
    {
        get => isSaving;
        private set
        {
            if (SetProperty(ref isSaving, value))
            {
                SaveCommand.Refresh();
            }
        }
    }

    /// <summary>"Save" (Ctrl+S).</summary>
    public AsyncRelayCommand SaveCommand { get; }

    /// <summary>True when saving now would change the bulb.</summary>
    public bool HasUnsavedChanges => !BuildDocument().HasSameContent(session.Original);

    /// <summary>The outcome for <see cref="IBulbFactoryDialogs.EditBulbAsync"/>.</summary>
    public BulbEditingResult Result { get; private set; }

    /// <summary>The bulb as it would be saved now.</summary>
    /// <returns>A new document.</returns>
    public BulbDocument BuildDocument()
    {
        BulbDocument document = Frames.Document.Clone();
        Information.ApplyTo(document);
        return document;
    }

    /// <summary>"Cancel", Esc or the X button: closes at once without changes, else asks "Discard Changes?".</summary>
    /// <returns>True when the editor may close.</returns>
    public Task<bool> ConfirmCloseAsync() =>
        isSaving ? Task.FromResult(false) : HasUnsavedChanges ? prompts.ConfirmDiscardAsync(OriginalName) : Task.FromResult(true);

    private async Task SaveAsync()
    {
        ErrorMessage = null;
        IsSaving = true;
        try
        {
            bool replaced = await session.SaveAsync(BuildDocument());
            Result = new BulbEditingResult(true, session.BulbId, replaced);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.SaveFailedFormat, Path.GetFileName(session.FilePath), e.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void OnContentChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ValidationMessage));
        SaveCommand.Refresh();
    }
}
