using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HolidayLights.App.Controls;
using HolidayLights.Audio;

namespace HolidayLights.App.Settings.Pages;

/// <summary>
/// The "Home" page of the Settings window (owner: settings-ui; PRODUCT-SPEC 3.1): everything most people ever change,
/// on one screen, with the real lights in view.
/// </summary>
public partial class HomePage : UserControl, ISettingsPage, IDisposable
{
    /// <summary>The longest greeting taken from the screen saver message.</summary>
    public const int MaxGreetingLength = 40;

    /// <summary>How long a theme card is hovered or focused before the stage previews it.</summary>
    public static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(300);

    private readonly StackPanel headerActions;
    private readonly DispatcherTimer previewTimer;
    private readonly DispatcherTimer musicTimer;
    private ISettingsHost? host;
    private ThemeCardItem? previewCandidate;
    private StatusLine? statusLine;
    private string title = "Holiday Lights";
    private string description = "Custom settings";
    private bool shown;
    private bool disposed;

    /// <summary>Creates the page.</summary>
    /// <param name="services">The application services.</param>
    public HomePage(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        InitializeComponent();
        headerActions = new StackPanel { Orientation = Orientation.Horizontal, Children = { new PeekButton() } };
        Stage.Services = services;
        FlashGroup.Services = services;
        DrawingGroup.Services = services;
        Stage.Hit += OnStageHit;
        previewTimer = new DispatcherTimer { Interval = PreviewDelay };
        previewTimer.Tick += (_, _) =>
        {
            previewTimer.Stop();
            ShowPreview(previewCandidate);
        };
        musicTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        musicTimer.Tick += (_, _) => UpdateMusic();
        services.Settings.Changed += OnSettingsChanged;
        services.Lights.SceneChanged += OnLightsChanged;
        services.Lights.StatusChanged += OnLightsChanged;
        services.Music.StateChanged += OnMusicChanged;
        services.Themes.Changed += OnThemesChanged;
        RefreshAll();
    }

    /// <summary>The application services.</summary>
    public IAppServices Services { get; }

    /// <inheritdoc />
    public event EventHandler? HeaderChanged;

    /// <inheritdoc />
    public SettingsPageId PageId => SettingsPageId.Home;

    /// <inheritdoc />
    public string Title => title;

    /// <inheritdoc />
    public string Description => description;

    /// <inheritdoc />
    public FrameworkElement? HeaderActions => headerActions;

    /// <inheritdoc />
    public bool ScrollsAsWhole => true;

    /// <summary>The cards of "Choose a Theme", in order.</summary>
    public IReadOnlyList<ThemeCardItem> Cards { get; private set; } = [];

    /// <inheritdoc />
    public void Attach(ISettingsHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void OnShown()
    {
        shown = true;
        RefreshAll();
    }

    /// <inheritdoc />
    public void OnHidden()
    {
        shown = false;
        musicTimer.Stop();
        previewTimer.Stop();
        ShowPreview(null);
    }

    /// <inheritdoc />
    public void HandleRequest(SettingsRequest request)
    {
    }

    /// <inheritdoc />
    public string HelpTopicFor(DependencyObject? focused)
    {
        foreach (DependencyObject node in Controls.ElementTree.SelfAndAncestors(focused))
        {
            if (ReferenceEquals(node, ThemeCards))
            {
                return HelpTopics.AboutThemes;
            }

            if (ReferenceEquals(node, FlashGroup))
            {
                return HelpTopics.FlashPattern;
            }

            if (ReferenceEquals(node, DrawingGroup))
            {
                return HelpTopics.DesktopOrOnTop;
            }

            if (ReferenceEquals(node, MusicToggle))
            {
                return HelpTopics.MusicOnOff;
            }
        }

        return HelpTopics.LightsOnOff;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        previewTimer.Stop();
        musicTimer.Stop();
        Services.Settings.Changed -= OnSettingsChanged;
        Services.Lights.SceneChanged -= OnLightsChanged;
        Services.Lights.StatusChanged -= OnLightsChanged;
        Services.Music.StateChanged -= OnMusicChanged;
        Services.Themes.Changed -= OnThemesChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The greeting: the screen saver message's first line (at most 40 characters), else "Holiday Lights" (PRODUCT-SPEC 3.1).
    /// </summary>
    /// <param name="message">The screen saver message.</param>
    /// <returns>The page title.</returns>
    public static string Greeting(string? message)
    {
        string line = (message ?? "").Replace("\r", "", StringComparison.Ordinal).Split('\n')
            .Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        if (line.Length > MaxGreetingLength)
        {
            line = line[..MaxGreetingLength].TrimEnd();
        }

        return line.Length == 0 ? "Holiday Lights" : line;
    }

    private void RefreshAll()
    {
        UpdateHeader();
        UpdateLights();
        BuildCards();
        UpdateBulbSize();
        UpdateMusic();
        Stage.SetValue(AutomationProperties.HelpTextProperty, ArrangementTexts.Summary(Services.Settings.Current.Current.Arrangement, NameOf));
    }

    private string NameOf(string bulbId) =>
        Services.Bulbs.TryGetInfo(bulbId, out BulbInfo? info) ? info.Name : BulbIds.IsValid(bulbId) ? BulbIds.GetKey(bulbId) : bulbId;

    /// <summary>"Happy Halloween!" / "Halloween - Automatic theme until Oct 31" / "Halloween - chosen by you" / "Custom settings".</summary>
    private void UpdateHeader()
    {
        AppSettings settings = Services.Settings.Current;
        string newTitle = Greeting(settings.Current.Saver.Message);
        string newDescription = "Custom settings";
        if (Services.ThemeService.FindMatching(settings) is { } theme)
        {
            newDescription = $"{theme.Name} - chosen by you";
            if (settings.Calendar.Enabled)
            {
                CalendarResolution today = Services.Calendar.Resolve(settings.Calendar, DateOnly.FromDateTime(DateTime.Now));
                if (string.Equals(today.ThemeName, theme.Name, StringComparison.CurrentCultureIgnoreCase))
                {
                    newDescription = today.End is { } end
                        ? $"{theme.Name} - Automatic theme until {LightsStatusText.ShortDate(end)}"
                        : $"{theme.Name} - Automatic theme";
                }
            }
        }

        if (newTitle != title || newDescription != description)
        {
            title = newTitle;
            description = newDescription;
            HeaderChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateLights()
    {
        AppSettings settings = Services.Settings.Current;
        ShowLightsToggle.IsChecked = settings.Lights.On;
        (DateOnly, string)? next = null;
        if (settings.Calendar.Enabled && Services.Calendar.GetNextChange(settings.Calendar, DateOnly.FromDateTime(DateTime.Now)) is { } change)
        {
            next = (change.Date, change.Resolution.ThemeName);
        }

        statusLine = LightsStatusText.Home(settings, Services.Lights.Scene, Services.Lights.Status, next);
        StatusText.Text = statusLine.Text;
        StatusLink.Content = statusLine.ActionText;
        StatusLink.Visibility = statusLine.ActionText is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnStatusActionClick(object sender, RoutedEventArgs e)
    {
        switch (statusLine?.Action)
        {
            case StatusAction.TurnOn:
                Services.Lights.SetLightsOn(true);
                break;
            case StatusAction.ChooseDisplays or StatusAction.ChangeEnergySaver:
                host?.Navigate(SettingsPageId.General);
                break;
            case StatusAction.ChangeBulbs:
                host?.Navigate(SettingsPageId.BulbFactory);
                break;
            case StatusAction.TryAgain:
                Services.Lights.RetryPreferredLayer();
                break;
        }
    }

    /// <summary>Click, and Checked/Unchecked (UI Automation's Toggle raises no Click; review r1 #34): only a real difference applies.</summary>
    private void OnShowLightsClick(object sender, RoutedEventArgs e)
    {
        bool on = ShowLightsToggle.IsChecked == true;
        if (on != Services.Settings.Current.Lights.On)
        {
            Services.Lights.SetLightsOn(on);
        }
    }

    /// <summary>
    /// The eight cards, de-duplicated, in this order: Automatic; the current theme; today's calendar theme; the next three
    /// different calendar themes; Christmas 1; the two themes loaded most recently; then Classic Lights, Christmas
    /// Twinkle and Holiday Party until there are eight (PRODUCT-SPEC 3.1).
    /// </summary>
    private void BuildCards()
    {
        AppSettings settings = Services.Settings.Current;
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        CalendarSettings calendar = settings.Calendar with { Enabled = true };
        CalendarResolution resolution = Services.Calendar.Resolve(calendar, today);
        ThemeDefinition? todays = Services.Themes.Find(resolution.ThemeName);
        string todayName = resolution.EntryId is { } entry ? Services.Calendar.GetDisplayName(entry) : resolution.ThemeName;
        var cards = new List<ThemeCardItem>
        {
            new("Automatic", todays, isAutomatic: true)
            {
                Flash = todays is null ? null : ThemeActions.FlashOf(todays, settings),
                DateText = $"{todayName} today",
            },
        };
        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        void Add(string? name)
        {
            if (cards.Count >= 8 || name is null || names.Contains(name) || Services.Themes.Find(name) is not { } theme)
            {
                return;
            }

            names.Add(theme.Name);
            cards.Add(new ThemeCardItem(theme.Name, theme, isAutomatic: false)
            {
                Flash = ThemeActions.FlashOf(theme, settings),
                DateText = CalendarDates(theme.Name, settings.Calendar, today),
            });
        }

        ThemeDefinition? matching = Services.ThemeService.FindMatching(settings);
        Add(matching?.Name);
        Add(resolution.ThemeName);
        DateOnly from = today;
        for (int i = 0, found = 0; i < 12 && found < 3; i++)
        {
            if (Services.Calendar.GetNextChange(calendar, from) is not { } change)
            {
                break;
            }

            int before = cards.Count;
            Add(change.Resolution.ThemeName);
            found += cards.Count > before ? 1 : 0;
            from = change.Date;
        }

        Add(ShippedThemeNames.Christmas1);
        foreach (string name in ThemeActions.RecentlyLoaded(settings).Take(2))
        {
            Add(name);
        }

        Add(ShippedThemeNames.ClassicLights);
        Add(ShippedThemeNames.ChristmasTwinkle);
        Add(ShippedThemeNames.HolidayParty);
        foreach (ThemeCardItem card in cards)
        {
            card.IsSelected = card.IsAutomatic
                ? settings.Calendar.Enabled
                : !settings.Calendar.Enabled && matching is not null && string.Equals(card.Name, matching.Name, StringComparison.CurrentCultureIgnoreCase);
        }

        Cards = cards;
        ThemeCards.ItemsSource = cards;
    }

    /// <summary>"Oct 1 - Oct 31" when a checked calendar entry uses the theme, else null.</summary>
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

    private void OnThemeCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ThemeCardItem card })
        {
            return;
        }

        previewTimer.Stop();
        ShowPreview(null);
        LoadCard(card);
    }

    private void LoadCard(ThemeCardItem card)
    {
        if (card.IsAutomatic)
        {
            if (Services.Settings.Current.Calendar.Enabled)
            {
                host?.Announce("Automatic themes are on.");
                return;
            }

            ThemeActions.TurnOnAutomatic(Services, host);
        }
        else if (card.Theme is { } theme)
        {
            ThemeActions.Load(Services, host, theme);
        }
    }

    private void OnThemeCardMenu(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button { DataContext: ThemeCardItem card, ContextMenu: { } menu })
        {
            return;
        }

        menu.Items.Clear();
        var load = new MenuItem { Header = card.IsAutomatic ? "_Turn On" : "_Load", FontWeight = FontWeights.SemiBold };
        load.Click += (_, _) => LoadCard(card);
        var manage = new MenuItem { Header = "_Manage Themes…" };
        manage.Click += (_, _) => host?.Navigate(SettingsPageId.Themes);
        menu.Items.Add(load);
        menu.Items.Add(manage);
    }

    private void OnThemeCardHot(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ThemeCardItem card })
        {
            previewCandidate = card;
            previewTimer.Stop();
            previewTimer.Start();
        }
    }

    private void OnThemeCardCold(object sender, RoutedEventArgs e)
    {
        if (ThemeCards.IsKeyboardFocusWithin || ThemeCards.IsMouseOver)
        {
            return;
        }

        previewTimer.Stop();
        previewCandidate = null;
        ShowPreview(null);
    }

    /// <summary>The stage shows a theme with the pill "Preview: Thanksgiving" (the desktop does not change), or the desktop again.</summary>
    private void ShowPreview(ThemeCardItem? card)
    {
        if (card?.Theme is not { } theme)
        {
            Stage.Arrangement = null;
            Stage.Flash = null;
            Stage.Interval = null;
            Stage.OverlayText = null;
            return;
        }

        Stage.Arrangement = theme.Arrangement;
        Stage.Flash = card.Flash;
        Stage.Interval = theme.Flash?.Interval;
        Stage.OverlayText = "Preview: " + theme.Name;
    }

    private void OnStageHit(object? sender, Preview.LightStageHitEventArgs e) => OpenBulbFactory(e.DisplayId);

    private void OnStageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            OpenBulbFactory(null);
            e.Handled = true;
        }
    }

    /// <summary>Opens Bulb Factory, previewing the clicked display.</summary>
    private void OpenBulbFactory(string? displayId)
    {
        if (host is null)
        {
            return;
        }

        host.Navigate(SettingsPageId.BulbFactory);
        if (displayId is not null && host.Window is SettingsWindow window && window.GetPage(SettingsPageId.BulbFactory) is BulbFactoryPage page)
        {
            page.PreviewDisplay(displayId);
        }
    }

    private void OnAllThemesClick(object sender, RoutedEventArgs e) => host?.Navigate(SettingsPageId.Themes);

    private void OnChangeBulbsClick(object sender, RoutedEventArgs e) => host?.Navigate(SettingsPageId.BulbFactory);

    private void OnScreenSaverClick(object sender, RoutedEventArgs e) => host?.Navigate(SettingsPageId.ScreenSaver);

    private void OnMusicBoxClick(object sender, RoutedEventArgs e) => host?.Navigate(SettingsPageId.MusicBox);

    /// <summary>The Bulb Size radios and "Standard Bulbs are 48 px tall here." for the main display (tooltip: every display).</summary>
    private void UpdateBulbSize()
    {
        BulbSize size = Services.Settings.Current.Lights.Size;
        SmallRadio.IsChecked = size == BulbSize.Small;
        StandardRadio.IsChecked = size == BulbSize.Standard;
        LargeRadio.IsChecked = size == BulbSize.Large;
        ExtraLargeRadio.IsChecked = size == BulbSize.ExtraLarge;
        if (!Services.Bulbs.TryGetBulb(BulbIds.BuiltIn("standard-bulbs"), out IBulb? bulb))
        {
            SizeCaption.Text = "";
            return;
        }

        int cell = bulb.GetCellSize(CellSlot.Top, 0, 0).Height;
        int Pixels(DisplayInfo display) => ArtScale.Scale(cell, ArtScale.Effective(display.Scale, size));
        SizeCaption.Text = string.Create(CultureInfo.CurrentCulture, $"Standard Bulbs are {Pixels(Services.Displays.Primary)} px tall here.");
        SizeCaption.ToolTip = string.Join(Environment.NewLine,
            Services.Displays.Displays.OrderBy(d => d.Number).Select(d => string.Create(CultureInfo.CurrentCulture, $"Display {d.Number}: {Pixels(d)} px")));
    }

    private void OnBulbSizeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: BulbSize size } || size == Services.Settings.Current.Lights.Size)
        {
            return;
        }

        string name = size switch
        {
            BulbSize.Small => "Small",
            BulbSize.Large => "Large",
            BulbSize.ExtraLarge => "Extra Large",
            _ => "Standard",
        };
        Services.Settings.Update(s => s with { Lights = s.Lights with { Size = size } }, SettingsChange.Edit($"Change the bulb size to {name}"));
    }

    /// <summary>The Music card: the global switch and one status line (with "Pause" and "Next Song" while playing).</summary>
    private void UpdateMusic()
    {
        AppSettings settings = Services.Settings.Current;
        MusicState state = Services.Music.State;
        MusicToggle.IsChecked = settings.Music.Enabled;
        bool settingsOnlyWithMusicOn = Services.Options.Session == AppSessionKind.SettingsOnly && settings.Music.Enabled;
        MusicText.Text = MusicStatusText.Text(state, DateTimeOffset.Now, forHome: true, MusicStatusText.PauseReason(settings.Rest, Services.Lights.Status), settingsOnlyWithMusicOn);
        bool playing = state.Status == MusicStatus.Playing;
        bool paused = state.Status == MusicStatus.Paused;

        // While the user's pause holds, the same button offers "Resume" (review r1 #35), so the music can always go on.
        PauseButton.Content = paused ? "Resume" : "Pause";
        PauseButton.Visibility = playing || paused ? Visibility.Visible : Visibility.Collapsed;
        NextSongButton.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        if (shown && state.Status == MusicStatus.BetweenSongs)
        {
            musicTimer.Start();
        }
        else
        {
            musicTimer.Stop();
        }
    }

    private void OnMusicClick(object sender, RoutedEventArgs e)
    {
        bool on = MusicToggle.IsChecked == true;
        if (on == Services.Settings.Current.Music.Enabled)
        {
            return; // Shown again after a change, or the Click after the Checked/Unchecked that applied it (review r1 #34).
        }

        Services.Settings.Update(
            s => s with
            {
                Music = s.Music with { Enabled = on },
                Current = on && s.Current.Music.Mode == PlayMode.Never ? s.Current with { Music = s.Current.Music with { Mode = PlayMode.Always } } : s.Current,
            },
            SettingsChange.Edit(on ? "Turn on holiday music" : "Turn off holiday music"));
    }

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (Services.Music.State.Status == MusicStatus.Paused)
        {
            Services.Music.Resume();
        }
        else
        {
            Services.Music.Pause();
        }
    }

    private void OnNextSongClick(object sender, RoutedEventArgs e) => Services.Music.NextSong();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        UpdateHeader();
        UpdateLights();
        if (!Equals(e.OldSettings.Current, e.NewSettings.Current) || !Equals(e.OldSettings.Calendar, e.NewSettings.Calendar)
            || !Equals(e.OldSettings.RecentSettings, e.NewSettings.RecentSettings) || e.OldSettings.Look != e.NewSettings.Look)
        {
            BuildCards();
        }

        if (e.OldSettings.Lights.Size != e.NewSettings.Lights.Size)
        {
            UpdateBulbSize();
        }

        UpdateMusic();
        Stage.SetValue(AutomationProperties.HelpTextProperty, ArrangementTexts.Summary(e.NewSettings.Current.Arrangement, NameOf));
    }

    private void OnLightsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            UpdateLights();
            UpdateBulbSize();
            UpdateMusic();
        }
    });

    private void OnMusicChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            UpdateMusic();
        }
    });

    private void OnThemesChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            UpdateHeader();
            BuildCards();
        }
    });
}
