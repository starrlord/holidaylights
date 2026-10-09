using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HolidayLights.App.Controls;
using Microsoft.Win32;

namespace HolidayLights.App.BulbFactory;

/// <summary>
/// The Bulb Editing window (PRODUCT-SPEC 3.3.1): binds <see cref="BulbEditingViewModel"/>, steps the preview at the flash
/// speed, routes the keyboard (Ctrl+S, F1, Esc, Ctrl+Z/Ctrl+Y outside text boxes, arrows on the slot map and the preview),
/// takes dropped pictures on the preview and asks "Discard Changes?" before closing with unsaved changes (Cancel, Esc, X).
/// </summary>
internal sealed partial class BulbEditingWindow : Window, IBulbEditingPrompts
{
    private const int LargeSquareStep = 8;
    private const double PreviewMargin = 16;

    private readonly Action showHelp;
    private readonly DispatcherTimer stepTimer;
    private bool closeAllowed;
    private bool askingToClose;

    /// <summary>Creates the window for an opened bulb.</summary>
    /// <param name="session">The opened bulb file.</param>
    /// <param name="knownCategories">Every known category.</param>
    /// <param name="stepInterval">The flash speed (one frame per step).</param>
    /// <param name="showHelp">Opens "Making Your Own Bulbs" (F1).</param>
    public BulbEditingWindow(BulbEditorSession session, IEnumerable<string> knownCategories, TimeSpan stepInterval, Action showHelp)
    {
        this.showHelp = showHelp;
        ViewModel = new BulbEditingViewModel(session, knownCategories, this);
        DataContext = ViewModel;
        InitializeComponent();
        stepTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = stepInterval };
        stepTimer.Tick += (_, _) => ViewModel.Frames.Preview.Tick();
        ViewModel.CloseRequested += (_, _) => CloseNow();
        ViewModel.Information.CategoryAdded += (_, item) => BringCategoryIntoView(item);
        Loaded += (_, _) =>
        {
            stepTimer.Start();
            NameBox.Focus();
        };
        Closed += (_, _) => stepTimer.Stop();
    }

    /// <summary>The editor.</summary>
    public BulbEditingViewModel ViewModel { get; }

    private AnimationFramesViewModel Frames => ViewModel.Frames;

    /// <summary>Asks "Discard Changes?" when needed and closes the window unless the user keeps editing.</summary>
    /// <returns>True when the window closed.</returns>
    public async Task<bool> RequestCloseAsync()
    {
        if (askingToClose)
        {
            return false;
        }

        askingToClose = true;
        try
        {
            if (!await ViewModel.ConfirmCloseAsync())
            {
                return false;
            }

            CloseNow();
            return true;
        }
        finally
        {
            askingToClose = false;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string>? PickPictureFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = BulbFactoryStrings.ChangeDialogTitle,
            Filter = BulbFactoryStrings.PictureFilter,
            Multiselect = true,
            CheckFileExists = true,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileNames : null;
    }

    /// <inheritdoc />
    public string? AskNewCategory() => NewCategoryWindow.Ask(this);

    /// <inheritdoc />
    public Task<bool> ConfirmDiscardAsync(string bulbName) =>
        ContentDialogs.ShowAsync(this, new ContentDialogOptions(
            BulbFactoryStrings.DiscardTitle,
            string.Format(CultureInfo.CurrentCulture, BulbFactoryStrings.DiscardTextFormat, bulbName),
            BulbFactoryStrings.Discard,
            BulbFactoryStrings.KeepEditing,
            PrimaryIsDestructive: true));

    /// <inheritdoc />
    /// <remarks>
    /// The X button and Alt+F4 close at once when nothing changed. Otherwise the attempt is cancelled and "Discard Changes?"
    /// is asked once it is over (a window cannot be closed again from inside its own closing).
    /// </remarks>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (closeAllowed || e.Cancel)
        {
            return;
        }

        if (!ViewModel.IsSaving && !ViewModel.HasUnsavedChanges)
        {
            closeAllowed = true;
            return;
        }

        e.Cancel = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _ = RequestCloseAsync());
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.F1)
        {
            showHelp();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _ = RequestCloseAsync();
            e.Handled = true;
        }
        else if (control && ((e.Key == Key.Z && shift) || e.Key == Key.Y))
        {
            e.Handled = Run(Frames.RedoCommand);
        }
        else if (control && e.Key == Key.Z)
        {
            e.Handled = Run(Frames.UndoCommand);
        }
    }

    private static bool Run(ICommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    private static (int X, int Y) Direction(Key key) => key switch
    {
        Key.Left => (-1, 0),
        Key.Right => (1, 0),
        Key.Up => (0, -1),
        Key.Down => (0, 1),
        _ => (0, 0),
    };

    private void CloseNow()
    {
        if (!closeAllowed)
        {
            closeAllowed = true;
            Close();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _ = RequestCloseAsync();

    /// <summary>Author and copyright have two lines (5.4 stored "Name\r\nE-mail"): Enter does nothing on the second line.</summary>
    private void OnTwoLineBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box && box.Text.Contains('\n', StringComparison.Ordinal) && !box.SelectedText.Contains('\n', StringComparison.Ordinal))
        {
            e.Handled = true;
        }
    }

    /// <summary>The category list is one tab stop; its label's access key lands on the first check box.</summary>
    private void OnCategoryListFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(e.NewFocus, CategoryList))
        {
            CategoryList.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }

    private void BringCategoryIntoView(CategoryItem item) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (CategoryList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        });

    private void OnSlotMapKeyDown(object sender, KeyEventArgs e)
    {
        (int columns, int rows) = Direction(e.Key);
        if (columns == 0 && rows == 0)
        {
            return;
        }

        Frames.MoveSlotSelection(rows, columns);
        (SlotMapList.ItemContainerGenerator.ContainerFromItem(Frames.SelectedCell) as ListBoxItem)?.Focus();
        e.Handled = true;
    }

    /// <summary>Arrows move the white square (Shift: 8 pixels); while paused, Left and Right step through the frames.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        (int x, int y) = Direction(e.Key);
        if (x == 0 && y == 0)
        {
            return;
        }

        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? LargeSquareStep : 1;
        e.Handled = Frames.MoveWhiteSquareBy(x * step, y * step) || (y == 0 && Frames.Preview.StepFrame(x));
    }

    private void OnSquareDragged(object? sender, Point position) => Frames.MoveWhiteSquareTo(position.X, position.Y);

    private void OnPreviewAreaSizeChanged(object sender, SizeChangedEventArgs e) =>
        Frames.Preview.SetViewport(e.NewSize.Width - PreviewMargin, e.NewSize.Height - PreviewMargin);

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Dropping a GIF (or PNG frames) on the preview is "Change...".</summary>
    private async void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            e.Handled = true;
            await Frames.ImportFilesAsync(files);
        }
    }
}
