using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using HolidayLights.App.Controls;
using HolidayLights.App.Settings.Dialogs;
using HolidayLights.App.Settings.Undo;
using HolidayLights.Audio;
using Microsoft.Win32;

namespace HolidayLights.App.Settings.Pages;

/// <summary>
/// The "Music Box" page of the Settings window (owner: settings-ui; PRODUCT-SPEC 3.4): Play Holiday Music, Now Playing,
/// songs, Play the Chosen Songs, Lights and Music, Advanced.
/// </summary>
public partial class MusicBoxPage : UserControl, ISettingsPage, IDisposable
{
    /// <summary>The "Music Files" filter of "Add Song..." (PRODUCT-SPEC 3.4.3).</summary>
    public const string MusicFilesFilter = "Music Files (*.mid; *.rmi; *.mp3; *.wav; *.wma; *.aif; *.aifc; *.aiff; *.au; *.snd; *.mpeg; *.m4a)|*.mid;*.rmi;*.mp3;*.wav;*.wma;*.aif;*.aifc;*.aiff;*.au;*.snd;*.mpeg;*.m4a";

    private static readonly (SongCategory? Category, string Name)[] Categories =
    [
        (null, "All"),
        (SongCategory.Christmas, "Christmas"),
        (SongCategory.Chanukah, "Chanukah"),
        (SongCategory.Halloween, "Halloween"),
        (SongCategory.NewYear, "New Year"),
        (SongCategory.Patriotic, "Patriotic"),
        (SongCategory.FolkAndClassics, "Folk & Classics"),
        (SongCategory.MySongs, "My Songs"),
    ];

    /// <summary>Below this page width "Play the Chosen Songs" and "Lights and Music" move under the songs.</summary>
    private const double SideBySideMinimum = 860;

    /// <summary>How long the progress bar keeps a value the user chose with a key before it follows the song again.</summary>
    private const long ProgressKeyHoldMilliseconds = 1000;

    private readonly DispatcherTimer progressTimer;
    private ISettingsHost? host;
    private SongCategory? category;
    private List<SongRow> rows = [];
    private bool updating;
    private bool disposed;
    private long progressKeyAt = -ProgressKeyHoldMilliseconds;

    /// <summary>Creates the page.</summary>
    /// <param name="services">The application services.</param>
    public MusicBoxPage(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        InitializeComponent();
        progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        progressTimer.Tick += (_, _) => UpdateNowPlaying();
        MusicOffBar.ActionCommand = new DelegateCommand(() => SetMusic(true));
        InputBindings.Add(new KeyBinding(new DelegateCommand(SongSearch.FocusAndSelect), Key.F, ModifierKeys.Control));
        services.Settings.Changed += OnSettingsChanged;
        services.Songs.Changed += OnSongsChanged;
        services.Music.StateChanged += OnMusicChanged;
        SizeChanged += (_, _) => UpdateLayoutMode();
        BuildSongs(services.Settings.Current);
        Refresh();
    }

    /// <summary>The application services.</summary>
    public IAppServices Services { get; }

    /// <inheritdoc />
    public event EventHandler? HeaderChanged;

    /// <inheritdoc />
    public SettingsPageId PageId => SettingsPageId.MusicBox;

    /// <inheritdoc />
    public string Title => "Music Box";

    /// <inheritdoc />
    public string Description => "Holiday Lights can play music in the background. It randomly chooses songs from among the checked songs.";

    /// <inheritdoc />
    public FrameworkElement? HeaderActions => null;

    /// <inheritdoc />
    public bool ScrollsAsWhole => true;

    /// <summary>The songs listed (after the category filter).</summary>
    public IReadOnlyList<SongRow> Rows => rows;

    /// <inheritdoc />
    public void Attach(ISettingsHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void OnShown()
    {
        updating = true;
        UpdateMidiDevices(Services.Settings.Current);
        updating = false;
        Refresh();
        progressTimer.Start();
    }

    /// <inheritdoc />
    public void OnHidden() => progressTimer.Stop();

    /// <inheritdoc />
    public void HandleRequest(SettingsRequest request)
    {
        if (request is OpenFilesRequest files && host is not null)
        {
            AddSongs(files.Paths);
        }
    }

    /// <inheritdoc />
    public string HelpTopicFor(DependencyObject? focused)
    {
        foreach (DependencyObject node in Controls.ElementTree.SelfAndAncestors(focused))
        {
            if (ReferenceEquals(node, SongList) || ReferenceEquals(node, Chips))
            {
                return HelpTopics.SongsOnOff;
            }

            if (node is RadioButton { GroupName: "PlayMode" })
            {
                return HelpTopics.WhenMusicPlays;
            }

            if (ReferenceEquals(node, DanceCheck))
            {
                return HelpTopics.LightsDance;
            }

            if (ReferenceEquals(node, VolumeSlider) || ReferenceEquals(node, MuteButton))
            {
                return HelpTopics.Volume;
            }
        }

        return HelpTopics.AboutMusic;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        progressTimer.Stop();
        Services.Settings.Changed -= OnSettingsChanged;
        Services.Songs.Changed -= OnSongsChanged;
        Services.Music.StateChanged -= OnMusicChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>The play mode and Lights and Music cards beside the songs, or under them on narrow windows.</summary>
    private void UpdateLayoutMode()
    {
        bool sideBySide = ActualWidth >= SideBySideMinimum;
        SideGapColumn.Width = new GridLength(sideBySide ? 16 : 0);
        SideColumn.Width = sideBySide ? new GridLength(300) : new GridLength(0);
        SideGapRow.Height = new GridLength(sideBySide ? 0 : 16);
        Grid.SetColumn(SidePanel, sideBySide ? 2 : 0);
        Grid.SetRow(SidePanel, sideBySide ? 0 : 2);
    }

    private void Refresh()
    {
        updating = true;
        try
        {
            AppSettings settings = Services.Settings.Current;
            MusicToggle.IsChecked = settings.Music.Enabled;
            MusicOffBar.IsOpen = !settings.Music.Enabled;
            VolumeSlider.Value = settings.Music.Volume;
            VolumeText.Text = string.Create(CultureInfo.CurrentCulture, $"{settings.Music.Volume} %");
            MuteButton.IsChecked = settings.Music.Muted;
            MuteGlyph.Text = settings.Music.Muted ? Glyphs.Mute : Glyphs.Volume;
            PlayMode mode = settings.Current.Music.Mode;
            NeverRadio.IsChecked = mode == PlayMode.Never;
            SaverOnRadio.IsChecked = mode == PlayMode.SaverOn;
            SaverOffRadio.IsChecked = mode == PlayMode.SaverOff;
            AlwaysRadio.IsChecked = mode == PlayMode.Always;
            IntermittentRadio.IsChecked = mode == PlayMode.Intermittently;
            DanceCheck.IsChecked = settings.Current.Flash.Pattern == FlashPatternId.DanceToMusic;
            UpdateSongChecks(settings);
        }
        finally
        {
            updating = false;
        }

        UpdateNowPlaying();
    }

    /// <summary>The category chips with counts, the list, "46 songs, 31 on" and the empty text.</summary>
    private void BuildSongs(AppSettings settings)
    {
        IReadOnlyList<SongInfo> songs = Services.Songs.Songs;
        var disabled = new HashSet<string>(settings.Current.Music.DisabledSongs, MediaIds.Comparer);
        string? playing = Services.Music.State is { Status: MusicStatus.Playing or MusicStatus.Paused, CurrentSong: { } song } ? song.Id : null;
        Chips.Children.Clear();
        foreach ((SongCategory? chipCategory, string name) in Categories)
        {
            int count = chipCategory is { } c ? songs.Count(s => s.Categories.Contains(c)) : songs.Count;
            var chip = new ToggleButton
            {
                Content = string.Create(CultureInfo.CurrentCulture, $"{name} {count}"),
                IsChecked = chipCategory == category,
                Tag = chipCategory,
            };
            chip.SetResourceReference(StyleProperty, "HL.Style.Chip");
            System.Windows.Automation.AutomationProperties.SetName(chip, string.Create(CultureInfo.CurrentCulture, $"{name}, {count} songs"));
            // Checked/Unchecked rather than Click: UI Automation's Toggle changes IsChecked without a Click. Rebuilding
            // checks exactly the chosen chip again (clicking the chosen chip keeps it chosen).
            RoutedEventHandler choose = (_, _) =>
            {
                category = (SongCategory?)chip.Tag;
                BuildSongs(Services.Settings.Current);
            };
            chip.Checked += choose;
            chip.Unchecked += choose;
            Chips.Children.Add(chip);
        }

        string? selected = (SongList.SelectedItem as SongRow)?.Id;
        string[] words = SongSearch.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        rows = [.. songs.Where(s => (category is not { } c || s.Categories.Contains(c)) && Matches(s, words))
            .Select(s => new SongRow(s, !disabled.Contains(s.Id), s.Id == playing))];
        SongList.ItemsSource = rows;
        SongList.SelectedItem = rows.FirstOrDefault(r => r.Id == selected);
        int on = songs.Count(s => !disabled.Contains(s.Id));
        SongCount.Text = string.Create(CultureInfo.CurrentCulture, $"{songs.Count} {(songs.Count == 1 ? "song" : "songs")}, {on} on");
        SongSearch.Placeholder = string.Create(CultureInfo.CurrentCulture, $"Search {songs.Count} songs");
        EmptySongs.Text = songs.Count == 0
            ? "There are no songs. Click Add Song…, or Restore Removed Songs."
            : $"No songs match \"{SongSearch.Text.Trim()}\".";
        EmptySongs.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Follows the check boxes and "46 songs, 31 on" when the unchecked songs changed (the list keeps its scroll position).</summary>
    private void UpdateSongChecks(AppSettings settings)
    {
        var disabled = new HashSet<string>(settings.Current.Music.DisabledSongs, MediaIds.Comparer);
        foreach (SongRow row in rows)
        {
            row.IsChecked = !disabled.Contains(row.Id);
        }

        IReadOnlyList<SongInfo> songs = Services.Songs.Songs;
        int on = songs.Count(s => !disabled.Contains(s.Id));
        SongCount.Text = string.Create(CultureInfo.CurrentCulture, $"{songs.Count} {(songs.Count == 1 ? "song" : "songs")}, {on} on");
    }

    /// <summary>True when every word appears in the title or the arranger (case- and accent-insensitive).</summary>
    private static bool Matches(SongInfo song, string[] words)
    {
        CompareInfo compare = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return words.All(w => compare.IndexOf(song.Title, w, options) >= 0 || compare.IndexOf(song.Arranger ?? "", w, options) >= 0);
    }

    private void OnSongSearchChanged(object? sender, EventArgs e) => BuildSongs(Services.Settings.Current);

    /// <summary>Title, arranger and progress while a song plays; else the status text; the pause button and the problem InfoBars.</summary>
    private void UpdateNowPlaying()
    {
        MusicState state = Services.Music.State;
        bool hasSong = state is { Status: MusicStatus.Playing or MusicStatus.Paused, CurrentSong: not null };
        if (hasSong && state.CurrentSong is { } song)
        {
            SongTitle.Text = song.Title;
            SongArranger.Text = song.Arranger is { Length: > 0 } arranger
                ? (song.Origin == MediaOrigin.Bundled ? "Arranged by " + arranger : arranger)
                : "";
            double length = song.Length?.TotalSeconds ?? 0;
            ProgressSlider.Maximum = Math.Max(1, length);
            if (!IsUserMovingProgress())
            {
                ProgressSlider.Value = Math.Min(length, state.Position.TotalSeconds);
            }

            ProgressSlider.IsEnabled = state.CanSeek;
            ElapsedText.Text = MusicStatusText.Duration(state.Position);
            LengthText.Text = song.Length is { } total ? MusicStatusText.Duration(total) : "";
            ProgressPanel.Visibility = Visibility.Visible;
        }
        else
        {
            MusicPauseReason pause = MusicStatusText.PauseReason(Services.Settings.Current.Rest, Services.Lights.Status);
            bool settingsOnly = Services.Options.Session == AppSessionKind.SettingsOnly;
            SongTitle.Text = MusicStatusText.Text(state, DateTimeOffset.Now, forHome: false, pause, settingsOnly && Services.Settings.Current.Music.Enabled);
            SongArranger.Text = "";
            ProgressPanel.Visibility = Visibility.Collapsed;
        }

        bool paused = state.Status == MusicStatus.Paused;
        PauseButton.Content = paused ? "_Resume" : "_Pause";
        Controls.ButtonContent.SetGlyph(PauseButton, paused ? Glyphs.Play : Glyphs.Pause);

        // "Resume" stays available while the music is paused, also when the paused song went away (unchecked, removed,
        // another theme loaded): it then lets the next song start.
        PauseButton.IsEnabled = hasSong || paused;
        PreviousButton.IsEnabled = hasSong;
        // A settings-only session plays only what Play Now asks for: there is no "next" song (review r1 #62).
        NextButton.IsEnabled = Services.Settings.Current.Music.Enabled && Services.Options.Session != AppSessionKind.SettingsOnly;
        UpdateProblem(state);
        foreach (SongRow row in rows)
        {
            row.IsPlaying = hasSong && row.Id == state.CurrentSong?.Id;
        }
    }

    /// <summary>
    /// True while the user moves the progress bar: dragging the thumb, holding the button after a click on the bar, or
    /// within a second of an arrow key (the key's seek follows on key up). Otherwise the bar follows the song, also while
    /// it has keyboard focus.
    /// </summary>
    private bool IsUserMovingProgress() =>
        ProgressSlider.IsMouseCaptureWithin
        || (ProgressSlider.IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed)
        || Environment.TickCount64 - progressKeyAt < ProgressKeyHoldMilliseconds;

    /// <summary>The engine problems of PRODUCT-SPEC 3.4.6 as one InfoBar (Add Song's results use <c>ImportBar</c>).</summary>
    private void UpdateProblem(MusicState state)
    {
        (string Text, string? Action, Action? Run)? problem = state switch
        {
            { Status: MusicStatus.WaitingForSynthesizer } => ("Another app is using the MIDI synthesizer, so MIDI songs can't play right now. Holiday Lights tries again every 30 seconds.", "Try Now", Services.Music.RetryNow),
            { Status: MusicStatus.StoppedAfterFailures } => ("Music stopped because several songs couldn't be played.", "Try Again", Services.Music.RetryNow),
            { Status: MusicStatus.NoOutputDevice } => ("No speakers or headphones are available. Music will start when one is connected.", null, null),
            { MutedInMixer: true } => ("Holiday Lights is muted in the Windows volume mixer.", "Open Volume Mixer", () => Services.Shell.OpenSettingsUri("ms-settings:apps-volume")),
            _ => null,
        };
        if (problem is not { } p)
        {
            ProblemBar.IsOpen = false;
            return;
        }

        ProblemBar.Message = p.Text;
        ProblemBar.ActionText = p.Action;
        ProblemBar.ActionCommand = p.Run is { } run ? new DelegateCommand(run) : null;
        ProblemBar.IsOpen = true;
    }

    private void UpdateMidiDevices(AppSettings settings)
    {
        IReadOnlyList<string> devices = Services.Music.GetMidiOutputDevices();
        MidiBox.ItemsSource = devices;
        string? automatic = devices.FirstOrDefault(d => d.Contains("GS Wavetable", StringComparison.OrdinalIgnoreCase)) ?? devices.FirstOrDefault();
        string chosen = settings.Music.MidiDevice;
        MidiBox.SelectedItem = devices.FirstOrDefault(d => string.Equals(d, chosen, StringComparison.OrdinalIgnoreCase)) ?? automatic;
    }

    /// <summary>
    /// The music switch. Click, Checked and Unchecked all come here: UI Automation's Toggle (Narrator, Voice Access) and
    /// the + and - keys change IsChecked without a Click. Only a real difference is applied, so a click's second event and
    /// the page's own refresh change nothing.
    /// </summary>
    private void OnMusicToggled(object sender, RoutedEventArgs e)
    {
        bool on = MusicToggle.IsChecked == true;
        if (!updating && on != Services.Settings.Current.Music.Enabled)
        {
            SetMusic(on);
        }
    }

    /// <summary>The global music switch; turning it on while the play mode is "Never" sets "Always".</summary>
    private void SetMusic(bool on) => Services.Settings.Update(
        s => s with
        {
            Music = s.Music with { Enabled = on },
            Current = on && s.Current.Music.Mode == PlayMode.Never ? s.Current with { Music = s.Current.Music with { Mode = PlayMode.Always } } : s.Current,
        },
        SettingsChange.Edit(on ? "Turn on holiday music" : "Turn off holiday music"));

    private void OnPreviousClick(object sender, RoutedEventArgs e) => Services.Music.Previous();

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

    private void OnNextClick(object sender, RoutedEventArgs e) => Services.Music.NextSong();

    private void OnProgressReleased(object sender, MouseButtonEventArgs e) => Seek();

    private void OnProgressKeyDown(object sender, KeyEventArgs e)
    {
        if (IsProgressKey(e.Key))
        {
            progressKeyAt = Environment.TickCount64;
        }
    }

    private void OnProgressKeyUp(object sender, KeyEventArgs e)
    {
        if (IsProgressKey(e.Key))
        {
            progressKeyAt = Environment.TickCount64;
            Seek();
        }
    }

    /// <summary>The keys a slider moves its value with.</summary>
    private static bool IsProgressKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;

    private void Seek()
    {
        if (Services.Music.State.CanSeek)
        {
            Services.Music.Seek(TimeSpan.FromSeconds(ProgressSlider.Value));
        }
    }

    /// <summary>"Mute" (Click, Checked and Unchecked; see <see cref="OnMusicToggled"/>).</summary>
    private void OnMuteToggled(object sender, RoutedEventArgs e)
    {
        bool muted = MuteButton.IsChecked == true;
        if (!updating && muted != Services.Settings.Current.Music.Muted)
        {
            Services.Settings.Update(s => s with { Music = s.Music with { Muted = muted } }, SettingsChange.Edit(muted ? "Mute the music" : "Unmute the music"));
        }
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updating)
        {
            return;
        }

        int volume = (int)Math.Round(VolumeSlider.Value);
        VolumeText.Text = string.Create(CultureInfo.CurrentCulture, $"{volume} %");
        Services.Settings.Update(s => s with { Music = s.Music with { Volume = volume } }, SettingsChange.Edit("Change the volume"));
    }

    private void OnVolumeMixerClick(object sender, RoutedEventArgs e) => Services.Shell.OpenSettingsUri("ms-settings:apps-volume");

    /// <summary>
    /// A song's check box (Click, Checked and Unchecked; see <see cref="OnMusicToggled"/>): applies the box's state when it
    /// differs from the song's. Realized rows bind the song's state, which is never a difference.
    /// </summary>
    private void OnSongCheckChanged(object sender, RoutedEventArgs e)
    {
        if (!updating && sender is CheckBox { DataContext: SongRow row } box && (box.IsChecked == true) != row.IsChecked)
        {
            SetSongChecked(row, box.IsChecked == true);
        }
    }

    /// <summary>Checks or unchecks a song (the settings store the unchecked songs, 5.4).</summary>
    private void SetSongChecked(SongRow row, bool on)
    {
        string id = row.Id;
        Services.Settings.Update(
            s =>
            {
                bool isOff = s.Current.Music.DisabledSongs.Contains(id, MediaIds.Comparer);
                if (on != isOff)
                {
                    return s;
                }

                return s with
                {
                    Current = s.Current with
                    {
                        Music = s.Current.Music with
                        {
                            DisabledSongs = on
                                ? [.. s.Current.Music.DisabledSongs.Where(d => !MediaIds.Comparer.Equals(d, id))]
                                : [.. s.Current.Music.DisabledSongs, id],
                        },
                    },
                };
            },
            SettingsChange.Edit(on ? $"Turn on {row.Title}" : $"Turn off {row.Title}"));
    }

    private void OnPlayNowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SongRow row })
        {
            Services.Music.PlayNow(row.Id);
        }
    }

    /// <summary>
    /// A double-click on a row plays its song. The row raises it even when its check box or play button handled the
    /// clicks; those act on their own clicks only (a quick double-click on a check box toggles it twice and plays nothing).
    /// </summary>
    private void OnSongDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: SongRow row } item && e.ChangedButton == MouseButton.Left
            && !IsOnRowButton(e.OriginalSource as DependencyObject, item))
        {
            Services.Music.PlayNow(row.Id);
            e.Handled = true;
        }
    }

    /// <summary>True when an element is a button or check box of a row, or lies inside one.</summary>
    private static bool IsOnRowButton(DependencyObject? source, ListBoxItem row) =>
        Controls.ElementTree.SelfAndAncestors(source).TakeWhile(node => !ReferenceEquals(node, row)).Any(node => node is ButtonBase);

    /// <summary>Space toggles the check box (5.4); Enter plays; Delete removes.</summary>
    private void OnSongKeyDown(object sender, KeyEventArgs e)
    {
        if (SongList.SelectedItem is not SongRow row || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                SetSongChecked(row, !row.IsChecked);
                e.Handled = true;
                break;
            case Key.Enter:
                Services.Music.PlayNow(row.Id);
                e.Handled = true;
                break;
            case Key.Delete:
                RemoveSong(row);
                e.Handled = true;
                break;
        }
    }

    /// <summary>"Play Now" (bold, 5.4), "Remove Song" (5.4), "Show in Folder".</summary>
    private void OnSongMenuOpening(object sender, ContextMenuEventArgs e)
    {
        ContextMenu menu = SongList.ContextMenu;
        menu.Items.Clear();
        SongRow? row = e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(SongList, source) is ListBoxItem { DataContext: SongRow found }
            ? found
            : SongList.SelectedItem as SongRow;
        if (row is null)
        {
            e.Handled = true;
            return;
        }

        SongList.SelectedItem = row;
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        Item("_Play Now", () => Services.Music.PlayNow(row.Id)).FontWeight = FontWeights.SemiBold;
        Item("_Remove Song", () => RemoveSong(row)).InputGestureText = "Delete";
        Item("Show in _Folder", () => Services.Shell.ShowInFolder(row.Song.FilePath));
    }

    /// <summary>
    /// "Remove Song": your song goes to the holding folder (snackbar "Removed &lt;title&gt;." [Undo]; Cancel brings it
    /// back); a bundled song is hidden.
    /// </summary>
    /// <param name="row">The song.</param>
    internal void RemoveSong(SongRow row)
    {
        if (host is null)
        {
            return;
        }

        string id = row.Id;
        string title = row.Title;
        HeldItem? held;
        try
        {
            held = Services.Songs.Remove(id);
        }
        catch (Exception e) when (FileProblems.Is(e))
        {
            // Nothing was removed: no undo step, no "Removed" (PRODUCT-SPEC 5.13; review r1 #40). Song names are personal.
            Services.Log.Warn("Settings.MusicBox", "A song could not be removed.", e);
            host.ShowMessage(FileProblems.Sentence($"Couldn't remove {title}", e), InfoBarSeverity.Error);
            return;
        }

        if (held is not null)
        {
            HeldItem current = held;
            host.History.Record(
                new UndoStep($"Remove {title}", () => Services.Songs.Restore(current), () => current = Services.Songs.Remove(id) ?? current),
                UndoCancelBehavior.RevertOnCancel);
            host.ShowSnackbar($"Removed {title}.", "Undo", host.UndoLast);
        }
        else
        {
            host.ShowSnackbar("Bundled songs aren't deleted. Use Restore Removed Songs to bring them back.", "Undo", host.UndoLast);
        }

        host.Announce($"Removed {title}.");
    }

    /// <summary>"Check All" / "Check None": the songs shown (one undo step).</summary>
    private void SetShownChecked(bool on)
    {
        string[] shown = [.. rows.Select(r => r.Id)];
        Services.Settings.Update(
            s =>
            {
                var disabled = new HashSet<string>(s.Current.Music.DisabledSongs, MediaIds.Comparer);
                if (on)
                {
                    disabled.ExceptWith(shown);
                }
                else
                {
                    disabled.UnionWith(shown);
                }

                return s with { Current = s.Current with { Music = s.Current.Music with { DisabledSongs = [.. disabled.Order(StringComparer.OrdinalIgnoreCase)] } } };
            },
            SettingsChange.Edit(on ? "Check all songs" : "Check no songs"));
    }

    private void OnCheckAllClick(object sender, RoutedEventArgs e) => SetShownChecked(true);

    private void OnCheckNoneClick(object sender, RoutedEventArgs e) => SetShownChecked(false);

    private void OnAddSongClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Add Song", Filter = MusicFilesFilter, Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            AddSongs(dialog.FileNames);
        }
    }

    /// <summary>
    /// Copies songs into My Music (they start checked); "&lt;name&gt; is already in your Music Box." for duplicates and
    /// "Couldn't copy ..." for failures, in their own InfoBar, which stays until it is closed or songs are added again (the
    /// engine problems' bar follows the music state every half second).
    /// </summary>
    private void AddSongs(IReadOnlyList<string> paths)
    {
        if (host is null)
        {
            return;
        }

        IReadOnlyList<MediaImportResult> results = MediaImport.AddSongs(Services, host, paths);
        if (MediaImport.Problem(results, name => $"{name} is already in your Music Box.") is { } problem)
        {
            ImportBar.Message = problem;
            ImportBar.IsOpen = true;
        }
        else
        {
            ImportBar.IsOpen = false;
        }
    }

    /// <summary>"Open My Music Folder", "Restore Removed Songs" (only when bundled songs were removed).</summary>
    private void OnSongsMoreClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SongsMoreButton, Placement = PlacementMode.Bottom };
        var open = new MenuItem { Header = "Open My _Music Folder" };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(Services.Paths.MyMusicFolder);
            Services.Shell.OpenFolder(Services.Paths.MyMusicFolder);
        };
        menu.Items.Add(open);
        if (Services.Songs.HiddenSongs.Count > 0)
        {
            var restore = new MenuItem { Header = "_Restore Removed Songs" };
            restore.Click += (_, _) => Services.Songs.RestoreHiddenSongs();
            menu.Items.Add(restore);
        }

        menu.IsOpen = true;
    }

    /// <summary>The play mode radios (Click and Checked; UI Automation's Select checks a radio without a Click).</summary>
    private void OnPlayModeChosen(object sender, RoutedEventArgs e)
    {
        if (!updating && sender is RadioButton { Tag: PlayMode mode } && mode != Services.Settings.Current.Current.Music.Mode)
        {
            string name = mode switch
            {
                PlayMode.Never => "Never",
                PlayMode.SaverOn => "Only When the Screen Saver is On",
                PlayMode.SaverOff => "Only When the Screen Saver is Off",
                PlayMode.Intermittently => "Intermittently",
                _ => "Always",
            };
            Services.Settings.Update(s => s with { Current = s.Current with { Music = s.Current.Music with { Mode = mode } } }, SettingsChange.Edit($"Play the chosen songs: {name}"));
        }
    }

    /// <summary>
    /// Checking sets "Dance to the Music" and remembers the previous pattern; unchecking restores it (Click, Checked and
    /// Unchecked; see <see cref="OnMusicToggled"/>).
    /// </summary>
    private void OnDanceToggled(object sender, RoutedEventArgs e)
    {
        bool dance = DanceCheck.IsChecked == true;
        if (updating || dance == (Services.Settings.Current.Current.Flash.Pattern == FlashPatternId.DanceToMusic))
        {
            return;
        }

        Services.Settings.Update(
            s =>
            {
                FlashPatternId pattern = s.Current.Flash.Pattern;
                return dance
                    ? s with
                    {
                        Current = s.Current with { Flash = s.Current.Flash with { Pattern = FlashPatternId.DanceToMusic } },
                        Lights = pattern == FlashPatternId.DanceToMusic ? s.Lights : s.Lights with { PatternBeforeDance = pattern },
                    }
                    : s with { Current = s.Current with { Flash = s.Current.Flash with { Pattern = s.Lights.PatternBeforeDance ?? FlashPatternId.FlashTogether } } };
            },
            SettingsChange.Edit(dance ? "Make the lights dance to the music" : "Stop making the lights dance to the music"));
    }

    private void OnMidiChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updating && MidiBox.SelectedItem is string device)
        {
            Services.Settings.Update(s => s with { Music = s.Music with { MidiDevice = device } }, SettingsChange.Edit($"Use {device} for MIDI songs"));
        }
    }

    private void OnLoadThemeClick(object sender, RoutedEventArgs e) => host?.Navigate(SettingsPageId.Themes);

    private void OnSaveThemeClick(object sender, RoutedEventArgs e)
    {
        if (host is not null)
        {
            SaveThemeDialog.Show(host, Services);
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Refresh();

    private void OnSongsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            BuildSongs(Services.Settings.Current);
            Refresh();
        }
    });

    private void OnMusicChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            UpdateNowPlaying();
        }
    });
}
