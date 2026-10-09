using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using HolidayLights.App.Preview;

namespace HolidayLights.App.Controls;

/// <summary>A command on a bulb that the frame editor asks its page to carry out.</summary>
public enum BulbCommand
{
    /// <summary>"Edit Bulb..." or "Edit Categories...".</summary>
    Edit,

    /// <summary>"Bulb Credits".</summary>
    Credits,

    /// <summary>"Find in Bulb List".</summary>
    FindInList,
}

/// <summary>Arguments of <see cref="ArrangementEditor.BulbCommandRequested"/>.</summary>
/// <param name="Command">The command.</param>
/// <param name="BulbId">The bulb.</param>
public sealed record BulbCommandRequest(BulbCommand Command, string BulbId);

/// <summary>Arguments of <see cref="ArrangementEditor.FilesDropped"/>: files dropped on a box (imported, then placed there).</summary>
/// <param name="Paths">The files.</param>
/// <param name="Destination">The box, or null for the stage's nearest box.</param>
public sealed record FilesDroppedRequest(IReadOnlyList<string> Paths, BoxPosition Destination);

/// <summary>
/// The frame editor (PRODUCT-SPEC 3.2.2): Top, Right, Bottom and Left Edge each hold an ordered list of 0-6 bulb types,
/// the four corners one bulb each, around a light stage of one display. Selecting a chip, a "+" or a corner makes it the
/// target; drag and drop follows the 5.4 rules (3.2.7); the keyboard model is roving focus where focus is selection
/// (3.2.12). The editor never changes settings itself: it raises <see cref="EditRequested"/> with the edit and its texts.
/// </summary>
public partial class ArrangementEditor : UserControl
{
    /// <summary>Identifies <see cref="Arrangement"/>.</summary>
    public static readonly DependencyProperty ArrangementProperty =
        DependencyProperty.Register(nameof(Arrangement), typeof(SlotAssignment), typeof(ArrangementEditor),
            new PropertyMetadata(SlotAssignment.Empty, (d, _) => ((ArrangementEditor)d).Rebuild()));

    /// <summary>Identifies <see cref="Target"/>.</summary>
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(nameof(Target), typeof(ArrangementTarget), typeof(ArrangementEditor),
            new FrameworkPropertyMetadata(ArrangementTarget.WholeFrame, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTargetChanged));

    /// <summary>The largest stage height (PRODUCT-SPEC 3.2.1).</summary>
    private const double MaxStageHeight = 300;

    /// <summary>The boxes take 72 DIP plus an 8 DIP gap on each side of the stage.</summary>
    private const double BoxesWidth = 160;

    private const double CaptionHeight = 22;

    private readonly Dictionary<Side, EdgeBox> edges = [];
    private readonly Dictionary<Corner, CornerBox> corners = [];
    private readonly DispatcherTimer highlightTimer;
    private IAppServices? services;
    private CellSlot? hoverSlot;
    private SlotAssignment? tryOn;
    private bool updatingDisplays;

    /// <summary>Creates the editor.</summary>
    public ArrangementEditor()
    {
        InitializeComponent();
        highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        highlightTimer.Tick += (_, _) =>
        {
            highlightTimer.Stop();
            Stage.HighlightedSlot = hoverSlot;
        };
        BuildBoxes();
        Stage.Hit += OnStageHit;
        Stage.ScreenAreaChanged += (_, _) => PlaceDisplayPicker();
        AllowDrop = true;
        Frame.Background = Brushes.Transparent;
        DragOver += OnEditorDragOver;
        AddHandler(DragEnterEvent, new DragEventHandler(OnEditorDragEnter), handledEventsToo: true);
        AddHandler(DragLeaveEvent, new DragEventHandler(OnEditorDragLeave), handledEventsToo: true);
        StageArea.DragEnter += OnStageDragOver;
        StageArea.DragOver += OnStageDragOver;
        StageArea.DragLeave += (_, _) => Stage.HighlightedSlot = hoverSlot;
        StageArea.Drop += OnStageDrop;
        SizeChanged += (_, _) => UpdateStageSize();
        PreviewKeyDown += OnEditorPreviewKeyDown;
        Loaded += (_, _) => AttachDisplays();
        Unloaded += (_, _) => DetachDisplays();
    }

    /// <summary>Raised with an edit for the page to apply (one undo step each).</summary>
    public event EventHandler<ArrangementEdit>? EditRequested;

    /// <summary>Raised when the target changed (by selection, a stage click or the page).</summary>
    public event EventHandler? TargetChanged;

    /// <summary>Enter on a box, or "Pick a Bulb...": the page moves focus to the Bulb List search box.</summary>
    public event EventHandler? PickBulbRequested;

    /// <summary>Ctrl+V on a box: the page adds the bulbs selected in the Bulb List there (an edge appends them, a corner takes the first).</summary>
    public event EventHandler<BoxPosition>? PasteRequested;

    /// <summary>A chip menu command on a bulb that the page carries out.</summary>
    public event EventHandler<BulbCommandRequest>? BulbCommandRequested;

    /// <summary>Bulb files or GIFs dropped on a box.</summary>
    public event EventHandler<FilesDroppedRequest>? FilesDropped;

    /// <summary>The services (set by the page before the editor loads).</summary>
    public IAppServices? Services
    {
        get => services;
        set
        {
            services = value;
            Stage.Services = value;
            foreach (EdgeBox box in edges.Values)
            {
                box.Sample.Services = value;
                box.Plus.Services = value;
            }

            foreach (CornerBox box in corners.Values)
            {
                box.Chip.Services = value;
            }

            Rebuild();
        }
    }

    /// <summary>The arrangement shown (the page sets it from the settings).</summary>
    public SlotAssignment Arrangement
    {
        get => (SlotAssignment)GetValue(ArrangementProperty);
        set => SetValue(ArrangementProperty, value);
    }

    /// <summary>The target (PRODUCT-SPEC 3.2.4).</summary>
    public ArrangementTarget Target
    {
        get => (ArrangementTarget)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>The display the stage shows (a <see cref="DisplayInfo.DeviceId"/>), or null for the main display.</summary>
    public string? DisplayId => Stage.DisplayId;

    /// <summary>Shows a display in the preview (Home: a click on that display opens Bulb Factory previewing it).</summary>
    /// <param name="displayId">A <see cref="DisplayInfo.DeviceId"/>.</param>
    public void ShowDisplay(string displayId)
    {
        Stage.DisplayId = displayId;
        UpdateDisplays();
    }

    /// <summary>Moves keyboard focus to the target's box (or the Top Edge "+").</summary>
    public void FocusTarget() => FocusableFor(Target)?.Focus();

    /// <summary>The bulb name of an id (the id's file stem when the bulb is unknown).</summary>
    /// <param name="bulbId">A bulb id.</param>
    /// <returns>The name.</returns>
    public string NameOf(string bulbId)
    {
        if (services is not null && services.Bulbs.TryGetInfo(bulbId, out BulbInfo? info))
        {
            return info.Name;
        }

        return BulbIds.IsValid(bulbId) ? BulbIds.GetKey(bulbId) : bulbId;
    }

    /// <summary>
    /// Shows a try-on with its pill ("Trying On: Candy Canes - the whole frame"), or the real arrangement again. While
    /// trying on, the stage zooms into the target's corner so the bulbs show at a legible size (PO decision 8), the edge
    /// samples show the edges tried on, and the corner boxes show the corners tried on (outlined as a try-on).
    /// </summary>
    /// <param name="arrangement">The arrangement to show, or null for the real one.</param>
    /// <param name="pill">The pill text, or null.</param>
    public void ShowTryOn(SlotAssignment? arrangement, string? pill)
    {
        tryOn = arrangement;
        Stage.Arrangement = arrangement ?? Arrangement;
        Stage.OverlayText = arrangement is null ? null : pill;
        if (arrangement is null)
        {
            Stage.ClearValue(LightStage.ZoomProperty);
            Stage.ClearValue(LightStage.ZoomCornerProperty);
        }
        else
        {
            Stage.ZoomCorner = TryOnCorner(Target);
            Stage.Zoom = StageZoom.Corner;
        }

        foreach (EdgeBox box in edges.Values)
        {
            box.ShowSample();
        }

        foreach (CornerBox box in corners.Values)
        {
            box.ShowTryOn(arrangement);
        }
    }

    /// <summary>The corner the stage zooms into while trying on: the target's corner, the start of its edge, else the top-left.</summary>
    /// <param name="target">The target.</param>
    /// <returns>The corner.</returns>
    internal static Corner TryOnCorner(ArrangementTarget target) => target.Kind switch
    {
        ArrangementTargetKind.Corner => target.Corner,
        ArrangementTargetKind.Chip or ArrangementTargetKind.Plus => target.Side switch
        {
            Side.Right => Corner.TopRight,
            Side.Bottom => Corner.BottomLeft,
            _ => Corner.TopLeft,
        },
        _ => Corner.TopLeft,
    };

    /// <summary>Shows a slot's strip or corner on the stage for a second (after a selection or an edit).</summary>
    /// <param name="slot">The slot, or null.</param>
    public void FlashHighlight(CellSlot? slot)
    {
        Stage.HighlightedSlot = slot;
        highlightTimer.Stop();
        if (slot is not null)
        {
            highlightTimer.Start();
        }
    }

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ArrangementEditor)d;
        var target = (ArrangementTarget)e.NewValue;
        editor.UpdateSelection();
        editor.FlashHighlight(target.Slot);
        if (editor.IsKeyboardFocusWithin && !target.IsQuick && !editor.DisplayPicker.IsKeyboardFocusWithin)
        {
            editor.FocusTarget();
        }

        editor.TargetChanged?.Invoke(editor, EventArgs.Empty);
    }

    private void Raise(ArrangementEdit edit) => EditRequested?.Invoke(this, edit);

    private void BuildBoxes()
    {
        foreach (Side side in CellSlots.Sides)
        {
            var box = new EdgeBox(this, side);
            edges[side] = box;
            (int row, int column) = side switch
            {
                Side.Top => (0, 2),
                Side.Bottom => (4, 2),
                Side.Left => (2, 0),
                _ => (2, 4),
            };
            Grid.SetRow(box.Root, row);
            Grid.SetColumn(box.Root, column);
            Frame.Children.Add(box.Root);
        }

        foreach (Corner corner in CellSlots.Corners)
        {
            var box = new CornerBox(this, corner);
            corners[corner] = box;
            Grid.SetRow(box.Root, corner is Corner.TopLeft or Corner.TopRight ? 0 : 4);
            Grid.SetColumn(box.Root, corner is Corner.TopLeft or Corner.BottomLeft ? 0 : 4);
            Frame.Children.Add(box.Root);
        }
    }

    /// <summary>Shows the arrangement: chips, "+" placeholders, corners, captions, names and the stage summary.</summary>
    private void Rebuild()
    {
        SlotAssignment arrangement = Arrangement;
        foreach (EdgeBox box in edges.Values)
        {
            box.Show(arrangement.GetEdge(box.Side));
        }

        foreach (CornerBox box in corners.Values)
        {
            box.Show(arrangement.GetCorner(box.Corner));
        }

        Stage.Arrangement = tryOn ?? arrangement;
        string summary = ArrangementTexts.Summary(arrangement, NameOf);
        AutomationProperties.SetHelpText(this, summary);
        AutomationProperties.SetHelpText(Stage, summary);
        ArrangementTarget normalized = Target.Normalize(arrangement);
        if (normalized != Target)
        {
            Target = normalized;
        }
        else
        {
            UpdateSelection();
        }
    }

    /// <summary>Marks the target's item selected and makes it the editor's one tab stop (roving focus).</summary>
    private void UpdateSelection()
    {
        ArrangementTarget target = Target;
        ArrangementChip? focusable = FocusableFor(target);
        foreach (ArrangementChip chip in AllChips())
        {
            chip.IsSelected = !target.IsQuick && ReferenceEquals(chip, focusable);
            chip.IsTabStop = ReferenceEquals(chip, focusable);
        }
    }

    private IEnumerable<ArrangementChip> AllChips() =>
        edges.Values.SelectMany(b => b.Chips.Append(b.Plus)).Concat(corners.Values.Select(c => c.Chip));

    /// <summary>The item that holds focus for a target (a quick target: the Top Edge "+", or its last chip when full).</summary>
    private ArrangementChip? FocusableFor(ArrangementTarget target)
    {
        if (edges.Count == 0)
        {
            return null;
        }

        switch (target.Kind)
        {
            case ArrangementTargetKind.Chip when target.Index < edges[target.Side].Chips.Count:
                return edges[target.Side].Chips[target.Index];
            case ArrangementTargetKind.Corner:
                return corners[target.Corner].Chip;
            case ArrangementTargetKind.Plus or ArrangementTargetKind.Chip:
                return edges[target.Side].EndItem;
            default:
                return edges[Side.Top].Plus.Visibility == Visibility.Visible ? edges[Side.Top].Plus : edges[Side.Top].EndItem;
        }
    }

    /// <summary>The target a focused item stands for.</summary>
    private ArrangementTarget? TargetOf(ArrangementChip chip)
    {
        foreach (EdgeBox box in edges.Values)
        {
            int index = box.Chips.IndexOf(chip);
            if (index >= 0)
            {
                return ArrangementTarget.ForChip(box.Side, index);
            }

            if (ReferenceEquals(chip, box.Plus))
            {
                return ArrangementTarget.ForPlus(box.Side);
            }
        }

        return corners.Values.FirstOrDefault(c => ReferenceEquals(c.Chip, chip)) is { } corner ? ArrangementTarget.ForCorner(corner.Corner) : null;
    }

    private void OnChipFocused(ArrangementChip chip)
    {
        if (TargetOf(chip) is { } target && target != Target)
        {
            Target = target;
        }
    }

    private void OnChipRemove(ArrangementChip chip)
    {
        switch (TargetOf(chip))
        {
            case { Kind: ArrangementTargetKind.Chip } target:
                Raise(ArrangementEdits.Remove(Arrangement, target.Side, target.Index, NameOf));
                break;
            case { Kind: ArrangementTargetKind.Corner } target:
                Raise(ArrangementEdits.ClearCorner(Arrangement, target.Corner, NameOf));
                break;
        }
    }

    private void OnBoxHover(CellSlot? slot)
    {
        hoverSlot = slot;
        if (!highlightTimer.IsEnabled)
        {
            Stage.HighlightedSlot = slot;
        }
    }

    /// <summary>A click on the stage selects (PRODUCT-SPEC 3.2.3): a bulb its chip, an empty band the "+", a corner zone the corner, the middle the Whole Frame.</summary>
    private void OnStageHit(object? sender, LightStageHitEventArgs e)
    {
        SlotAssignment arrangement = Arrangement;
        if (e.Placement is { IsCorner: true } corner)
        {
            Target = ArrangementTarget.ForCorner(corner.Slot.ToCorner());
        }
        else if (e.Corner is { } zone)
        {
            Target = ArrangementTarget.ForCorner(zone);
        }
        else if (e.Placement is { } placement && e.Edge is { } side && arrangement.GetEdge(side).Count is var count and > 0)
        {
            Target = ArrangementTarget.ForChip(side, placement.Index % count);
        }
        else if (e.Edge is { } band)
        {
            Target = ArrangementTarget.ForPlus(band).Normalize(arrangement);
        }
        else
        {
            Target = ArrangementTarget.WholeFrame;
        }

        if (!Target.IsQuick)
        {
            FocusTarget();
        }
    }

    private void UpdateStageSize()
    {
        double width = Math.Max(0, ActualWidth - BoxesWidth);
        DisplayInfo? display = ShownDisplay();
        double aspect = display is null || display.Bounds.Width == 0 ? 9.0 / 16 : (double)display.Bounds.Height / display.Bounds.Width;
        StageRow.Height = new GridLength(Math.Min(MaxStageHeight, Math.Round(width * aspect)) + CaptionHeight);
    }

    private DisplayInfo? ShownDisplay()
    {
        if (services is null)
        {
            return null;
        }

        IReadOnlyList<DisplayInfo> displays = services.Displays.Displays;
        return displays.FirstOrDefault(d => d.DeviceId == Stage.DisplayId) ?? services.Displays.Primary;
    }

    private void AttachDisplays()
    {
        if (services is null)
        {
            return;
        }

        services.Lights.SceneChanged -= OnSceneChanged;
        services.Lights.SceneChanged += OnSceneChanged;
        UpdateDisplays();
    }

    private void DetachDisplays()
    {
        if (services is not null)
        {
            services.Lights.SceneChanged -= OnSceneChanged;
        }
    }

    private void OnSceneChanged(object? sender, EventArgs e) => UpdateDisplays();

    /// <summary>With two or more enabled displays a "Display 1 (Main)" selector appears; the caption describes the display.</summary>
    private void UpdateDisplays()
    {
        if (services is null)
        {
            return;
        }

        updatingDisplays = true;
        try
        {
            DisplayInfo[] enabled = [.. services.Lights.Scene.Displays.Where(d => d.Enabled).Select(d => d.Display)];
            DisplayPicker.Items.Clear();
            foreach (DisplayInfo display in enabled)
            {
                DisplayPicker.Items.Add(new ComboBoxItem
                {
                    Content = display.IsPrimary ? $"Display {display.Number} (Main)" : $"Display {display.Number}",
                    Tag = display.DeviceId,
                });
            }

            DisplayPicker.Visibility = enabled.Length >= 2 ? Visibility.Visible : Visibility.Collapsed;
            string? chosen = enabled.Any(d => d.DeviceId == Stage.DisplayId) ? Stage.DisplayId : enabled.FirstOrDefault(d => d.IsPrimary)?.DeviceId ?? enabled.FirstOrDefault()?.DeviceId;
            Stage.DisplayId = chosen;
            DisplayPicker.SelectedIndex = Array.FindIndex(enabled, d => d.DeviceId == chosen);
            DisplayCaption.Text = ShownDisplay()?.Describe() ?? "";
        }
        finally
        {
            updatingDisplays = false;
        }

        UpdateStageSize();
    }

    /// <summary>Keeps the display picker in the top-right corner of the drawn screen, which the stage letterboxes.</summary>
    private void PlaceDisplayPicker()
    {
        Rect area = Stage.ScreenArea;
        DisplayPicker.Margin = area.IsEmpty
            ? new Thickness(0, 6, 6, 0)
            : new Thickness(0, area.Top + 6, Math.Max(0, Stage.ActualWidth - area.Right) + 6, 0);
    }

    private void OnDisplayPickerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingDisplays || DisplayPicker.SelectedItem is not ComboBoxItem { Tag: string id })
        {
            return;
        }

        Stage.DisplayId = id;
        DisplayCaption.Text = ShownDisplay()?.Describe() ?? "";
        UpdateStageSize();
    }

    /// <summary>Plays the settle animation of a chip that just received a bulb (scale 0.8 to 1.0, 150 ms).</summary>
    /// <param name="target">The chip's target.</param>
    public void Settle(ArrangementTarget target)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (FocusableFor(target) is not { } chip || target.IsQuick)
            {
                return;
            }

            var scale = new ScaleTransform(0.8, 0.8);
            chip.RenderTransformOrigin = new Point(0.5, 0.5);
            chip.RenderTransform = scale;
            var grow = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The frame of a box: card background, the dashed outline of valid drop targets, the 10 % accent fill under the pointer.</summary>
    private abstract class BoxBase
    {
        protected BoxBase()
        {
            Root = new AutomationGroup { AllowDrop = true };
            var background = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
            background.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
            background.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            Fill = new Rectangle { RadiusX = 6, RadiusY = 6, Opacity = 0.1, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            Fill.SetResourceReference(Shape.FillProperty, "AccentFillColorDefaultBrush");
            Refused = new Rectangle { RadiusX = 6, RadiusY = 6, Opacity = 0.15, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            Refused.SetResourceReference(Shape.FillProperty, "SystemFillColorCriticalBrush");
            Outline = new Rectangle { RadiusX = 6, RadiusY = 6, StrokeThickness = 2, StrokeDashArray = [3, 2], Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            Outline.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
            Content = new Grid { Margin = new Thickness(6, 4, 6, 4) };
            Root.Children.Add(background);
            Root.Children.Add(Fill);
            Root.Children.Add(Refused);
            Root.Children.Add(Content);
            Root.Children.Add(Outline);
        }

        public AutomationGroup Root { get; }

        public Grid Content { get; }

        public Rectangle Fill { get; }

        public Rectangle Refused { get; }

        public Rectangle Outline { get; }

        public abstract CellSlot Slot { get; }

        public void ShowDragState(bool active, bool over, bool refused)
        {
            Outline.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            Fill.Visibility = over && !refused ? Visibility.Visible : Visibility.Collapsed;
            Refused.Visibility = over && refused ? Visibility.Visible : Visibility.Collapsed;
        }

        protected static TextBlock Caption()
        {
            var caption = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            return caption;
        }
    }

    /// <summary>An edge box: caption "Top Edge 2/6", chips in order, the dashed "+", and the edge sample along the box.</summary>
    private sealed class EdgeBox : BoxBase
    {
        private readonly ArrangementEditor editor;
        private readonly TextBlock caption;
        private readonly TextBlock count;

        public EdgeBox(ArrangementEditor editor, Side side)
        {
            this.editor = editor;
            Side = side;
            bool vertical = side is Side.Left or Side.Right;
            double chipSize = vertical ? 32 : 40;
            caption = Caption();
            count = Caption();
            Panel = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };
            Plus = editor.NewChip(Side.ToSlot(), chipSize, vertical, isPlus: true);
            Panel.Children.Add(Plus);
            Sample = new LightStrip { Side = side, IncludeCorners = false, BulbHeight = 16, Focusable = false };
            Sample.SetValue(AutomationProperties.NameProperty, "");
            if (vertical)
            {
                BuildVertical(side);
            }
            else
            {
                BuildHorizontal();
            }

            Root.SetResourceReference(FrameworkElement.ToolTipProperty, side switch
            {
                Side.Top => "HL.Tip.BulbFactory.TopEdge",
                Side.Right => "HL.Tip.BulbFactory.RightEdge",
                Side.Bottom => "HL.Tip.BulbFactory.BottomEdge",
                _ => "HL.Tip.BulbFactory.LeftEdge",
            });
            ToolTipService.SetInitialShowDelay(Root, 500);
            Root.MouseEnter += (_, _) => editor.OnBoxHover(Slot);
            Root.MouseLeave += (_, _) => editor.OnBoxHover(null);
            Root.MouseRightButtonUp += (_, e) =>
            {
                if (e.OriginalSource is not DependencyObject source || FindChip(source) is null)
                {
                    editor.OpenBoxMenu(BoxPosition.OnEdge(Side, 0), Root, fromKeyboard: false);
                    e.Handled = true;
                }
            };
            Root.DragEnter += (_, e) => editor.OnEdgeDragOver(this, e);
            Root.DragOver += (_, e) => editor.OnEdgeDragOver(this, e);
            Root.DragLeave += (_, _) => editor.ClearDragMarks();
            Root.Drop += (_, e) => editor.OnEdgeDrop(this, e);
        }

        public Side Side { get; }

        public override CellSlot Slot => Side.ToSlot();

        public StackPanel Panel { get; }

        public List<ArrangementChip> Chips { get; } = [];

        public ArrangementChip Plus { get; }

        public LightStrip Sample { get; }

        /// <summary>The focusable items in order: the chips, then the "+" while the edge has room.</summary>
        public IReadOnlyList<ArrangementChip> Items => Plus.Visibility == Visibility.Visible ? [.. Chips, Plus] : Chips;

        /// <summary>The "+" while the edge has room, else its last chip.</summary>
        public ArrangementChip EndItem => Plus.Visibility == Visibility.Visible || Chips.Count == 0 ? Plus : Chips[^1];

        public void Show(IReadOnlyList<string> ids)
        {
            bool vertical = Side is Side.Left or Side.Right;
            while (Chips.Count > ids.Count)
            {
                Panel.Children.Remove(Chips[^1]);
                Chips.RemoveAt(Chips.Count - 1);
            }

            while (Chips.Count < ids.Count)
            {
                ArrangementChip chip = editor.NewChip(Side.ToSlot(), vertical ? 32 : 40, vertical, isPlus: false);
                Panel.Children.Insert(Chips.Count, chip);
                Chips.Add(chip);
            }

            string edge = ArrangementTexts.EdgeName(Side);
            for (int i = 0; i < ids.Count; i++)
            {
                ArrangementChip chip = Chips[i];
                chip.BulbId = ids[i];
                chip.Order = i + 1;
                string name = editor.NameOf(ids[i]);
                AutomationProperties.SetName(chip, string.Create(CultureInfo.CurrentCulture, $"{name}, position {i + 1} of {ids.Count} on the {edge}."));
                chip.ToolTip = editor.services is { } s && !s.Bulbs.TryGetBulb(ids[i], out _)
                    ? string.Format(CultureInfo.CurrentCulture, editor.TryFindResource("HL.Tip.BulbFactory.MissingBulb") as string ?? "{0}", BulbIds.IsValid(ids[i]) ? BulbIds.GetKey(ids[i]) + ".bul" : ids[i])
                    : name;
            }

            bool full = ids.Count >= SlotAssignment.MaxTypesPerEdge;
            Plus.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetName(Plus, string.Create(CultureInfo.CurrentCulture, $"Add a bulb to the {edge}, {ids.Count} of {SlotAssignment.MaxTypesPerEdge} used."));
            Plus.ToolTip = "Add a bulb here";
            string fraction = string.Create(CultureInfo.CurrentCulture, $"{ids.Count}/{SlotAssignment.MaxTypesPerEdge}");
            caption.Text = vertical ? ArrangementTexts.EdgeTitle(Side) : $"{ArrangementTexts.EdgeTitle(Side)}  {fraction}";
            count.Text = fraction;
            AutomationProperties.SetName(Root, string.Create(CultureInfo.CurrentCulture,
                $"{char.ToUpperInvariant(edge[0])}{edge[1..]}, {ids.Count} of {SlotAssignment.MaxTypesPerEdge} bulb types{(ids.Count == 0 ? "" : ": " + string.Join(", ", ids.Select(editor.NameOf)))}."));
            ShowSample();
        }

        /// <summary>The edge sample shows the edge tried on while a try-on runs, else the real edge.</summary>
        public void ShowSample()
        {
            SlotAssignment shown = editor.tryOn ?? editor.Arrangement;
            Sample.Arrangement = shown;
            Sample.Visibility = shown.GetEdge(Side).Count == 0 ? Visibility.Hidden : Visibility.Visible;
        }

        public int IndexOfChip(ArrangementChip chip) => Chips.IndexOf(chip);

        public ArrangementChip? FindChip(DependencyObject source)
        {
            return ElementTree.SelfAndAncestors(source).TakeWhile(node => !ReferenceEquals(node, Root)).OfType<ArrangementChip>().FirstOrDefault();
        }

        private void BuildHorizontal()
        {
            Content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            Content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.Margin = new Thickness(2, 0, 10, 0);
            var scroller = new ScrollViewer
            {
                Content = Panel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(scroller, 1);
            Grid.SetRow(Sample, 1);
            Grid.SetColumnSpan(Sample, 2);
            Sample.Margin = new Thickness(0, 0, 0, 0);
            Content.Children.Add(caption);
            Content.Children.Add(scroller);
            Content.Children.Add(Sample);
        }

        private void BuildVertical(Side side)
        {
            bool left = side == Side.Left;
            Content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Content.ColumnDefinitions.Add(new ColumnDefinition { Width = left ? new GridLength(16) : new GridLength(1, GridUnitType.Star) });
            Content.ColumnDefinitions.Add(new ColumnDefinition { Width = left ? new GridLength(1, GridUnitType.Star) : new GridLength(16) });
            Grid.SetColumnSpan(caption, 2);
            Grid.SetRow(count, 1);
            Grid.SetColumnSpan(count, 2);
            count.Margin = new Thickness(0, 0, 0, 4);
            var scroller = new ScrollViewer
            {
                Content = Panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Grid.SetRow(scroller, 2);
            Grid.SetColumn(scroller, left ? 1 : 0);
            Grid.SetRow(Sample, 2);
            Grid.SetColumn(Sample, left ? 0 : 1);
            Sample.Width = 16;
            Content.Children.Add(caption);
            Content.Children.Add(count);
            Content.Children.Add(scroller);
            Content.Children.Add(Sample);
        }
    }

    /// <summary>A corner box: caption "Top-Left", the corner's bulb (animated, fitted at up to 1.5x) or a dashed "+".</summary>
    private sealed class CornerBox : BoxBase
    {
        private readonly ArrangementEditor editor;

        public CornerBox(ArrangementEditor editor, Corner corner)
        {
            this.editor = editor;
            Corner = corner;
            TextBlock caption = Caption();
            caption.Text = ArrangementTexts.CornerCaption(corner);
            caption.FontSize = 10.5;
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            Content.Margin = new Thickness(2, 3, 2, 4);
            Chip = editor.NewChip(corner.ToSlot(), 44, vertical: false, isPlus: false);
            Chip.IsAnimated = true;
            Chip.Margin = new Thickness(0);
            Content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(Chip, 1);
            Content.Children.Add(caption);
            Content.Children.Add(Chip);
            Root.SetResourceReference(FrameworkElement.ToolTipProperty, corner switch
            {
                Corner.TopLeft => "HL.Tip.BulbFactory.TopLeftCorner",
                Corner.TopRight => "HL.Tip.BulbFactory.TopRightCorner",
                Corner.BottomRight => "HL.Tip.BulbFactory.BottomRightCorner",
                _ => "HL.Tip.BulbFactory.BottomLeftCorner",
            });
            ToolTipService.SetInitialShowDelay(Root, 500);
            Root.MouseEnter += (_, _) => editor.OnBoxHover(Slot);
            Root.MouseLeave += (_, _) => editor.OnBoxHover(null);
            Root.MouseRightButtonUp += (_, e) =>
            {
                if (!e.Handled)
                {
                    editor.OpenBoxMenu(BoxPosition.InCorner(Corner), Root, fromKeyboard: false);
                    e.Handled = true;
                }
            };
            Root.DragEnter += (_, e) => editor.OnCornerDragOver(this, e);
            Root.DragOver += (_, e) => editor.OnCornerDragOver(this, e);
            Root.DragLeave += (_, _) => editor.ClearDragMarks();
            Root.Drop += (_, e) => editor.OnCornerDrop(this, e);
        }

        public Corner Corner { get; }

        public override CellSlot Slot => Corner.ToSlot();

        public ArrangementChip Chip { get; }

        public void Show(string? bulbId)
        {
            Chip.BulbId = bulbId;
            string name = ArrangementTexts.CornerCaption(Corner).ToLowerInvariant();
            string text = $"{char.ToUpperInvariant(name[0])}{name[1..]} corner: {(bulbId is null ? "empty" : editor.NameOf(bulbId))}";
            AutomationProperties.SetName(Chip, text);
            AutomationProperties.SetName(Root, text);
            Chip.ToolTip = bulbId is null ? "Add a bulb here" : editor.NameOf(bulbId);
            ShowTryOn(editor.tryOn);
        }

        /// <summary>Shows the corner's bulb of a try-on when it differs from the real one (or the real corner again).</summary>
        /// <param name="arrangement">The arrangement tried on, or null.</param>
        public void ShowTryOn(SlotAssignment? arrangement)
        {
            string? tried = arrangement?.GetCorner(Corner);
            Chip.TryOnBulbId = tried is not null && !BulbIds.Comparer.Equals(tried, editor.Arrangement.GetCorner(Corner)) ? tried : null;
        }
    }

    private ArrangementChip NewChip(CellSlot slot, double size, bool vertical, bool isPlus)
    {
        var chip = new ArrangementChip
        {
            Services = services,
            Slot = slot,
            Width = size,
            Height = size,
            IsVertical = vertical,
            IsPlus = isPlus,
            IsTabStop = false,
            TabIndex = 0,
        };
        chip.GotKeyboardFocus += (_, _) => OnChipFocused(chip);
        chip.RemoveRequested += (_, e) =>
        {
            OnChipRemove(chip);
            e.Handled = true;
        };
        chip.PreviewMouseLeftButtonDown += (_, e) => OnChipMouseDown(chip, e);
        chip.PreviewMouseMove += (_, e) => OnChipMouseMove(chip, e);
        chip.MouseRightButtonUp += (_, e) =>
        {
            chip.Focus();
            if (TargetOf(chip) is { } target)
            {
                OpenMenuFor(chip, target, fromKeyboard: false);
            }

            e.Handled = true;
        };
        return chip;
    }
}
