using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using HolidayLights.App.Controls;
using HolidayLights.App.Settings.Dialogs;
using HolidayLights.App.Settings.Undo;

namespace HolidayLights.App.Settings.Pages;

/// <summary>
/// The "Themes" page of the Settings window (owner: settings-ui; PRODUCT-SPEC 3.6): the Automatic Themes card, theme
/// cards in groups (load, rename, duplicate, restore original, delete), Save Theme, Restore Built-In Themes and Recent
/// Settings.
/// </summary>
public partial class ThemesPage : UserControl, ISettingsPage, IDisposable
{
    private const string LogSource = "Settings.Themes";

    private readonly StackPanel headerActions;
    private ISettingsHost? host;
    private bool disposed;

    /// <summary>Creates the page.</summary>
    /// <param name="services">The application services.</param>
    public ThemesPage(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        InitializeComponent();
        headerActions = BuildHeaderActions();
        services.Settings.Changed += OnSettingsChanged;
        services.Themes.Changed += OnThemesChanged;
        Refresh();
    }

    /// <summary>The application services.</summary>
    public IAppServices Services { get; }

    /// <inheritdoc />
    public event EventHandler? HeaderChanged;

    /// <inheritdoc />
    public SettingsPageId PageId => SettingsPageId.Themes;

    /// <inheritdoc />
    public string Title => "Themes";

    /// <inheritdoc />
    public string Description => "A theme is a collection of saved bulb, music and screen saver settings. Loading a theme will replace your current bulb, music and screen saver settings.";

    /// <inheritdoc />
    public FrameworkElement? HeaderActions => headerActions;

    /// <inheritdoc />
    public bool ScrollsAsWhole => true;

    /// <summary>Every card on the page, in display order.</summary>
    public IReadOnlyList<ThemeCardItem> Cards { get; private set; } = [];

    /// <inheritdoc />
    public void Attach(ISettingsHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void OnShown() => Refresh();

    /// <inheritdoc />
    public void OnHidden()
    {
    }

    /// <inheritdoc />
    public void HandleRequest(SettingsRequest request)
    {
        if (request is ThemeSwitchedRequest switched)
        {
            ShowMessage(
                $"Holiday Lights switched to {switched.ThemeName} on {LightsStatusText.ShortDate(switched.Date)}.",
                InfoBarSeverity.Informational,
                ("Undo", UndoAutomaticSwitch),
                ("Turn Off Automatic Themes", () =>
                {
                    ThemeActions.TurnOffAutomatic(Services);
                    MessageBar.IsOpen = false;
                }));
        }
    }

    /// <inheritdoc />
    public string HelpTopicFor(DependencyObject? focused)
    {
        foreach (DependencyObject node in Controls.ElementTree.SelfAndAncestors(focused))
        {
            if (ReferenceEquals(node, AutomaticToggle) || ReferenceEquals(node, TellMeCheck))
            {
                return HelpTopics.AutomaticThemes;
            }

            if (ReferenceEquals(node, RecentExpander))
            {
                return HelpTopics.RecentSettings;
            }
        }

        return HelpTopics.AboutThemes;
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
        Services.Themes.Changed -= OnThemesChanged;
        GC.SuppressFinalize(this);
    }

    private StackPanel BuildHeaderActions()
    {
        var save = new Button { Content = "Sa_ve Settings As Theme…", Margin = new Thickness(0, 0, 8, 0) };
        save.SetResourceReference(ToolTipProperty, "HL.Tip.Themes.SaveTheme");
        save.Click += (_, _) => SaveTheme();
        var restore = new Button { Content = "_Restore Built-In Themes…" };
        restore.SetResourceReference(ToolTipProperty, "HL.Tip.Themes.RestoreBuiltIn");
        restore.Click += (_, _) => RestoreBuiltIn();
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { save, restore } };
    }

    private void Refresh()
    {
        UpdateAutomatic();
        BuildCards();
        BuildRecent();
        UpdateProblems();
    }

    /// <summary>"Today: Halloween (until Oct 31). Next: Thanksgiving on Nov 1." / "Automatic themes are off. Your lights stay as they are."</summary>
    private void UpdateAutomatic()
    {
        CalendarSettings calendar = Services.Settings.Current.Calendar;
        AutomaticToggle.IsChecked = calendar.Enabled;
        TellMeCheck.IsChecked = calendar.Notify;
        CalendarStatus.Text = CalendarText(Services.Calendar, calendar, DateOnly.FromDateTime(DateTime.Now));
    }

    /// <summary>The status line of the Automatic Themes card (PRODUCT-SPEC 3.6.4).</summary>
    /// <param name="seasons">The season calendar.</param>
    /// <param name="calendar">The calendar settings.</param>
    /// <param name="today">Today.</param>
    /// <returns>The sentence.</returns>
    public static string CalendarText(ISeasonCalendar seasons, CalendarSettings calendar, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        ArgumentNullException.ThrowIfNull(calendar);
        if (!calendar.Enabled)
        {
            return "Automatic themes are off. Your lights stay as they are.";
        }

        CalendarResolution resolution = seasons.Resolve(calendar, today);
        string text = resolution.EntryId is { } entry
            ? $"Today: {seasons.GetDisplayName(entry)}{(resolution.End is { } end ? $" (until {LightsStatusText.ShortDate(end)})" : "")}."
            : $"Today: between holidays ({resolution.ThemeName}).";
        if (seasons.GetNextChange(calendar, today) is { } next)
        {
            string name = next.Resolution.EntryId is { } nextEntry ? seasons.GetDisplayName(nextEntry) : next.Resolution.ThemeName;
            text += $" Next: {name} on {LightsStatusText.ShortDate(next.Date)}.";
        }

        return text;
    }

    /// <summary>The three groups, A-Z inside each (PRODUCT-SPEC 3.6.2); each group hidden when empty.</summary>
    private void BuildCards()
    {
        AppSettings settings = Services.Settings.Current;
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        var mine = new List<ThemeCardItem>();
        var classic = new List<ThemeCardItem>();
        var added = new List<ThemeCardItem>();
        foreach (ThemeDefinition theme in Services.Themes.Themes.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            IReadOnlyList<string> missing = Services.ThemeService.FindMissingBulbs(theme);
            var card = new ThemeCardItem(theme.Name, theme, isAutomatic: false)
            {
                Flash = ThemeActions.FlashOf(theme, settings),
                Summary = ThemeActions.Summary(theme, Services.Songs),
                DateText = CalendarDates(theme.Name, settings.Calendar, today),
                IsChanged = theme.Shipped is not null && Services.Themes.IsChangedFromOriginal(theme),
                MissingText = missing.Count switch
                {
                    0 => null,
                    1 => "1 bulb missing",
                    _ => string.Create(CultureInfo.CurrentCulture, $"{missing.Count} bulbs missing"),
                },
                MissingTip = missing.Count == 0 ? null : string.Join(", ", missing.Select(id => BulbIds.IsValid(id) ? BulbIds.GetKey(id) : id)),
            };
            card.IsCurrent = Services.ThemeService.Matches(theme, settings);
            (theme.Shipped switch
            {
                ShippedThemeKind.Classic => classic,
                ShippedThemeKind.New => added,
                _ => mine,
            }).Add(card);
        }

        MyThemes.ItemsSource = mine;
        ClassicThemes.ItemsSource = classic;
        NewThemes.ItemsSource = added;
        SetGroupVisibility(MyThemesHeader, MyThemes, mine.Count);
        SetGroupVisibility(ClassicHeader, ClassicThemes, classic.Count);
        SetGroupVisibility(NewHeader, NewThemes, added.Count);
        EmptyText.Visibility = mine.Count + classic.Count + added.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Cards = [.. mine, .. classic, .. added];
    }

    private static void SetGroupVisibility(UIElement header, UIElement items, int count)
    {
        header.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        items.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private string? CalendarDates(string themeName, CalendarSettings calendar, DateOnly today)
    {
        CalendarEntry? entry = calendar.Entries.FirstOrDefault(e => e.Use && string.Equals(e.Theme, themeName, StringComparison.CurrentCultureIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        HolidayOccurrence occurrence = Services.Calendar.GetOccurrence(entry, calendar.Region, today);
        return $"{LightsStatusText.ShortDate(occurrence.Start)} - {LightsStatusText.ShortDate(occurrence.End)}";
    }

    /// <summary>"Before Halloween (automatic) - Oct 1, 2026 12:00 AM" [Restore], newest first.</summary>
    private void BuildRecent()
    {
        RecentList.Children.Clear();
        IReadOnlyList<RecentSettingsEntry> entries = [.. Services.Settings.Current.RecentSettings.OrderByDescending(e => e.Date)];
        if (entries.Count == 0)
        {
            var none = new TextBlock { Text = "No settings have been replaced yet.", Margin = new Thickness(0, 4, 0, 4) };
            none.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            RecentList.Children.Add(none);
            return;
        }

        foreach (RecentSettingsEntry entry in entries)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string text = $"{entry.Label} - {entry.Date.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)}";
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var restore = new Button { Content = "Restore", Margin = new Thickness(12, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(restore, "Restore " + entry.Label);
            RecentSettingsEntry captured = entry;
            restore.Click += (_, _) => RestoreRecent(captured);
            Grid.SetColumn(restore, 1);
            row.Children.Add(restore);
            RecentList.Children.Add(row);
        }
    }

    private void RestoreRecent(RecentSettingsEntry entry)
    {
        Services.Settings.Update(
            s => Services.ThemeService.RestoreRecent(s, entry, DateTimeOffset.Now),
            new SettingsChange(SettingsChangeKind.ThemeLoaded, $"Restore {entry.Label}"));
        if (host is not null)
        {
            host.ShowSnackbar($"Restored {entry.Label}.", "Undo", host.UndoLast);
            host.Announce($"Restored {entry.Label}.");
        }
    }

    /// <summary>The undo of an automatic switch: restores the values recorded just before it.</summary>
    private void UndoAutomaticSwitch()
    {
        RecentSettingsEntry? before = Services.Settings.Current.RecentSettings
            .Where(e => e.Label.EndsWith(" (automatic)", StringComparison.Ordinal))
            .OrderByDescending(e => e.Date)
            .FirstOrDefault();
        if (before is not null)
        {
            RestoreRecent(before);
        }

        MessageBar.IsOpen = false;
    }

    private void UpdateProblems()
    {
        IReadOnlyList<ThemeFileProblem> problems = Services.Themes.Problems;
        if (problems.Count == 0)
        {
            ProblemsBar.IsOpen = false;
            return;
        }

        string files = string.Join(", ", problems.Select(p => System.IO.Path.GetFileName(p.FilePath)));
        ProblemsBar.Message = problems.Count == 1
            ? $"1 theme couldn't be read: {files}."
            : string.Create(CultureInfo.CurrentCulture, $"{problems.Count} themes couldn't be read: {files}.");
        ProblemsBar.IsOpen = true;
    }

    private void ShowMessage(string text, InfoBarSeverity severity, (string Text, Action Action)? action = null, (string Text, Action Action)? secondary = null)
    {
        MessageBar.Severity = severity;
        MessageBar.Message = text;
        MessageBar.ActionText = action?.Text;
        MessageBar.ActionCommand = action is { } a ? new DelegateCommand(a.Action) : null;
        MessageBar.SecondaryActionText = secondary?.Text;
        MessageBar.SecondaryActionCommand = secondary is { } b ? new DelegateCommand(b.Action) : null;
        MessageBar.IsOpen = true;
    }

    /// <summary>Click, and Checked/Unchecked (UI Automation's Toggle raises no Click; review r1 #34): only a real difference applies.</summary>
    private void OnAutomaticClick(object sender, RoutedEventArgs e)
    {
        if ((AutomaticToggle.IsChecked == true) == Services.Settings.Current.Calendar.Enabled)
        {
            return;
        }

        if (AutomaticToggle.IsChecked == true)
        {
            ThemeActions.TurnOnAutomatic(Services, host);
            MessageBar.IsOpen = false;
        }
        else
        {
            ThemeActions.TurnOffAutomatic(Services);
        }
    }

    private void OnTellMeClick(object sender, RoutedEventArgs e)
    {
        bool notify = TellMeCheck.IsChecked == true;
        if (notify == Services.Settings.Current.Calendar.Notify)
        {
            return;
        }

        Services.Settings.Update(s => s with { Calendar = s.Calendar with { Notify = notify } },
            SettingsChange.Edit(notify ? "Tell me when the theme changes" : "Don't tell me when the theme changes"));
    }

    private void OnEditHolidaysClick(object sender, RoutedEventArgs e)
    {
        if (host is not null)
        {
            ThemeCalendarDialog.Show(host, Services);
        }
    }

    private void SaveTheme()
    {
        if (host is not null)
        {
            SaveThemeDialog.Show(host, Services);
        }
    }

    private void RestoreBuiltIn()
    {
        if (host is not null)
        {
            RestoreThemesDialog.Show(host, Services);
        }
    }

    private void OnCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ThemeCardItem { IsRenaming: false } card })
        {
            Load(card);
        }
    }

    private void Load(ThemeCardItem card)
    {
        if (card.Theme is not { } theme)
        {
            return;
        }

        if (ThemeActions.Load(Services, host, theme))
        {
            ShowMessage("Automatic themes are off because you chose a theme.", InfoBarSeverity.Informational, ("Turn Back On", () =>
            {
                ThemeActions.TurnOnAutomatic(Services, host);
                MessageBar.IsOpen = false;
            }));
        }

        IReadOnlyList<string> missing = Services.ThemeService.FindMissingBulbs(theme);
        if (missing.Count > 0)
        {
            ShowMessage(
                (missing.Count == 1 ? "This theme uses 1 bulb that isn't installed: " : string.Create(CultureInfo.CurrentCulture, $"This theme uses {missing.Count} bulbs that aren't installed: "))
                + string.Join(", ", missing.Select(id => BulbIds.IsValid(id) ? BulbIds.GetKey(id) : id)) + ".",
                InfoBarSeverity.Warning);
        }
    }

    private void OnCardKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Button { DataContext: ThemeCardItem card } button || card.IsRenaming)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F2:
                BeginRename(card, button);
                e.Handled = true;
                break;
            case Key.Delete:
                Delete(card);
                e.Handled = true;
                break;
        }
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: ThemeCardItem card } more && FindCardButton(more) is { ContextMenu: { } menu } button)
        {
            FillMenu(menu, card, button);
            menu.PlacementTarget = more;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnCardMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is Button { DataContext: ThemeCardItem card, ContextMenu: { } menu } button)
        {
            FillMenu(menu, card, button);
        }
    }

    /// <summary>"Load", "Rename...", "Duplicate", "Restore Original" (changed shipped themes), "Delete" (PRODUCT-SPEC 3.6.2).</summary>
    private void FillMenu(ContextMenu menu, ThemeCardItem card, Button button)
    {
        menu.Items.Clear();
        MenuItem Item(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        Item("_Load", () => Load(card)).FontWeight = FontWeights.SemiBold;
        Item("_Rename…", () => BeginRename(card, button)).InputGestureText = "F2";
        Item("_Duplicate", () => Duplicate(card));
        if (card.CanRestoreOriginal)
        {
            Item("Restore _Original", () => RestoreOriginal(card));
        }

        menu.Items.Add(new Separator());
        Item("_Delete", () => Delete(card)).InputGestureText = "Delete";
    }

    private static Button? FindCardButton(DependencyObject element) =>
        ElementTree.SelfAndAncestors(element).Skip(1).OfType<Button>().FirstOrDefault(b => b.Name == "Card");

    private void BeginRename(ThemeCardItem card, Button button)
    {
        card.IsRenaming = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (FindDescendant<TextBox>(button) is { } box)
            {
                box.Text = card.Name;
                box.Focus();
                box.SelectAll();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ThemeCardItem card } box)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitRename(card, box.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            card.IsRenaming = false;
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: ThemeCardItem { IsRenaming: true } card } box)
        {
            CommitRename(card, box.Text);
        }
    }

    /// <summary>Renames a theme (Save Theme's rules); calendar rows that use it follow; one undo step.</summary>
    internal void CommitRename(ThemeCardItem card, string text)
    {
        card.IsRenaming = false;
        string newName = text.Trim();
        string oldName = card.Name;
        if (newName == oldName || host is null)
        {
            return;
        }

        ThemeNameCheck check = Services.ThemeService.ValidateName(newName);
        if (check != ThemeNameCheck.Valid || (Services.Themes.Find(newName) is { } other && !string.Equals(other.Name, oldName, StringComparison.CurrentCultureIgnoreCase)))
        {
            string reason = check switch
            {
                ThemeNameCheck.InvalidCharacters => SaveThemeDialog.InvalidCharactersText,
                ThemeNameCheck.TooLong => SaveThemeDialog.TooLongText,
                ThemeNameCheck.Empty => "A theme needs a name.",
                _ => $"A theme named \"{newName}\" already exists.",
            };
            ShowMessage(reason, InfoBarSeverity.Warning);
            host.Announce(reason);
            return;
        }

        try
        {
            Services.Themes.Rename(oldName, newName);
        }
        catch (Exception exception) when (FileProblems.Is(exception))
        {
            ShowFileProblem($"Couldn't rename {oldName}", exception);
            return;
        }

        // One step: the rename (kept by Cancel, which then makes the restored calendar use the new name too) and the
        // calendar rows that follow it.
        string description = $"Rename {oldName} to {newName}";
        using (host.History.BeginGroup(description))
        {
            host.History.Record(
                new UndoStep(description, () => Services.Themes.Rename(newName, oldName), () => Services.Themes.Rename(oldName, newName)),
                UndoCancelBehavior.KeepOnCancel,
                settings => ThemeActions.FollowRename(settings, oldName, newName));
            AppSettings current = Services.Settings.Current;
            if (!ReferenceEquals(ThemeActions.FollowRename(current, oldName, newName), current))
            {
                Services.Settings.Update(s => ThemeActions.FollowRename(s, oldName, newName), SettingsChange.Edit(description));
            }
        }

        host.Announce($"Renamed {oldName} to {newName}.");
    }

    /// <summary>Shows a theme file operation that failed in the page's InfoBar, with the Windows reason (PRODUCT-SPEC 5.13).</summary>
    /// <param name="what">"Couldn't delete Halloween".</param>
    /// <param name="exception">The exception.</param>
    private void ShowFileProblem(string what, Exception exception)
    {
        Services.Log.Warn(LogSource, what + ".", exception);
        ShowMessage(FileProblems.Sentence(what, exception), InfoBarSeverity.Error);

        // The InfoBar sits at the top of the page; the card that failed may be far below it.
        Dispatcher.BeginInvoke(() => MessageBar.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>"Duplicate": "&lt;name&gt; Copy" (then "Copy 2", ...); one undo step.</summary>
    internal void Duplicate(ThemeCardItem card)
    {
        if (card.Theme is not { } theme || host is null)
        {
            return;
        }

        string name = $"{theme.Name} Copy";
        for (int i = 2; Services.Themes.Find(name) is not null; i++)
        {
            name = string.Create(CultureInfo.CurrentCulture, $"{theme.Name} Copy {i}");
        }

        ThemeDefinition copy = theme with { Name = name, Shipped = null };
        try
        {
            Services.Themes.Save(copy);
        }
        catch (Exception exception) when (FileProblems.Is(exception))
        {
            ShowFileProblem($"Couldn't duplicate {theme.Name}", exception);
            return;
        }

        HeldItem? held = null;
        host.History.Record(new UndoStep(
            $"Duplicate {theme.Name}",
            () => held = Services.Themes.Delete(name),
            () =>
            {
                if (held is not null)
                {
                    Services.Themes.Restore(held);
                }
            }));
        host.Announce($"Created {name}.");
    }

    /// <summary>"Restore Original": a changed shipped theme gets its original values back; one undo step.</summary>
    internal void RestoreOriginal(ThemeCardItem card)
    {
        if (card.Theme is not { } changed || host is null)
        {
            return;
        }

        try
        {
            Services.Themes.RestoreShipped([changed.Name]);
        }
        catch (Exception exception) when (FileProblems.Is(exception))
        {
            ShowFileProblem($"Couldn't restore the original {changed.Name}", exception);
            return;
        }

        host.History.Record(new UndoStep(
            $"Restore the original {changed.Name}",
            () => Services.Themes.Save(changed),
            () => Services.Themes.RestoreShipped([changed.Name])));
        host.Announce($"Restored the original {changed.Name}.");
    }

    /// <summary>"Delete": no question; the theme goes to the holding folder; snackbar "Deleted &lt;name&gt;." [Undo]; Cancel restores it.</summary>
    internal void Delete(ThemeCardItem card)
    {
        if (host is null)
        {
            return;
        }

        string name = card.Name;
        HeldItem held;
        try
        {
            held = Services.Themes.Delete(name);
        }
        catch (Exception exception) when (FileProblems.Is(exception))
        {
            ShowFileProblem($"Couldn't delete {name}", exception);
            return;
        }

        host.History.Record(
            new UndoStep($"Delete {name}", () => Services.Themes.Restore(held), () => held = Services.Themes.Delete(name)),
            UndoCancelBehavior.RevertOnCancel);
        host.ShowSnackbar($"Deleted {name}.", "Undo", host.UndoLast);
        host.Announce($"Deleted {name}.");
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        UpdateAutomatic();
        if (!Equals(e.OldSettings.Current, e.NewSettings.Current) || !Equals(e.OldSettings.Calendar, e.NewSettings.Calendar))
        {
            foreach (ThemeCardItem card in Cards)
            {
                if (card.Theme is { } theme)
                {
                    card.IsCurrent = Services.ThemeService.Matches(theme, e.NewSettings);
                }
            }
        }

        if (!Equals(e.OldSettings.Calendar, e.NewSettings.Calendar))
        {
            BuildCards();
        }

        if (!Equals(e.OldSettings.RecentSettings, e.NewSettings.RecentSettings))
        {
            BuildRecent();
        }
    }

    private void OnThemesChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            BuildCards();
            UpdateProblems();
        }
    });
}
