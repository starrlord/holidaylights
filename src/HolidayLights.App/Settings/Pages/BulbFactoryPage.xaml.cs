using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HolidayLights.App.Controls;
using HolidayLights.App.Gallery;

namespace HolidayLights.App.Settings.Pages;

/// <summary>
/// The "Bulb Factory" page of the Settings window (owner: settings-ui; PRODUCT-SPEC 3.2): the frame editor with 8 boxes
/// around an exact live preview, targets and try-on, the Bulb List with its selected-bulb bar, drag and drop, adding
/// bulb files and GIFs, Flash Settings and Bulb Drawing.
/// </summary>
public partial class BulbFactoryPage : UserControl, ISettingsPage, IDisposable
{
    /// <summary>Below this content width the page uses the narrow, single-column layout.</summary>
    public const double WideLayoutMinimum = 900;

    /// <summary>The narrowest Bulb List column.</summary>
    public const double ListMinimumWidth = 340;

    /// <summary>Flash Settings and Bulb Drawing stack below this left-column width.</summary>
    private const double GroupsSideBySideMinimum = 640;

    /// <summary>The Bulb List height in the narrow layout.</summary>
    private const double NarrowListHeight = 560;

    /// <summary>Below this Bulb List height the selected-bulb bar folds its credits line away (the tiles keep their room).</summary>
    private const double CompactBarBelow = 700;

    private readonly StackPanel headerActions;
    private ISettingsHost? host;
    private bool wide = true;
    private bool disposed;
    private bool softwareDrawingDismissed;
    private IReadOnlyList<string> damagedShown = [];

    /// <summary>Creates the page.</summary>
    /// <param name="services">The application services.</param>
    public BulbFactoryPage(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        InitializeComponent();
        headerActions = BuildHeaderActions();
        Editor.Services = services;
        List.Services = services;
        Bar.Services = services;
        Bar.List = List;
        FlashGroup.Services = services;
        DrawingGroup.Services = services;
        Editor.Arrangement = services.Settings.Current.Current.Arrangement;
        Editor.EditRequested += (_, edit) => Apply(edit);
        Editor.TargetChanged += (_, _) => OnTargetChanged();
        Editor.PickBulbRequested += (_, _) => List.FocusSearch();
        Editor.PasteRequested += (_, box) => Paste(box);
        Editor.BulbCommandRequested += (_, request) => OnBulbCommand(request);
        Editor.FilesDropped += (_, request) => _ = ImportAsync(request.Paths, request.Destination);
        List.SelectionChanged += (_, _) => UpdateBar();
        List.ActionRequested += (_, request) => Run(request);
        List.TryOnChanged += (_, id) => ShowTryOn(id);
        List.DragActiveChanged += (_, active) => Editor.ShowDropTargets(active);
        List.ImportRequested += (_, paths) => _ = ImportAsync(paths, null, bulbsOnly: true);
        Bar.ActionRequested += (_, request) => Run(request);
        MissingBar.ActionCommand = new DelegateCommand(RemoveMissing);
        AutomaticBar.ActionCommand = new DelegateCommand(SaveTheme);
        AutomaticBar.SecondaryActionCommand = new DelegateCommand(TurnOffAutomaticThemes);
        DamagedBar.ActionCommand = new DelegateCommand(ShowDamagedInFolder);
        InputBindings.Add(new KeyBinding(new DelegateCommand(List.FocusSearch), Key.F, ModifierKeys.Control));
        SizeChanged += (_, _) => UpdateLayoutMode();
        ListPane.SizeChanged += (_, _) => Bar.IsCompact = ListPane.ActualHeight < CompactBarBelow;
        services.Settings.Changed += OnSettingsChanged;
        services.Bulbs.Changed += OnCatalogChanged;
        services.Lights.StatusChanged += OnLightsStatusChanged;
        SoftwareDrawingBar.Closed += (_, _) => softwareDrawingDismissed = true;
        OnTargetChanged();
        UpdateBar();
    }

    /// <summary>The application services.</summary>
    public IAppServices Services { get; }

    /// <inheritdoc />
    public event EventHandler? HeaderChanged;

    /// <inheritdoc />
    public SettingsPageId PageId => SettingsPageId.BulbFactory;

    /// <inheritdoc />
    public string Title => "Bulb Factory";

    /// <inheritdoc />
    public string Description => "Drag bulbs into the boxes, or double-click one. Bulbs repeat along each edge in the order shown.";

    /// <inheritdoc />
    public FrameworkElement? HeaderActions => headerActions;

    /// <inheritdoc />
    public bool ScrollsAsWhole => false;

    /// <summary>The arrangement in the settings.</summary>
    private SlotAssignment Arrangement => Services.Settings.Current.Current.Arrangement;

    /// <inheritdoc />
    public void Attach(ISettingsHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void OnShown()
    {
        Editor.Target = ArrangementTarget.WholeFrame;
        UpdateMissing();
        UpdateDamaged();
        UpdateSoftwareDrawing();
    }

    /// <inheritdoc />
    public void OnHidden()
    {
        List.ClearTryOn();
        Editor.ShowTryOn(null, null);
    }

    /// <inheritdoc />
    public void HandleRequest(SettingsRequest request)
    {
        if (request is OpenFilesRequest files)
        {
            _ = ImportAsync(files.Paths, null);
        }
    }

    /// <inheritdoc />
    public string HelpTopicFor(DependencyObject? focused)
    {
        foreach (DependencyObject node in Controls.ElementTree.SelfAndAncestors(focused))
        {
            if (ReferenceEquals(node, FlashGroup))
            {
                return HelpTopics.FlashPattern;
            }

            if (ReferenceEquals(node, DrawingGroup))
            {
                return HelpTopics.DesktopOrOnTop;
            }

            if (ReferenceEquals(node, ListPane))
            {
                return HelpTopics.FindingBulbs;
            }
        }

        return HelpTopics.ChangingBulbs;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Services.Settings.Changed -= OnSettingsChanged;
        Services.Bulbs.Changed -= OnCatalogChanged;
        Services.Lights.StatusChanged -= OnLightsStatusChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>Previews a display in the frame editor (Home: a click on that display).</summary>
    /// <param name="displayId">A <see cref="DisplayInfo.DeviceId"/>.</param>
    public void PreviewDisplay(string displayId) => Editor.ShowDisplay(displayId);

    /// <summary>The bulb name of an id (the file stem when the bulb is missing).</summary>
    private string NameOf(string bulbId) => Editor.NameOf(bulbId);

    private StackPanel BuildHeaderActions()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new PeekButton { Margin = new Thickness(0, 0, 8, 0) });
        var clear = new Button { Content = "Clear All _Bulbs" };
        ButtonContent.SetGlyph(clear, Glyphs.Delete);
        clear.SetResourceReference(ContentTemplateProperty, "HL.Template.GlyphLabel");
        clear.SetResourceReference(ToolTipProperty, "HL.Tip.BulbFactory.ClearAllBulbs");
        System.Windows.Automation.AutomationProperties.SetName(clear, "Clear All Bulbs");
        clear.Click += (_, _) => Apply(ArrangementEdits.ClearAll(Arrangement));
        panel.Children.Add(clear);
        return panel;
    }

    /// <summary>Wide layout: left column clamp(width - 16 - 360, 540, 760), the Bulb List the rest; narrow: one scrolling column.</summary>
    private void UpdateLayoutMode()
    {
        double width = ActualWidth;
        bool shouldBeWide = width >= WideLayoutMinimum;
        if (shouldBeWide != wide)
        {
            wide = shouldBeWide;
            LeftStack.Children.Remove(ListPane);
            LayoutRoot.Children.Remove(ListPane);
            if (wide)
            {
                ListPane.Height = double.NaN;
                ListPane.Margin = new Thickness(0, 0, 0, 12);
                Grid.SetColumn(ListPane, 2);
                LayoutRoot.Children.Add(ListPane);
            }
            else
            {
                ListPane.Height = NarrowListHeight;
                ListPane.Margin = new Thickness(0, 12, 0, 0);
                Grid.SetColumn(ListPane, 0);
                LeftStack.Children.Insert(LeftStack.Children.IndexOf(QuickTargets) + 1, ListPane);
            }
        }

        if (wide)
        {
            double left = Math.Clamp(width - 16 - 360, 540, 760);
            left = Math.Min(left, Math.Max(0, width - 16 - ListMinimumWidth));
            LeftColumn.Width = new GridLength(left);
            GapColumn.Width = new GridLength(16);
            RightColumn.Width = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            LeftColumn.Width = new GridLength(1, GridUnitType.Star);
            GapColumn.Width = new GridLength(0);
            RightColumn.Width = new GridLength(0);
        }

        bool sideBySide = (wide ? LeftColumn.Width.Value : width) >= GroupsSideBySideMinimum;
        FlashColumn.Width = new GridLength(1, GridUnitType.Star);
        GroupGapColumn.Width = new GridLength(sideBySide ? 12 : 0);
        DrawingColumn.Width = sideBySide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        GroupGapRow.Height = new GridLength(sideBySide ? 0 : 12);
        Grid.SetColumn(DrawingGroup, sideBySide ? 2 : 0);
        Grid.SetRow(DrawingGroup, sideBySide ? 0 : 2);
    }

    private void OnTargetChanged()
    {
        ArrangementTarget target = Editor.Target;
        List.Target = target;
        Bar.Target = target;
        WholeFrameButton.IsChecked = target.Kind == ArrangementTargetKind.WholeFrame;
        AllEdgesButton.IsChecked = target.Kind == ArrangementTargetKind.AllEdges;
        AllCornersButton.IsChecked = target.Kind == ArrangementTargetKind.AllCorners;
    }

    private void OnQuickTargetClick(object sender, RoutedEventArgs e)
    {
        Editor.Target = QuickTargetOf(sender);
        OnTargetChanged();
    }

    /// <summary>
    /// Checked/Unchecked of a segment (UI Automation's Toggle raises no Click; review r1 #34): checking one chooses its
    /// target; the chosen segment stays checked.
    /// </summary>
    private void OnQuickTargetToggled(object sender, RoutedEventArgs e)
    {
        var segment = (System.Windows.Controls.Primitives.ToggleButton)sender;
        ArrangementTarget target = QuickTargetOf(sender);
        bool chosen = Editor.Target.Kind == target.Kind;
        if (segment.IsChecked == true && !chosen)
        {
            Editor.Target = target;
            OnTargetChanged();
        }
        else if (segment.IsChecked != true && chosen)
        {
            segment.IsChecked = true;
        }
    }

    private ArrangementTarget QuickTargetOf(object sender) =>
        ReferenceEquals(sender, AllEdgesButton) ? ArrangementTarget.AllEdges
        : ReferenceEquals(sender, AllCornersButton) ? ArrangementTarget.AllCorners
        : ArrangementTarget.WholeFrame;

    private void UpdateBar() => Bar.Show(List.SelectedItems, Arrangement);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!Equals(e.OldSettings.Current.Arrangement, e.NewSettings.Current.Arrangement))
        {
            Editor.Arrangement = e.NewSettings.Current.Arrangement;
            UpdateMissing();
            UpdateBar();
            OnTargetChanged();
        }
        else if (!Equals(e.OldSettings.Bulbs, e.NewSettings.Bulbs))
        {
            UpdateBar();
        }

        if (e.OldSettings.Calendar.Enabled && !e.NewSettings.Calendar.Enabled)
        {
            AutomaticBar.IsOpen = false;
        }
    }

    private void OnCatalogChanged(object? sender, BulbCatalogChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            UpdateMissing();
            UpdateDamaged();
            Editor.Arrangement = Arrangement;
        }
    });

    /// <summary>"2 bulbs in your arrangement are missing." [Remove Missing Bulbs] (PRODUCT-SPEC 3.2.9).</summary>
    private void UpdateMissing()
    {
        int missing = BulbShowOptions.InUse(Arrangement).Count(id => !Services.Bulbs.TryGetBulb(id, out _));
        if (missing == 0 || !Services.Bulbs.Status.IsComplete)
        {
            MissingBar.IsOpen = false;
            return;
        }

        MissingBar.Message = missing == 1
            ? "1 bulb in your arrangement is missing."
            : string.Create(CultureInfo.CurrentCulture, $"{missing} bulbs in your arrangement are missing.");
        MissingBar.IsOpen = true;
    }

    /// <summary>"1 bulb file couldn't be read: &lt;file&gt;. It may be damaged." [Show in Folder] (PRODUCT-SPEC 3.2.9).</summary>
    private void UpdateDamaged()
    {
        IReadOnlyList<DamagedBulbFile> damaged = Services.Bulbs.DamagedFiles;
        string[] files = [.. damaged.Select(d => d.FilePath)];
        if (files.SequenceEqual(damagedShown, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        damagedShown = files;
        if (files.Length == 0)
        {
            DamagedBar.IsOpen = false;
            return;
        }

        string names = string.Join(", ", files.Select(System.IO.Path.GetFileName));
        DamagedBar.Message = files.Length == 1
            ? $"1 bulb file couldn't be read: {names}. It may be damaged."
            : string.Create(CultureInfo.CurrentCulture, $"{files.Length} bulb files couldn't be read: {names}. They may be damaged.");
        DamagedBar.IsOpen = true;
    }

    private void OnLightsStatusChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            UpdateSoftwareDrawing();
        }
    });

    /// <summary>"Holiday Lights had trouble drawing the lights and switched to software drawing." while the lights use WARP (Appendix D), until closed.</summary>
    private void UpdateSoftwareDrawing() =>
        SoftwareDrawingBar.IsOpen = !softwareDrawingDismissed && Services.Lights.Status.Health == LightsHealth.SoftwareRendering;

    private void ShowDamagedInFolder()
    {
        if (damagedShown.Count > 0)
        {
            Services.Shell.ShowInFolder(damagedShown[0]);
        }
    }

    private void RemoveMissing()
    {
        Apply(ArrangementEdits.RemoveMissing(Arrangement, id => Services.Bulbs.TryGetBulb(id, out _)));
        MissingBar.IsOpen = false;
    }

    private void TurnOffAutomaticThemes()
    {
        Services.Settings.Update(s => s with { Calendar = s.Calendar with { Enabled = false } }, SettingsChange.Edit("Turn off automatic themes"));
        AutomaticBar.IsOpen = false;
        host?.Announce("Automatic themes are off.");
    }

    private void OnLoadThemeClick(object sender, RoutedEventArgs e) => host?.Navigate(SettingsPageId.Themes);

    private void OnSaveThemeClick(object sender, RoutedEventArgs e) => SaveTheme();

    private void SaveTheme()
    {
        if (host is not null)
        {
            Dialogs.SaveThemeDialog.Show(host, Services);
        }
    }

    /// <summary>Shows a sentence in the page's message InfoBar.</summary>
    private void ShowMessage(string text, InfoBarSeverity severity)
    {
        MessageBar.Severity = severity;
        MessageBar.Message = text;
        MessageBar.IsOpen = true;
    }
}
