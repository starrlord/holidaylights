using System.ComponentModel;
using System.Globalization;
using HolidayLights.Core.Bulbs.Writing;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// The "Animation Frames" group of Bulb Editing (PRODUCT-SPEC 3.3.1): the slot map and
/// slot combo, the flavor combo with its " *" marks, the 5.4 explanation texts, the preview (<see cref="Preview"/>) with the
/// white square of the list picture, "Change...", "Remove Flavor", "Copy to All Sides", the dialog's own Undo and Redo,
/// and the edge sample.
/// </summary>
/// <remarks>UI thread. Decoding happens on the thread pool; <see cref="WhenIdleAsync"/> completes when the views are up to date.</remarks>
internal sealed class AnimationFramesViewModel : ObservableObject
{
    private readonly IBulbEditingPrompts prompts;
    private readonly IBulbResolver catalog;
    private readonly string bulbId;
    private readonly Guid sessionKey = Guid.NewGuid();
    private readonly AnimationCache animations = new();
    private readonly EditorHistory history = new();

    private BulbDocument document;
    private SlotChoice selectedSlot = SlotChoice.For(CellSlot.Preview);
    private int selectedFlavor;
    private string? errorMessage;
    private string? noticeMessage;
    private IBulbResolver? edgeSampleBulbs;
    private int edgeSampleRevision;
    private int thumbnailRevision;
    private Task pending = Task.CompletedTask;

    /// <summary>Creates the group for a document.</summary>
    /// <param name="document">The bulb as opened (copied).</param>
    /// <param name="bulbId">The bulb's id (the edge sample shows the document under it).</param>
    /// <param name="catalog">The catalog.</param>
    /// <param name="prompts">The file dialog of "Change...".</param>
    public AnimationFramesViewModel(BulbDocument document, string bulbId, IBulbResolver catalog, IBulbEditingPrompts prompts)
    {
        this.document = document.Clone();
        this.bulbId = bulbId;
        this.catalog = catalog;
        this.prompts = prompts;
        SlotMap = [.. SlotChoice.All.OrderBy(c => c.Row).ThenBy(c => c.Column).Select(c => new SlotMapCell(c))];
        Flavors = [.. Enumerable.Range(0, BulbDocument.MaxFlavors).Select(i => new FlavorChoice(i))];
        ChangeCommand = new AsyncRelayCommand(ChangeAsync);
        RemoveFlavorCommand = new RelayCommand(RemoveFlavor, () => CanRemoveFlavor);
        CopyToAllSidesCommand = new RelayCommand(CopyToAllSides, () => IsSideSelected);
        UndoCommand = new RelayCommand(Undo, () => history.CanUndo);
        RedoCommand = new RelayCommand(Redo, () => history.CanRedo);
        Preview.PropertyChanged += OnPreviewChanged;
        UpdateFlavorMarks();
        OnDocumentChanged(raiseChanged: false);
    }

    /// <summary>Raised after the animations or the list-preview window changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The edited animations (texts and categories live in <see cref="BulbInformationViewModel"/>).</summary>
    public BulbDocument Document => document;

    /// <summary>The preview's picture, frame, zoom, transparency and GIF tip.</summary>
    public PreviewViewModel Preview { get; } = new();

    /// <summary>The 9 slots in the 5.4 combo order.</summary>
    public IReadOnlyList<SlotChoice> Slots => SlotChoice.All;

    /// <summary>The 3 x 3 slot map, row by row.</summary>
    public IReadOnlyList<SlotMapCell> SlotMap { get; }

    /// <summary>"Flavor 1" ... "Flavor 8".</summary>
    public IReadOnlyList<FlavorChoice> Flavors { get; }

    /// <summary>The selected slot (choosing one selects flavor 1, 5.4).</summary>
    public SlotChoice SelectedSlot
    {
        get => selectedSlot;
        set
        {
            if (value is null || value == selectedSlot)
            {
                return;
            }

            selectedSlot = value;
            selectedFlavor = 0;
            OnPropertyChanged(nameof(SelectedFlavor));
            OnSelectionChanged();
        }
    }

    /// <summary>The slot-map cell of the selected slot.</summary>
    public SlotMapCell SelectedCell
    {
        get => SlotMap.First(c => c.Choice == selectedSlot);
        set
        {
            if (value is not null)
            {
                SelectedSlot = value.Choice;
            }
        }
    }

    /// <summary>The selected flavor (0-7; always 0 for corners and the preview).</summary>
    public int SelectedFlavor
    {
        get => selectedFlavor;
        set
        {
            int flavor = IsSideSelected ? Math.Clamp(value, 0, BulbDocument.MaxFlavors - 1) : 0;
            if (SetProperty(ref selectedFlavor, flavor))
            {
                OnSelectionChanged();
            }
        }
    }

    /// <summary>True when a side is selected: the flavor combo, "Copy to All Sides" and flavors apply.</summary>
    public bool IsSideSelected => selectedSlot.Slot.IsSide();

    /// <summary>True when the bulb list preview is selected: the white square shows.</summary>
    public bool IsPreviewSelected => selectedSlot.Slot == CellSlot.Preview;

    /// <summary>Text 206: what the selected slot is.</summary>
    public string SlotDescription => selectedSlot.Slot switch
    {
        CellSlot.Preview => BulbFactoryStrings.PreviewDescription,
        _ when selectedSlot.Slot.IsCorner() => BulbFactoryStrings.CornerDescription,
        _ => BulbFactoryStrings.SideDescription,
    };

    /// <summary>Text 205: what "Change..." and "Remove Flavor" do here.</summary>
    public string ChangeHint => selectedSlot.Slot switch
    {
        CellSlot.Preview => BulbFactoryStrings.PreviewHint,
        _ when selectedSlot.Slot.IsCorner() => BulbFactoryStrings.CornerHint,
        _ when selectedFlavor == 0 => BulbFactoryStrings.FirstFlavorHint,
        _ when SelectedGif is not null => BulbFactoryStrings.RemovableFlavorHint,
        _ => BulbFactoryStrings.EmptyFlavorHint,
    };

    /// <summary>The text shown instead of a picture (an empty flavor, a damaged animation), or null.</summary>
    public string? PreviewMessage => SelectedGif is null
        ? BulbFactoryStrings.NoAnimation
        : Preview.Animation is { IsDamaged: true } ? BulbFactoryStrings.DamagedAnimation : null;

    /// <summary>True when the white square of the list picture shows (bulb list preview selected, picture loaded).</summary>
    public bool IsWhiteSquareVisible => IsPreviewSelected && Preview.Animation is { IsDamaged: false };

    /// <summary>The list-preview window in art pixels of frame 0.</summary>
    public RectI WhiteSquare => document.PreviewWindow;

    /// <summary>Why the chosen files cannot be used ("Cannot Import GIF File: ..."), or null.</summary>
    public string? ErrorMessage
    {
        get => errorMessage;
        set => SetProperty(ref errorMessage, value);
    }

    /// <summary>A note after "Copy to All Sides" (how to undo it), or null.</summary>
    public string? NoticeMessage
    {
        get => noticeMessage;
        set => SetProperty(ref noticeMessage, value);
    }

    /// <summary>The edge sample's bulbs: the document under <see cref="EdgeSampleBulbId"/>, the catalog for the rest.</summary>
    public IBulbResolver? EdgeSampleBulbs
    {
        get => edgeSampleBulbs;
        private set => SetProperty(ref edgeSampleBulbs, value);
    }

    /// <summary>The bulb the edge sample shows.</summary>
    public string EdgeSampleBulbId => bulbId;

    /// <summary>The edge the sample shows: the selected side; for a corner its top or bottom edge; for the preview the top.</summary>
    public Side EdgeSampleSide => selectedSlot.Slot switch
    {
        _ when IsSideSelected => selectedSlot.Slot.ToSide(),
        CellSlot.BottomLeft or CellSlot.BottomRight => Side.Bottom,
        _ => Side.Top,
    };

    /// <summary>True for a corner: the sample shows the corners at the ends of its edge.</summary>
    public bool EdgeSampleIncludesCorners => selectedSlot.Slot.IsCorner();

    /// <summary>"Change...": choose one GIF or PNG frames for the selected slot and flavor.</summary>
    public AsyncRelayCommand ChangeCommand { get; }

    /// <summary>"Remove Flavor" (flavors 2-8 that have an animation).</summary>
    public RelayCommand RemoveFlavorCommand { get; }

    /// <summary>"Copy to All Sides" (a side is selected).</summary>
    public RelayCommand CopyToAllSidesCommand { get; }

    /// <summary>Undo the last change of the animations (Ctrl+Z).</summary>
    public RelayCommand UndoCommand { get; }

    /// <summary>Redo it (Ctrl+Y).</summary>
    public RelayCommand RedoCommand { get; }

    private BulbGif? SelectedGif => document.GetGif(selectedSlot.Slot, selectedFlavor);

    private bool CanRemoveFlavor => IsSideSelected && selectedFlavor > 0 && SelectedGif is not null;

    /// <summary>Completes when every decode started so far has reached the view.</summary>
    /// <returns>The task.</returns>
    public async Task WhenIdleAsync()
    {
        Task current;
        do
        {
            current = pending;
            await current;
        }
        while (current != pending);
    }

    /// <summary>Arrow keys on the preview: move the white square by 1 pixel, with Shift by 8 (kept inside the picture).</summary>
    /// <param name="dx">Horizontal pixels.</param>
    /// <param name="dy">Vertical pixels.</param>
    /// <returns>True when the square could move (it is shown).</returns>
    public bool MoveWhiteSquareBy(int dx, int dy)
    {
        if (!IsWhiteSquareVisible)
        {
            return false;
        }

        RectI square = document.PreviewWindow;
        MoveWhiteSquare(square.Left + dx, square.Top + dy);
        return true;
    }

    /// <summary>Dragging the white square: it is centred on the pointer, kept inside the picture (as in 5.4).</summary>
    /// <param name="x">Pointer position in art pixels.</param>
    /// <param name="y">Pointer position in art pixels.</param>
    public void MoveWhiteSquareTo(double x, double y)
    {
        if (IsWhiteSquareVisible)
        {
            RectI square = document.PreviewWindow;
            MoveWhiteSquare((int)Math.Floor(x - square.Width / 2.0), (int)Math.Floor(y - square.Height / 2.0));
        }
    }

    /// <summary>Arrow keys on the slot map: move the selection to a neighbouring cell.</summary>
    /// <param name="rows">Rows to move (-1, 0, 1).</param>
    /// <param name="columns">Columns to move (-1, 0, 1).</param>
    public void MoveSlotSelection(int rows, int columns) =>
        SelectedSlot = SlotChoice.At(Math.Clamp(selectedSlot.Row + rows, 0, 2), Math.Clamp(selectedSlot.Column + columns, 0, 2));

    /// <summary>"Change..." with chosen or dropped files: one GIF, or PNG frames, for the slot and flavor selected now.</summary>
    /// <param name="paths">The files.</param>
    /// <returns>A task that completes when the animation was used or the problem is shown.</returns>
    public async Task ImportFilesAsync(IReadOnlyList<string> paths)
    {
        CellSlot slot = selectedSlot.Slot;
        int flavor = selectedFlavor;
        ErrorMessage = null;
        NoticeMessage = null;
        PictureImportResult result = await Task.Run(() => PictureImport.Load(paths));
        if (result.Gif is null)
        {
            ErrorMessage = result.Error;
            return;
        }

        history.Record(document);
        document.SetAnimation(slot, flavor, result.Gif);
        OnDocumentChanged();
    }

    private async Task ChangeAsync()
    {
        if (prompts.PickPictureFiles() is { Count: > 0 } paths)
        {
            await ImportFilesAsync(paths);
        }
    }

    private void RemoveFlavor()
    {
        history.Record(document);
        document.RemoveFlavor(selectedSlot.Slot.ToSide(), selectedFlavor);
        OnDocumentChanged();
    }

    private void CopyToAllSides()
    {
        history.Record(document);
        document.CopyToAllSides(selectedSlot.Slot.ToSide());
        OnDocumentChanged();
        NoticeMessage = string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.CopiedToAllSidesFormat, selectedSlot.Name);
    }

    private void Undo()
    {
        document = history.Undo(document);
        OnDocumentChanged();
    }

    private void Redo()
    {
        document = history.Redo(document);
        OnDocumentChanged();
    }

    private void MoveWhiteSquare(int x, int y)
    {
        RectI before = document.PreviewWindow;
        document.MovePreviewWindow(x, y);
        if (document.PreviewWindow != before)
        {
            OnPropertyChanged(nameof(WhiteSquare));
            Track(LoadThumbnailsAsync());
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSelectionChanged()
    {
        Preview.Restart();
        UpdateFlavorMarks();
        foreach (string property in (string[])[nameof(SelectedSlot), nameof(SelectedCell), nameof(IsSideSelected), nameof(IsPreviewSelected),
                     nameof(SlotDescription), nameof(ChangeHint), nameof(PreviewMessage), nameof(EdgeSampleSide), nameof(EdgeSampleIncludesCorners)])
        {
            OnPropertyChanged(property);
        }

        RefreshCommands();
        Track(ShowSelectedAnimationAsync());
    }

    private void OnDocumentChanged(bool raiseChanged = true)
    {
        UpdateFlavorMarks();
        OnPropertyChanged(nameof(ChangeHint));
        OnPropertyChanged(nameof(PreviewMessage));
        OnPropertyChanged(nameof(WhiteSquare));
        RefreshCommands();
        Track(ShowSelectedAnimationAsync());
        Track(LoadThumbnailsAsync());
        Track(RefreshEdgeSampleAsync());
        if (raiseChanged)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPreviewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewViewModel.Animation))
        {
            OnPropertyChanged(nameof(PreviewMessage));
            OnPropertyChanged(nameof(IsWhiteSquareVisible));
        }
    }

    /// <summary>The flavor items' " *" marks; for the preview and the corners every item carries the mark of their single animation (5.4).</summary>
    private void UpdateFlavorMarks()
    {
        foreach (FlavorChoice flavor in Flavors)
        {
            flavor.Update(document.GetGif(selectedSlot.Slot, IsSideSelected ? flavor.Index : 0) is not null);
        }
    }

    private void RefreshCommands()
    {
        RemoveFlavorCommand.Refresh();
        CopyToAllSidesCommand.Refresh();
        UndoCommand.Refresh();
        RedoCommand.Refresh();
    }

    private void Track(Task task) => pending = Task.WhenAll(pending, task);

    private async Task ShowSelectedAnimationAsync()
    {
        BulbGif? gif = SelectedGif;
        Task<EditorAnimation>? decoding = gif is null ? null : animations.GetAsync(gif);
        if (decoding is { IsCompleted: false })
        {
            Preview.Show(null, IsPreviewSelected);
        }

        EditorAnimation? shown = decoding is null ? null : await decoding;
        if (ReferenceEquals(gif, SelectedGif))
        {
            Preview.Show(shown, IsPreviewSelected);
        }
    }

    private async Task LoadThumbnailsAsync()
    {
        int revision = ++thumbnailRevision;
        foreach (SlotMapCell cell in SlotMap)
        {
            BulbGif? gif = document.GetGif(cell.Choice.Slot, 0);
            EditorAnimation? shown = gif is null ? null : await animations.GetAsync(gif);
            if (revision != thumbnailRevision)
            {
                return;
            }

            cell.Thumbnail = shown is null ? null
                : cell.Choice.Slot == CellSlot.Preview ? shown.ListPicture(document.PreviewWindow) : shown.Frames[0];
        }
    }

    private async Task RefreshEdgeSampleAsync()
    {
        int revision = ++edgeSampleRevision;
        BulbDocument snapshot = document.Clone();
        string contentKey = DocumentBulbResolver.ContentKeyFor(sessionKey, revision);
        DocumentBulbResolver resolver = await Task.Run(() => DocumentBulbResolver.Create(catalog, bulbId, snapshot, contentKey));
        if (revision == edgeSampleRevision)
        {
            EdgeSampleBulbs = resolver;
        }
    }
}
