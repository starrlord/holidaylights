using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HolidayLights.App.Controls;
using Microsoft.Win32;

namespace HolidayLights.App.Gallery;

/// <summary>
/// The Bulb List (PRODUCT-SPEC 3.2.5): every bulb that is not removed, searchable, filtered by "Show:", sorted, as tiles
/// or Details rows, with try-on, drag and drop and the context menu. Selecting or hovering never changes the
/// arrangement: the page decides what <see cref="ActionRequested"/> and <see cref="TryOnChanged"/> do. Also the body of
/// the Choose a Bulb dialog (<see cref="IsChooser"/>, add-on bulbs only).
/// </summary>
public partial class BulbList : UserControl
{
    /// <summary>Identifies <see cref="Services"/>.</summary>
    public static readonly DependencyProperty ServicesProperty =
        DependencyProperty.Register(nameof(Services), typeof(IAppServices), typeof(BulbList), new PropertyMetadata(null, (d, _) => ((BulbList)d).Attach()));

    /// <summary>Identifies <see cref="TileArtScale"/>.</summary>
    public static readonly DependencyProperty TileArtScaleProperty =
        DependencyProperty.Register(nameof(TileArtScale), typeof(double), typeof(BulbList), new PropertyMetadata(2.0));

    /// <summary>Identifies <see cref="Target"/>.</summary>
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(nameof(Target), typeof(ArrangementTarget), typeof(BulbList),
            new PropertyMetadata(ArrangementTarget.WholeFrame, (d, _) => ((BulbList)d).UpdateTargetLine()));

    /// <summary>Identifies <see cref="IsChooser"/>.</summary>
    public static readonly DependencyProperty IsChooserProperty =
        DependencyProperty.Register(nameof(IsChooser), typeof(bool), typeof(BulbList), new PropertyMetadata(false, (d, _) => ((BulbList)d).UpdateChooserMode()));

    /// <summary>How long the pointer or focus rests on a bulb before it is tried on (PRODUCT-SPEC 3.2.4).</summary>
    public static readonly TimeSpan TryOnDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>How long after the pointer and focus left the list the try-on reverts.</summary>
    public static readonly TimeSpan RevertDelay = TimeSpan.FromMilliseconds(150);

    private readonly DispatcherTimer tryOnTimer;
    private readonly DispatcherTimer revertTimer;
    private readonly DispatcherTimer reloadTimer;
    private IReadOnlyList<ShowOption?> options = [];
    private ShowOption currentOption = new(ShowOption.AllKey, BulbFilter.All, "All Bulbs", 0, []);
    private List<BulbItem> items = [];
    private Dictionary<string, BulbItem> byId = new(BulbIds.Comparer);
    private IAppServices? attached;
    private bool updating;
    private bool reloadKeepsScroll = true;
    private bool reloadRequeries;
    private string? tryOnCandidate;
    private string? triedOn;
    private Point? dragStart;
    private BulbItem? dragItem;
    private BulbItem? pendingCollapse;

    /// <summary>Creates the list.</summary>
    public BulbList()
    {
        InitializeComponent();
        tryOnTimer = new DispatcherTimer { Interval = TryOnDelay };
        tryOnTimer.Tick += (_, _) =>
        {
            tryOnTimer.Stop();
            if (tryOnCandidate is { } id)
            {
                TryOnNow(id);
            }
        };
        revertTimer = new DispatcherTimer { Interval = RevertDelay };
        revertTimer.Tick += (_, _) =>
        {
            revertTimer.Stop();
            TryOnNow(null);
        };
        reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        reloadTimer.Tick += (_, _) =>
        {
            reloadTimer.Stop();
            Reload(reloadKeepsScroll, reloadRequeries);
            reloadKeepsScroll = true;
            reloadRequeries = false;
        };
        foreach (ListBox list in new ListBox[] { Tiles, Rows })
        {
            list.IsTextSearchEnabled = false;
            list.ContextMenu = new ContextMenu();
            list.SelectionChanged += OnListSelectionChanged;
            list.PreviewKeyDown += OnListKeyDown;
            list.PreviewTextInput += OnListTextInput;
            list.PreviewMouseMove += OnListMouseMove;
            list.PreviewMouseLeftButtonUp += OnListMouseUp;
            list.ContextMenuOpening += OnListContextMenuOpening;
            list.MouseLeave += (_, _) =>
            {
                if (!list.IsKeyboardFocusWithin)
                {
                    StartRevert();
                }
            };
            list.LostKeyboardFocus += (_, _) =>
            {
                if (!list.IsKeyboardFocusWithin && !list.IsMouseOver)
                {
                    StartRevert();
                }
            };
        }

        TilesButton.IsChecked = true;
        SortBox.SelectedIndex = 0;
        Search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && items.Count > 0)
            {
                FocusList();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    /// <summary>The selection changed (the selected-bulb bar follows it).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>A command for the page: use, add, edit, remove ... (PRODUCT-SPEC 3.2.11).</summary>
    public event EventHandler<BulbActionRequest>? ActionRequested;

    /// <summary>A bulb to try on in the preview, or null to revert (PRODUCT-SPEC 3.2.4).</summary>
    public event EventHandler<string?>? TryOnChanged;

    /// <summary>True when a drag of bulbs from the list starts, false when it ends (the frame editor outlines its boxes).</summary>
    public event EventHandler<bool>? DragActiveChanged;

    /// <summary>Files chosen with "Add Bulb..." or "Import Bulb Files from a Folder..." (an empty list: the folder had none).</summary>
    public event EventHandler<IReadOnlyList<string>>? ImportRequested;

    /// <summary>The services.</summary>
    public IAppServices? Services
    {
        get => (IAppServices?)GetValue(ServicesProperty);
        set => SetValue(ServicesProperty, value);
    }

    /// <summary>The most DIPs per art pixel in tiles: twice the bulb size setting (PRODUCT-SPEC 3.2.5).</summary>
    public double TileArtScale
    {
        get => (double)GetValue(TileArtScaleProperty);
        set => SetValue(TileArtScaleProperty, value);
    }

    /// <summary>The target named above the list and in "Use for ..." (PRODUCT-SPEC 3.2.4).</summary>
    public ArrangementTarget Target
    {
        get => (ArrangementTarget)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>True in the Choose a Bulb dialog: add-on bulbs only, no target line, no adding, no drags, "Choose" instead of "Use".</summary>
    public bool IsChooser
    {
        get => (bool)GetValue(IsChooserProperty);
        set => SetValue(IsChooserProperty, value);
    }

    /// <summary>The selected bulbs in selection order.</summary>
    public IReadOnlyList<BulbItem> SelectedItems => [.. ActiveList.SelectedItems.OfType<BulbItem>()];

    /// <summary>The only selected bulb, or null with none or several.</summary>
    public BulbItem? SelectedItem => ActiveList.SelectedItems.Count == 1 ? ActiveList.SelectedItems[0] as BulbItem : null;

    /// <summary>The bulbs listed.</summary>
    public IReadOnlyList<BulbItem> Items => items;

    /// <summary>The current "Show:" item.</summary>
    public ShowOption CurrentOption => currentOption;

    /// <summary>The visible list (tiles or Details rows).</summary>
    private ListBox ActiveList => DetailsButton.IsChecked == true ? Rows : Tiles;

    private bool IsSearching => Search.Text.Trim().Length > 0;

    /// <summary>Moves focus to the search box (Ctrl+F; Enter on a frame box).</summary>
    public void FocusSearch() => Search.FocusAndSelect();

    /// <summary>Moves focus to the selected bulb, or the first one.</summary>
    public void FocusList()
    {
        ListBox list = ActiveList;
        object? item = list.SelectedItem ?? (items.Count > 0 ? items[0] : null);
        if (item is null)
        {
            list.Focus();
            return;
        }

        list.ScrollIntoView(item);
        list.UpdateLayout();
        if (list.SelectedItem is null && item is BulbItem first)
        {
            SetSelection(list, [first]);
        }

        if (list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            container.Focus();
        }
    }

    /// <summary>
    /// Selects a bulb and scrolls to it ("Find in Bulb List"; a new bulb); Show switches to All Bulbs and the search clears
    /// when needed. A removed (hidden) bundled bulb - its file added again, say - is shown under "Removed Bulbs", selected,
    /// with its Restore button.
    /// </summary>
    /// <param name="bulbId">The bulb.</param>
    /// <returns>True when the bulb is listed (under All Bulbs or Removed Bulbs).</returns>
    public bool Select(string bulbId)
    {
        ArgumentException.ThrowIfNullOrEmpty(bulbId);
        if (!byId.ContainsKey(bulbId) && (IsSearching || currentOption.Key != ShowOption.AllKey))
        {
            updating = true;
            Search.Text = "";
            updating = false;
            SelectOption(ShowOption.AllKey);
        }

        if (!byId.ContainsKey(bulbId) && HasOption(ShowOption.RemovedKey))
        {
            SelectOption(ShowOption.RemovedKey);
            if (!byId.ContainsKey(bulbId))
            {
                SelectOption(ShowOption.AllKey);
            }
        }

        if (!byId.TryGetValue(bulbId, out BulbItem? item))
        {
            return false;
        }

        ListBox list = ActiveList;
        SetSelection(list, [item]);
        list.ScrollIntoView(item);
        return true;
    }

    /// <summary>Selects several bulbs (the GIFs just added under My Bulbs).</summary>
    /// <param name="bulbIds">The bulbs.</param>
    /// <param name="showMyBulbs">True to switch Show to "My Bulbs" first.</param>
    public void SelectMany(IReadOnlyList<string> bulbIds, bool showMyBulbs)
    {
        ArgumentNullException.ThrowIfNull(bulbIds);
        if (showMyBulbs)
        {
            updating = true;
            Search.Text = "";
            updating = false;
            Reload(keepScroll: false);
            SelectOption(ShowOption.MyBulbsKey);
        }

        ListBox list = ActiveList;
        SetSelection(list, [.. bulbIds.Select(id => byId.GetValueOrDefault(id)).OfType<BulbItem>()]);
        if (list.SelectedItems.Count > 0)
        {
            list.ScrollIntoView(list.SelectedItems[0]);
        }
    }

    /// <summary>Rebuilds the "Show:" items (live counts) and, unless told otherwise, the list now (after bulbs were added or removed).</summary>
    /// <param name="keepScroll">True to keep the scroll position and the selection.</param>
    /// <param name="requery">False when only the counts can have changed (the list stays as it is).</param>
    public void Reload(bool keepScroll = true, bool requery = true)
    {
        IAppServices? services = attached;
        if (services is null)
        {
            return;
        }

        AppSettings settings = services.Settings.Current;
        TileArtScale = 2 * ArtScale.Factor(settings.Lights.Size);
        IReadOnlySet<string> inUse = BulbShowOptions.InUse(settings.Current.Arrangement);
        (string Name, IReadOnlyList<string> Categories)? holiday = null;
        CalendarResolution today = services.Calendar.Resolve(settings.Calendar, DateOnly.FromDateTime(DateTime.Now));
        if (today.EntryId is { } entry && services.Calendar.GetBulbCategories(entry) is { Count: > 0 } categories)
        {
            holiday = (services.Calendar.GetDisplayName(entry), categories);
        }

        options = BulbShowOptions.Build(services.Bulbs, inUse, holiday, IsChooser);
        updating = true;
        try
        {
            ShowBox.Items.Clear();
            ComboBoxItem? selected = null;
            foreach (ShowOption? option in options)
            {
                if (option is null)
                {
                    ShowBox.Items.Add(new ComboBoxItem { Content = new Separator { Margin = new Thickness(0) }, IsEnabled = false, Focusable = false, Padding = new Thickness(4, 2, 4, 2) });
                    continue;
                }

                var item = new ComboBoxItem { Content = option.Text, Tag = option };
                ShowBox.Items.Add(item);
                if (option.Key == currentOption.Key)
                {
                    selected = item;
                }
            }

            selected ??= (ComboBoxItem)ShowBox.Items[0];
            ShowBox.SelectedItem = selected;
            currentOption = (ShowOption)selected.Tag;
            int total = options.FirstOrDefault(o => o?.Key == ShowOption.AllKey)?.Count ?? 0;
            Search.Placeholder = BulbShowOptions.Placeholder(total);
        }
        finally
        {
            updating = false;
        }

        if (requery)
        {
            Requery(keepScroll);
        }
        else
        {
            UpdateResultLine();
        }
    }

    /// <summary>Ends any try-on at once (a bulb was used, or the page is hidden).</summary>
    public void ClearTryOn()
    {
        tryOnTimer.Stop();
        revertTimer.Stop();
        tryOnCandidate = null;
        TryOnNow(null);
    }

    private void Attach()
    {
        IAppServices? services = Services;
        if (!IsLoaded || ReferenceEquals(services, attached))
        {
            return;
        }

        Detach();
        if (services is null)
        {
            return;
        }

        attached = services;
        services.Bulbs.Changed += OnCatalogChanged;
        services.Settings.Changed += OnSettingsChanged;
        GallerySettings gallery = services.Settings.Current.Ui.Gallery;
        updating = true;
        (gallery.View == BulbListView.Details ? DetailsButton : TilesButton).IsChecked = true;
        SortBox.SelectedIndex = (int)gallery.Sort;
        updating = false;
        ApplyView();
        UpdateTargetLine();
        UpdateChooserMode();
        Reload(keepScroll: false);
    }

    private void Detach()
    {
        if (attached is null)
        {
            return;
        }

        attached.Bulbs.Changed -= OnCatalogChanged;
        attached.Settings.Changed -= OnSettingsChanged;
        attached = null;
        reloadTimer.Stop();
        ClearTryOn();
    }

    private void OnCatalogChanged(object? sender, BulbCatalogChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => ScheduleReload(requery: true), DispatcherPriority.Background);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        AppSettings before = e.OldSettings;
        AppSettings after = e.NewSettings;
        bool favorites = !before.Bulbs.Favorites.SequenceEqual(after.Bulbs.Favorites, BulbIds.Comparer);
        bool library = !Equals(before.Bulbs with { Favorites = after.Bulbs.Favorites }, after.Bulbs);
        bool arrangement = !Equals(before.Current.Arrangement, after.Current.Arrangement);
        bool calendar = !Equals(before.Calendar, after.Calendar);
        if (before.Lights.Size != after.Lights.Size)
        {
            TileArtScale = 2 * ArtScale.Factor(after.Lights.Size);
        }

        if (arrangement)
        {
            IReadOnlySet<string> inUse = BulbShowOptions.InUse(after.Current.Arrangement);
            foreach (BulbItem item in items)
            {
                item.IsInUse = inUse.Contains(item.Id);
            }

            UpdateTargetLine();
        }

        if (favorites)
        {
            var favorite = new HashSet<string>(after.Bulbs.Favorites, BulbIds.Comparer);
            foreach (BulbItem item in items)
            {
                item.IsFavorite = favorite.Contains(item.Id);
            }
        }

        if (favorites || library || arrangement || calendar)
        {
            // The list itself changes only when its own filter is affected; otherwise only the Show counts do.
            BulbFilter filter = currentOption.Filter;
            ScheduleReload(library
                || (favorites && filter == BulbFilter.Favorites)
                || (arrangement && filter == BulbFilter.InUse)
                || (calendar && filter == BulbFilter.Holiday));
        }
    }

    private void ScheduleReload(bool requery)
    {
        reloadRequeries |= requery;
        reloadTimer.Stop();
        reloadTimer.Start();
    }

    /// <summary>Runs the query of the current Show item, search and sort; keeps the selection (and the scroll position).</summary>
    private void Requery(bool keepScroll)
    {
        IAppServices? services = attached;
        if (services is null)
        {
            return;
        }

        ListBox list = ActiveList;
        List<string> selectedIds = [.. list.SelectedItems.OfType<BulbItem>().Select(i => i.Id)];
        ScrollViewer? scroller = FindScrollViewer(list);
        double offset = keepScroll ? scroller?.VerticalOffset ?? 0 : 0;
        AppSettings settings = services.Settings.Current;
        IReadOnlySet<string> inUse = BulbShowOptions.InUse(settings.Current.Arrangement);
        BulbSortOrder sort = (BulbSortOrder)Math.Clamp(SortBox.SelectedIndex, 0, 2);
        if (IsSearching)
        {
            sort = settings.Ui.Gallery.Sort;
        }

        IReadOnlyList<BulbInfo> found = services.Bulbs.Query(currentOption.ToQuery(Search.Text, sort, inUse, IsChooser));
        DateTimeOffset now = DateTimeOffset.Now;
        bool removed = currentOption.Filter == BulbFilter.Removed;
        var favorites = new HashSet<string>(settings.Bulbs.Favorites, BulbIds.Comparer);
        items = [.. found.Select(info => new BulbItem(info, favorites.Contains(info.Id), inUse.Contains(info.Id), removed, now))];
        byId = new Dictionary<string, BulbItem>(BulbIds.Comparer);
        foreach (BulbItem item in items)
        {
            byId.TryAdd(item.Id, item);
        }

        List<object> shown = [.. items];
        shown.AddRange(Enumerable.Range(0, PlaceholderCount(services.Bulbs.Status)).Select(_ => new SkeletonItem()));
        updating = true;
        try
        {
            Tiles.ItemsSource = shown;
            Rows.ItemsSource = shown;
            SetSelection(list, [.. selectedIds.Select(id => byId.GetValueOrDefault(id)).OfType<BulbItem>()]);
        }
        finally
        {
            updating = false;
        }

        if (scroller is not null)
        {
            Dispatcher.BeginInvoke(() => scroller.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        }

        UpdateResultLine();
        if (selectedIds.Count != list.SelectedItems.Count)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>How many skeleton tiles follow the bulbs listed so far while add-ons are still being indexed.</summary>
    private int PlaceholderCount(BulbCatalogStatus status) =>
        status.IsComplete || currentOption.Filter is BulbFilter.BuiltIn or BulbFilter.Removed or BulbFilter.Favorites or BulbFilter.InUse
            ? 0
            : Math.Clamp(status.TotalAddOns - status.IndexedAddOns, 0, SkeletonItem.MaxShown);

    private void UpdateResultLine()
    {
        if (attached is null)
        {
            return;
        }

        BulbCatalogStatus status = attached.Bulbs.Status;
        ResultLine.Text = BulbShowOptions.ResultLine(items.Count, Search.Text, IsSearching ? status with { IsComplete = true } : status);
        bool empty = items.Count == 0 && PlaceholderCount(status) == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = BulbShowOptions.EmptyText(currentOption, Search.Text);
        ClearSearchButton.Visibility = IsSearching ? Visibility.Visible : Visibility.Collapsed;
        ShowAllButton.Visibility = currentOption.Key != ShowOption.AllKey ? Visibility.Visible : Visibility.Collapsed;
        RestoreAllButton.Visibility = currentOption.Filter == BulbFilter.Removed && items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BestMatchItem.Visibility = IsSearching ? Visibility.Visible : Visibility.Collapsed;
        updating = true;
        SortBox.SelectedItem = IsSearching ? BestMatchItem : SortBox.Items[(int)(attached.Settings.Current.Ui.Gallery.Sort)];
        SortBox.IsEnabled = !IsSearching;
        updating = false;
    }

    private void UpdateTargetLine()
    {
        SlotAssignment arrangement = attached?.Settings.Current.Current.Arrangement ?? SlotAssignment.Empty;
        TargetText.Text = Target.Sentence(arrangement);
    }

    private void UpdateChooserMode()
    {
        Visibility visibility = IsChooser ? Visibility.Collapsed : Visibility.Visible;
        Tiles.SelectionMode = IsChooser ? SelectionMode.Single : SelectionMode.Extended;
        Rows.SelectionMode = Tiles.SelectionMode;
        TargetLine.Visibility = visibility;
        AddBulbButton.Visibility = visibility;
        MoreButton.Visibility = visibility;
    }

    private void SelectOption(string key)
    {
        foreach (object entry in ShowBox.Items)
        {
            if (entry is ComboBoxItem { Tag: ShowOption option } item && option.Key == key)
            {
                ShowBox.SelectedItem = item;
                return;
            }
        }
    }

    private bool HasOption(string key) => ShowBox.Items.OfType<ComboBoxItem>().Any(i => i.Tag is ShowOption option && option.Key == key);

    private void OnShowChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || ShowBox.SelectedItem is not ComboBoxItem { Tag: ShowOption option })
        {
            return;
        }

        currentOption = option;
        Requery(keepScroll: false);
    }

    private void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || attached is null || SortBox.SelectedIndex is < 0 or > 2)
        {
            return;
        }

        var sort = (BulbSortOrder)SortBox.SelectedIndex;
        attached.Settings.Update(s => s with { Ui = s.Ui with { Gallery = s.Ui.Gallery with { Sort = sort } } }, SettingsChange.Internal);
        Requery(keepScroll: false);
    }

    private void OnSearchChanged(object? sender, EventArgs e)
    {
        if (!updating)
        {
            Requery(keepScroll: false);
        }
    }

    private void OnViewChecked(object sender, RoutedEventArgs e)
    {
        if (updating)
        {
            return;
        }

        ListBox before = ReferenceEquals(sender, TilesButton) ? Rows : Tiles;
        List<BulbItem> selection = [.. before.SelectedItems.OfType<BulbItem>()];
        ApplyView();
        ListBox after = ActiveList;
        updating = true;
        SetSelection(after, selection);
        updating = false;
        if (selection.Count > 0)
        {
            after.ScrollIntoView(selection[0]);
        }

        BulbListView view = DetailsButton.IsChecked == true ? BulbListView.Details : BulbListView.Tiles;
        attached?.Settings.Update(s => s with { Ui = s.Ui with { Gallery = s.Ui.Gallery with { View = view } } }, SettingsChange.Internal);
    }

    private void ApplyView()
    {
        bool details = DetailsButton.IsChecked == true;
        Tiles.Visibility = details ? Visibility.Collapsed : Visibility.Visible;
        Rows.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e) => Search.Clear();

    private void OnShowAllClick(object sender, RoutedEventArgs e) => SelectOption(ShowOption.AllKey);

    private void OnAddBulbClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add Bulb",
            Filter = "Bulb Files and GIF Files (*.bul; *.gif)|*.bul;*.gif",
            Multiselect = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            ImportRequested?.Invoke(this, dialog.FileNames);
        }
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || !ReferenceEquals(sender, ActiveList))
        {
            return;
        }

        DeselectPlaceholders((ListBox)sender, e.AddedItems);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (SelectedItem is { } item && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            TryOnNow(item.Id);
        }
    }

    /// <summary>Select All and Shift+click ranges reach the skeleton tiles too; they are never selected.</summary>
    private void DeselectPlaceholders(ListBox list, IList added)
    {
        SkeletonItem[] placeholders = [.. added.OfType<SkeletonItem>()];
        if (placeholders.Length == 0)
        {
            return;
        }

        updating = true;
        try
        {
            foreach (SkeletonItem placeholder in placeholders)
            {
                list.SelectedItems.Remove(placeholder);
            }
        }
        finally
        {
            updating = false;
        }
    }

    private void OnItemMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: BulbItem item })
        {
            Candidate(item.Id);
        }
    }

    private void OnItemMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: BulbItem item } && tryOnCandidate == item.Id)
        {
            tryOnTimer.Stop();
            tryOnCandidate = null;
        }
    }

    private void OnItemGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: BulbItem item } && ReferenceEquals(e.NewFocus, sender))
        {
            Candidate(item.Id);
        }
    }

    private void Candidate(string id)
    {
        if (IsChooser)
        {
            return;
        }

        revertTimer.Stop();
        tryOnCandidate = id;
        tryOnTimer.Stop();
        tryOnTimer.Start();
    }

    private void StartRevert()
    {
        tryOnTimer.Stop();
        tryOnCandidate = null;
        if (triedOn is not null)
        {
            revertTimer.Stop();
            revertTimer.Start();
        }
    }

    private void TryOnNow(string? id)
    {
        revertTimer.Stop();
        if (IsChooser || triedOn == id)
        {
            return;
        }

        triedOn = id;
        TryOnChanged?.Invoke(this, id);
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not ListBoxItem { DataContext: BulbItem item } || ActiveList.SelectedItems.Count > 1)
        {
            return;
        }

        e.Handled = true;
        Request(BulbAction.Use, [item.Id]);
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: BulbItem item } container)
        {
            return;
        }

        dragStart = e.GetPosition(this);
        dragItem = item;
        pendingCollapse = null;
        if (container.IsSelected && ActiveList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && e.ClickCount == 1)
        {
            pendingCollapse = item;
            container.Focus();
            e.Handled = true;
        }
    }

    private void OnListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (pendingCollapse is { } item)
        {
            SetSelection(ActiveList, [item]);
        }

        pendingCollapse = null;
        dragStart = null;
        dragItem = null;
    }

    /// <summary>Drags the selected bulbs (or the one under the pointer) after the system drag distance.</summary>
    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (dragStart is not { } start || dragItem is not { } item || e.LeftButton != MouseButtonState.Pressed || attached is null || IsChooser)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                dragStart = null;
                dragItem = null;
            }

            return;
        }

        if (!BulbDrag.IsDragDistance(start, e.GetPosition(this)))
        {
            return;
        }

        dragStart = null;
        dragItem = null;
        pendingCollapse = null;
        IReadOnlyList<string> ids = ActiveList.SelectedItems.Contains(item) ? [.. SelectedItems.Select(i => i.Id)] : [item.Id];
        tryOnTimer.Stop();
        DragActiveChanged?.Invoke(this, true);
        try
        {
            BulbDrag.Run(ActiveList, new BulbDragData(ids, null), attached, null);
        }
        finally
        {
            DragActiveChanged?.Invoke(this, false);
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        IReadOnlyList<BulbItem> selection = SelectedItems;
        switch (e.Key)
        {
            case Key.Enter when modifiers == ModifierKeys.None:
                if (selection.Count <= 1 && ((Keyboard.FocusedElement as ListBoxItem)?.DataContext as BulbItem ?? SelectedItem) is { } focused)
                {
                    Request(BulbAction.Use, [focused.Id]);
                }

                e.Handled = true;
                break;
            case Key.Enter when modifiers == ModifierKeys.Shift:
                if (selection.Count > 0 && !IsChooser)
                {
                    Request(BulbAction.AddToEveryEdge, [.. selection.Select(i => i.Id)]);
                }

                e.Handled = true;
                break;
            case Key.D when modifiers == ModifierKeys.Control:
                ToggleFavorites(selection);
                e.Handled = true;
                break;
            case Key.F when modifiers == ModifierKeys.Control:
                Search.FocusAndSelect();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Printable characters typed in the grid start a search (PRODUCT-SPEC 3.2.5).</summary>
    private void OnListTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text == " " || e.Text.Any(char.IsControl) || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            return;
        }

        Search.StartWith(e.Text);
        e.Handled = true;
    }

    private void OnStarClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BulbItem item })
        {
            attached?.Bulbs.SetFavorite(item.Id, !item.IsFavorite);
        }
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BulbItem item })
        {
            attached?.Bulbs.Unhide(item.Id);
        }
    }

    /// <summary>"Restore All" under "Removed Bulbs": every removed bundled bulb is listed again (one undo step).</summary>
    private void OnRestoreAllClick(object sender, RoutedEventArgs e)
    {
        if (attached is not { } services || items.Count == 0)
        {
            return;
        }

        string[] ids = [.. items.Select(i => i.Id)];
        using (UndoScope.Of(this, services).BeginGroup(ids.Length == 1 ? $"Restore {items[0].Name}" : $"Restore {ids.Length} bulbs"))
        {
            foreach (string id in ids)
            {
                services.Bulbs.Unhide(id);
            }
        }
    }

    /// <summary>Ctrl+D: favorites every selected bulb, or (when all are favorites) removes them all.</summary>
    private void ToggleFavorites(IReadOnlyList<BulbItem> selection)
    {
        if (attached is null || selection.Count == 0)
        {
            return;
        }

        bool favorite = selection.Any(i => !i.IsFavorite);
        if (selection.Count == 1)
        {
            attached.Bulbs.SetFavorite(selection[0].Id, favorite);
            return;
        }

        string description = favorite
            ? $"Add {selection.Count} bulbs to Favorites"
            : $"Remove {selection.Count} bulbs from Favorites";
        using (UndoScope.Of(this, attached).BeginGroup(description))
        {
            foreach (BulbItem item in selection)
            {
                attached.Bulbs.SetFavorite(item.Id, favorite);
            }
        }
    }

    private void Request(BulbAction action, IReadOnlyList<string> ids, Side side = Side.Top, Corner corner = Corner.TopLeft)
    {
        ClearTryOn();
        ActionRequested?.Invoke(this, new BulbActionRequest(action, ids, side, corner));
    }

    /// <summary>Selects exactly these bulbs (in order) in either selection mode.</summary>
    private static void SetSelection(ListBox list, IReadOnlyList<BulbItem> selection)
    {
        if (list.SelectionMode == SelectionMode.Single)
        {
            list.SelectedItem = selection.Count > 0 ? selection[0] : null;
            return;
        }

        list.SelectedItems.Clear();
        foreach (BulbItem item in selection)
        {
            list.SelectedItems.Add(item);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (attached is not { } services)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(MenuItem("Open My _Bulbs Folder", true, () =>
        {
            Directory.CreateDirectory(services.Paths.MyBulbsFolder);
            services.Shell.OpenFolder(services.Paths.MyBulbsFolder);
        }));
        menu.Items.Add(MenuItem("_Import Bulb Files from a Folder…", true, ImportFolder));
        menu.Items.Add(MenuItem("Show _Removed Bulbs", options.Any(o => o?.Key == ShowOption.RemovedKey), () => SelectOption(ShowOption.RemovedKey)));
        if (services.Bulbs.DamagedFiles.Count > 0)
        {
            menu.Items.Add(MenuItem("Show _Damaged Bulbs", true, ShowDamaged));
        }

        menu.IsOpen = true;
    }

    private void ImportFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Import Bulb Files from a Folder" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        string[] files;
        try
        {
            files = [.. Directory.EnumerateFiles(dialog.FolderName, "*.bul").Order(StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder's path names the account and the user's own folders: never logged (CONTRACTS 4; review r1 #75).
            attached?.Log.Warn("BulbList", "Could not list the bulb files of the chosen folder.", ex);
            files = [];
        }

        ImportRequested?.Invoke(this, files);
    }

    /// <summary>"Show Damaged Bulbs": the files of My Bulbs that could not be read, each with "Show in Folder".</summary>
    private void ShowDamaged()
    {
        if (attached is not { } services)
        {
            return;
        }

        var panel = new StackPanel { MaxWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = "These files in My Bulbs couldn't be read. They may be damaged. They are not deleted.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        foreach (DamagedBulbFile file in services.Bulbs.DamagedFiles)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var show = new Button { Content = "Show in Folder", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 3) };
            show.Click += (_, _) => services.Shell.ShowInFolder(file.FilePath);
            DockPanel.SetDock(show, Dock.Right);
            row.Children.Add(show);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = Path.GetFileName(file.FilePath), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var reason = new TextBlock { Text = file.Reason, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            reason.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            text.Children.Add(reason);
            row.Children.Add(text);
            panel.Children.Add(row);
        }

        Flyout.Show(MoreButton, panel, PlacementMode.Bottom, "Damaged Bulb Files");
    }

    private static MenuItem MenuItem(string header, bool enabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }
}
