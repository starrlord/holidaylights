using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using HolidayLights.App.Controls;
using HolidayLights.App.Settings.Pages;
using HolidayLights.App.Settings.Undo;

namespace HolidayLights.App.Settings;

/// <summary>
/// The Settings window (owner: settings-ui; PRODUCT-SPEC 2.3): left navigation with one page per spec page, the string
/// of lights, the bottom bar and the apply model (live apply, Cancel snapshot, Undo/Redo, holding-folder commit on close).
/// </summary>
public partial class SettingsWindow : Window, ISettingsHost
{
    /// <summary>Below this window width the navigation collapses to a 48 DIP rail.</summary>
    public const double CompactWidth = 1200;

    private const double PaneWidth = 220;
    private const double RailWidth = 48;
    private const string LogSource = "Settings.Window";

    private readonly IAppServices services;
    private readonly Dictionary<SettingsPageId, (ISettingsPage Page, FrameworkElement Host)> pages = [];
    private ISettingsPage? current;
    private bool compact;
    private bool paneOverlayOpen;
    private bool cancelled;
    private bool exiting;

    /// <summary>Creates the window; it shows Home unless <see cref="ShowPage"/> chose another page before it loads.</summary>
    /// <param name="services">The application services.</param>
    public SettingsWindow(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
        Session = new SettingsSession(services);
        InitializeComponent();
        Peek = new Peek(this);
        StringOfLights.Services = services;
        UndoButton.Command = new DelegateCommand(UndoLast);
        Session.History.Changed += (_, _) => UpdateUndoRedo();
        services.Settings.Changed += OnSettingsChanged;
        services.Settings.SaveFailed += OnSaveFailed;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        StringOfLights.HasBulbsChanged += (_, _) => UpdateStringOfLights();
        PreviewKeyDown += OnWindowPreviewKeyDown;
        AllowDrop = true;
        DragOver += OnWindowDragOver;
        Drop += OnWindowDrop;
        _ = new FileDropFeedback(this, services.Log, DroppedFiles.CanAddAny);
        SizeChanged += (_, _) => UpdatePaneMode();
        ContentLayer.PreviewMouseDown += (_, _) => ClosePaneOverlay();
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        UpdateUndoRedo();
        UpdateStringOfLights();
        Loaded += (_, _) =>
        {
            if (Navigation.SelectedIndex < 0)
            {
                ShowPage(SettingsPageId.Home);
            }
        };
    }

    /// <summary>This opening's snapshot and history.</summary>
    public SettingsSession Session { get; }

    /// <inheritdoc />
    public Peek Peek { get; }

    /// <summary>The page on display.</summary>
    public SettingsPageId CurrentPage => current?.PageId ?? SettingsPageId.Home;

    /// <inheritdoc />
    Window ISettingsHost.Window => this;

    /// <inheritdoc />
    UndoHistory ISettingsHost.History => Session.History;

    /// <inheritdoc />
    AppSettings ISettingsHost.Snapshot => Session.Snapshot;

    /// <summary>Shows a page.</summary>
    /// <param name="page">The page.</param>
    public void ShowPage(SettingsPageId page) => Navigation.SelectedIndex = (int)page;

    /// <summary>Passes a request to the page it belongs to (the caller already showed that page).</summary>
    /// <param name="page">The page.</param>
    /// <param name="request">The request.</param>
    public void HandleRequest(SettingsPageId page, SettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ShowPage(page);
        PageOf(page).Page.HandleRequest(request);
    }

    /// <summary>Closes the window keeping every change; the held files are committed at once (the program exits).</summary>
    public void CloseKeepingChanges()
    {
        exiting = true;
        Close();
    }

    /// <inheritdoc />
    public void ShowSnackbar(string message, string? actionText = null, Action? action = null) => Snackbar.Show(message, actionText, action);

    /// <inheritdoc />
    public void UndoLast()
    {
        string? next = Session.History.UndoDescription;
        try
        {
            if (Session.History.Undo() is { } description)
            {
                Announce($"Undone: {description}.");
            }
        }
        catch (Exception exception) when (FileProblems.Is(exception))
        {
            ReportFileProblem($"Couldn't undo \"{next}\"", exception);
        }
    }

    /// <summary>A file that Undo, Redo or Undo All could not move: the step stays as it was and the InfoBar says why.</summary>
    private void ReportFileProblem(string what, Exception exception)
    {
        services.Log.Warn(LogSource, what + ".", exception);
        ShowWindowMessage(FileProblems.Sentence(what, exception), InfoBarSeverity.Error);
    }

    /// <summary>The last sentence announced to screen readers (what Narrator and NVDA read last; tests and diagnostics).</summary>
    public string? LastAnnouncement { get; private set; }

    /// <inheritdoc />
    public void Announce(string text)
    {
        LastAnnouncement = text;
        Announcer.Announce(this, text);
    }

    /// <inheritdoc />
    public void Navigate(SettingsPageId page, SettingsRequest? request = null)
    {
        if (request is null)
        {
            ShowPage(page);
        }
        else
        {
            HandleRequest(page, request);
        }
    }

    /// <summary>Returns a page, creating it the first time.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The page.</returns>
    public ISettingsPage GetPage(SettingsPageId page) => PageOf(page).Page;

    private (ISettingsPage Page, FrameworkElement Host) PageOf(SettingsPageId id)
    {
        if (pages.TryGetValue(id, out (ISettingsPage Page, FrameworkElement Host) existing))
        {
            return existing;
        }

        UserControl control = id switch
        {
            SettingsPageId.Home => new HomePage(services),
            SettingsPageId.BulbFactory => new BulbFactoryPage(services),
            SettingsPageId.MusicBox => new MusicBoxPage(services),
            SettingsPageId.ScreenSaver => new ScreenSaverPage(services),
            SettingsPageId.Themes => new ThemesPage(services),
            SettingsPageId.General => new GeneralPage(services),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
        };

        var page = (ISettingsPage)control;
        page.Attach(this);
        page.HeaderChanged += (_, _) =>
        {
            if (ReferenceEquals(page, current))
            {
                ShowHeader(page);
            }
        };

        FrameworkElement host;
        if (page.ScrollsAsWhole)
        {
            control.Margin = new Thickness(24, 0, 24, 24);
            host = new ScrollViewer
            {
                Content = control,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                PanningMode = PanningMode.VerticalOnly,
            };
        }
        else
        {
            control.Margin = new Thickness(24, 0, 24, 16);
            host = control;
        }

        pages[id] = (page, host);
        return pages[id];
    }

    private void OnNavigationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedItem is not ListBoxItem { Tag: SettingsPageId id })
        {
            return;
        }

        (ISettingsPage page, FrameworkElement host) = PageOf(id);
        if (ReferenceEquals(page, current))
        {
            return;
        }

        current?.OnHidden();
        current = page;
        PageHost.Content = host;
        ShowHeader(page);
        page.OnShown();
        Snackbar.Dismiss();
        AnimatePageIn();
        ClosePaneOverlay();
        if (services.Settings.Current.Ui.Settings.LastPage != id)
        {
            services.Settings.Update(s => s with { Ui = s.Ui with { Settings = s.Ui.Settings with { LastPage = id } } }, SettingsChange.Internal);
        }
    }

    /// <summary>
    /// The navigation pane's keys (PRODUCT-SPEC 3.0.1 "arrow keys move, Enter/Space selects"): Up, Down, Home, End, Page Up
    /// and Page Down only move the focus, so walking past pages neither opens nor builds them; Enter or Space opens the
    /// focused page. A click still opens its page at once.
    /// </summary>
    private void OnNavigationPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is not ListBoxItem item || !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), Navigation)
            || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != ModifierKeys.None)
        {
            return;
        }

        int index = Navigation.ItemContainerGenerator.IndexFromContainer(item);
        int last = Navigation.Items.Count - 1;
        int? target = e.Key switch
        {
            Key.Up => Math.Max(0, index - 1),
            Key.Down => Math.Min(last, index + 1),
            Key.Home or Key.PageUp => 0,
            Key.End or Key.PageDown => last,
            _ => null,
        };
        if (target is { } next)
        {
            (Navigation.ItemContainerGenerator.ContainerFromIndex(next) as UIElement)?.Focus();
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Space && index >= 0)
        {
            Navigation.SelectedIndex = index;
            e.Handled = true;
        }
    }

    private void ShowHeader(ISettingsPage page)
    {
        Header.Title = page.Title;
        Header.Description = page.Description;
        PlaceHeaderActions(page);
    }

    private void OnHeaderSizeChanged(object sender, SizeChangedEventArgs e) => PlaceHeaderActions(current);

    /// <summary>
    /// Puts the page's actions in the header row, or under the header when that row is too narrow for the actions, Undo
    /// and Redo and the whole title (PRODUCT-SPEC 2.3: the title stays readable at every window size, 760 DIP included).
    /// </summary>
    private void PlaceHeaderActions(ISettingsPage? page)
    {
        FrameworkElement? actions = page?.HeaderActions;
        bool laidOut = actions is not null && VisualTreeHelper.GetParent(actions) is not null;
        bool below = laidOut && !HeaderRowFits(actions!);
        object? inHeader = below ? null : actions;
        object? under = below ? actions : null;

        // Detach before attaching, so the element never has two parents.
        if (!ReferenceEquals(Header.Actions, inHeader))
        {
            Header.Actions = null;
        }

        if (!ReferenceEquals(HeaderActionsBelow.Content, under))
        {
            HeaderActionsBelow.Content = null;
        }

        Header.Actions = inHeader;
        HeaderActionsBelow.Content = under;
        HeaderActionsBelow.Visibility = below ? Visibility.Visible : Visibility.Collapsed;
        if (actions is not null && !laidOut)
        {
            // Measured only once in the window's tree (with its styles): decide again after this layout pass.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (ReferenceEquals(current, page) && VisualTreeHelper.GetParent(actions) is not null)
                {
                    PlaceHeaderActions(page);
                }
            });
        }
    }

    /// <summary>True when the whole title, the page's actions and Undo/Redo fit side by side in the header row.</summary>
    private bool HeaderRowFits(FrameworkElement actions)
    {
        double available = Header.ActualWidth;
        if (available <= 0)
        {
            return true;
        }

        var unlimited = new Size(double.PositiveInfinity, double.PositiveInfinity);
        actions.Measure(unlimited);
        double commands = 0;
        if (Header.Commands is UIElement commandBar)
        {
            commandBar.Measure(unlimited);
            commands = commandBar.DesiredSize.Width;
        }

        // The header template: the title (28 SemiBold, 16 DIP right margin), the actions (12 DIP right margin), the commands.
        var typeface = new Typeface(Header.FontFamily, Header.FontStyle, FontWeights.SemiBold, Header.FontStretch);
        var title = new FormattedText(Header.Title ?? "", CultureInfo.CurrentUICulture, FlowDirection, typeface, 28, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double needed = Math.Ceiling(title.WidthIncludingTrailingWhitespace) + 16 + actions.DesiredSize.Width + 12 + commands;
        return needed + 2 <= available;
    }

    private void AnimatePageIn()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(167);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform();
        PageHost.RenderTransform = slide;
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration) { EasingFunction = ease });
    }

    private void UpdateUndoRedo()
    {
        UndoHistory history = Session.History;
        UndoButton.IsEnabled = history.CanUndo;
        RedoButton.IsEnabled = history.CanRedo;
        UndoAllMenuItem.IsEnabled = history.HasChanges;
        UndoButton.ToolTip = history.UndoDescription is { } undo ? Format("HL.Tip.Window.Undo", undo) : null;
        RedoButton.ToolTip = history.RedoDescription is { } redo ? Format("HL.Tip.Window.Redo", redo) : null;
    }

    private string Format(string key, string description) =>
        string.Format(CultureInfo.CurrentCulture, TryFindResource(key) as string ?? "{0}", description);

    private void OnRedoClick(object sender, RoutedEventArgs e)
    {
        string? next = Session.History.RedoDescription;
        try
        {
            if (Session.History.Redo() is { } description)
            {
                Announce($"Redone: {description}.");
            }
        }
        catch (Exception exception) when (FileProblems.Is(exception))
        {
            ReportFileProblem($"Couldn't redo \"{next}\"", exception);
        }
    }

    private void OnUndoAllClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Session.History.UndoAll(Session.Snapshot);
            Announce("Every change since this window opened was undone.");
        }
        catch (Exception exception) when (FileProblems.Is(exception))
        {
            ReportFileProblem("Couldn't undo every change", exception);
        }
    }

    /// <summary>"OK": closes the window and keeps everything.</summary>
    private void OnOkClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>"Cancel": restores the complete snapshot (and brings back removed items), then closes.</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        cancelled = true;
        Session.Cancel();
        Close();
    }

    /// <summary>"Help": opens Help on the current page's topic.</summary>
    private void OnHelpClick(object sender, RoutedEventArgs e) => services.AppShell.ShowHelp(HelpTopics.ForPage(CurrentPage));

    private void OnHelpNavClick(object sender, RoutedEventArgs e) => services.AppShell.ShowHelp(HelpTopics.ForPage(CurrentPage));

    private void OnAboutNavClick(object sender, RoutedEventArgs e) => services.AppShell.ShowAbout();

    private void OnPaneToggleClick(object sender, RoutedEventArgs e)
    {
        paneOverlayOpen = !paneOverlayOpen;
        UpdatePaneMode();
        if (paneOverlayOpen)
        {
            (Navigation.ItemContainerGenerator.ContainerFromIndex(Navigation.SelectedIndex) as UIElement)?.Focus();
        }
    }

    private void ClosePaneOverlay()
    {
        if (paneOverlayOpen)
        {
            paneOverlayOpen = false;
            UpdatePaneMode();
        }
    }

    /// <summary>Wide windows show the 220 DIP pane; narrower ones a 48 DIP rail whose [=] button opens the pane as an overlay.</summary>
    private void UpdatePaneMode()
    {
        compact = ActualWidth > 0 && ActualWidth < CompactWidth;
        if (!compact)
        {
            paneOverlayOpen = false;
        }

        bool wide = !compact || paneOverlayOpen;
        PaneColumn.Width = new GridLength(compact ? RailWidth : PaneWidth);
        PaneBorder.Width = wide ? PaneWidth : RailWidth;
        PaneToggle.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        PaneTitle.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        if (paneOverlayOpen)
        {
            PaneBorder.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
            PaneBorder.BorderThickness = new Thickness(0, 0, 1, 0);
            PaneBorder.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.3 };
        }
        else
        {
            PaneBorder.Background = Brushes.Transparent;
            PaneBorder.BorderThickness = new Thickness(0);
            PaneBorder.Effect = null;
        }

        foreach (object item in Navigation.Items.Cast<object>().Append(HelpNavButton).Append(AboutNavButton))
        {
            if (item is DependencyObject element)
            {
                ToolTipService.SetIsEnabled(element, compact && !paneOverlayOpen);
            }
        }
    }

    private void UpdateStringOfLights() =>
        StringOfLights.Visibility = services.Settings.Current.Ui.DecorateWindow && !SystemParameters.HighContrast && StringOfLights.HasBulbs
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.OldSettings.Ui.DecorateWindow != e.NewSettings.Ui.DecorateWindow)
        {
            UpdateStringOfLights();
        }
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            UpdateStringOfLights();
        }
    }

    /// <summary>The window's keys (PRODUCT-SPEC Appendix B). Esc never closes the window.</summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ContentDialogs.GetIsDialogOpen(this) || Peek.IsPeeking)
        {
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = modifiers.HasFlag(ModifierKeys.Control);
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl && !modifiers.HasFlag(ModifierKeys.Alt) && PageNumber(key) is { } number)
        {
            ShowPage((SettingsPageId)number);
            e.Handled = true;
        }
        else if (ctrl && key == Key.Tab)
        {
            int count = Navigation.Items.Count;
            ShowPage((SettingsPageId)((Navigation.SelectedIndex + (shift ? count - 1 : 1)) % count));
            FocusNavigation();
            e.Handled = true;
        }
        else if (key == Key.F6)
        {
            MoveFocusZone(!shift);
            e.Handled = true;
        }
        else if (key == Key.F1 && modifiers == ModifierKeys.None)
        {
            services.AppShell.ShowHelp(current?.HelpTopicFor(Keyboard.FocusedElement as DependencyObject) ?? HelpTopics.ForPage(CurrentPage));
            e.Handled = true;
        }
        else if (ctrl && !IsTextInput(Keyboard.FocusedElement) && (key == Key.Y || (key == Key.Z && shift)))
        {
            OnRedoClick(this, e);
            e.Handled = true;
        }
        else if (ctrl && !shift && key == Key.Z && !IsTextInput(Keyboard.FocusedElement))
        {
            UndoLast();
            e.Handled = true;
        }
        else if (key == Key.Escape && paneOverlayOpen)
        {
            ClosePaneOverlay();
            e.Handled = true;
        }
    }

    private static int? PageNumber(Key key) => key switch
    {
        >= Key.D1 and <= Key.D6 => key - Key.D1,
        >= Key.NumPad1 and <= Key.NumPad6 => key - Key.NumPad1,
        _ => null,
    };

    private static bool IsTextInput(IInputElement? element) => element is TextBoxBase or PasswordBox or ComboBox { IsEditable: true };

    /// <summary>F6 moves between the navigation, the page and the bottom bar.</summary>
    private void MoveFocusZone(bool forward)
    {
        DependencyObject? focused = Keyboard.FocusedElement as DependencyObject;
        int zone = focused is null ? -1
            : IsInside(focused, PaneBorder) ? 0
            : IsInside(focused, ContentLayer) ? 1
            : IsInside(focused, BottomBar) ? 2
            : -1;
        int next = ((zone < 0 ? (forward ? -1 : 0) : zone) + (forward ? 1 : 2)) % 3;
        switch (next)
        {
            case 0:
                FocusNavigation();
                break;
            case 1:
                PageHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                if (!IsInside(Keyboard.FocusedElement as DependencyObject, ContentLayer))
                {
                    Header.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }

                break;
            default:
                OkButton.Focus();
                break;
        }
    }

    private void FocusNavigation() =>
        (Navigation.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, Navigation.SelectedIndex)) as UIElement)?.Focus();

    private static bool IsInside(DependencyObject? element, DependencyObject container) => ElementTree.IsInside(element, container);

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        try
        {
            double primaryScale = services.Displays.Primary.Scale;
            WindowPlacementSettings placement = WindowPlacement.Capture(this, primaryScale);
            services.Settings.Update(s => s with { Ui = s.Ui with { Settings = s.Ui.Settings with { Window = placement } } }, SettingsChange.Internal);
        }
        catch (InvalidOperationException exception)
        {
            services.Log.Warn(LogSource, "The window placement could not be remembered.", exception);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        current?.OnHidden();
        foreach ((ISettingsPage page, _) in pages.Values)
        {
            (page as IDisposable)?.Dispose();
        }

        Peek.End();
        services.Settings.Changed -= OnSettingsChanged;
        services.Settings.SaveFailed -= OnSaveFailed;
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        Session.End(commitNow: exiting);
        services.Log.Info(LogSource, cancelled ? "Settings closed with Cancel." : "Settings closed.");
    }
}
