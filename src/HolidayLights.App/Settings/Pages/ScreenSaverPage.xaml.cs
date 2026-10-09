using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HolidayLights.App.Controls;
using HolidayLights.App.Settings.Dialogs;
using HolidayLights.App.Settings.Undo;
using Microsoft.Win32;

namespace HolidayLights.App.Settings.Pages;

/// <summary>A tile of the Animation or Background Picture list.</summary>
/// <param name="Key">The animation value or picture id ("(None)" for none).</param>
/// <param name="Name">The caption and accessible name.</param>
/// <param name="Tip">The tooltip, or null.</param>
/// <param name="Art">The picture on the night well.</param>
public sealed record SaverTile(string Key, string Name, string? Tip, FrameworkElement Art);

/// <summary>
/// The "Screen Saver" page of the Settings window (owner: settings-ui; PRODUCT-SPEC 3.5): what the screen saver shows,
/// and making Holiday Lights the Windows screen saver only when asked.
/// </summary>
public partial class ScreenSaverPage : UserControl, ISettingsPage, IDisposable
{
    /// <summary>The "Picture Files" filter of "Add..." (PRODUCT-SPEC 3.5.6).</summary>
    public const string PictureFilesFilter = "Picture Files (*.bmp; *.jpg; *.jpeg; *.gif; *.png)|*.bmp;*.jpg;*.jpeg;*.gif;*.png";

    /// <summary>The "Start After" choices in minutes (PRODUCT-SPEC 3.5.2).</summary>
    public static readonly IReadOnlyList<int> StartAfterMinutes = [1, 2, 3, 5, 10, 15, 20, 25, 30, 45, 60, 120];

    private static readonly int[] Sizes = [18, 20, 24, 28, 32, 36, 40, 48, 60, 72, 96, 100];

    private readonly Dictionary<string, FrameworkElement> animationArt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SaverTile> pictureTiles = new(StringComparer.OrdinalIgnoreCase);
    private SaverTile? noneTile;
    private string? shownAnimation;
    private string? shownPicture;
    private ISettingsHost? host;
    private ScreenSaverStatus? status;
    private bool updating;
    private bool disposed;
    private bool previewCreated;

    /// <summary>Creates the page.</summary>
    /// <param name="services">The application services.</param>
    public ScreenSaverPage(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        InitializeComponent();
        SizeBox.ItemsSource = Sizes;
        FontBox.SelectionChanged += (_, _) => OnFontChanged();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _ = PreviewAsync();
                e.Handled = true;
            }
        };
        services.Settings.Changed += OnSettingsChanged;
        services.Pictures.Changed += OnPicturesChanged;
        services.Bulbs.Changed += OnCatalogChanged;
        Refresh();
    }

    /// <summary>The application services.</summary>
    public IAppServices Services { get; }

    /// <inheritdoc />
    public event EventHandler? HeaderChanged;

    /// <inheritdoc />
    public SettingsPageId PageId => SettingsPageId.ScreenSaver;

    /// <inheritdoc />
    public string Title => "Screen Saver";

    /// <inheritdoc />
    public string Description => "When your computer is idle, Holiday Lights can show your bulbs around falling snow or other animations.";

    /// <inheritdoc />
    public FrameworkElement? HeaderActions => null;

    /// <inheritdoc />
    public bool ScrollsAsWhole => true;

    /// <inheritdoc />
    public void Attach(ISettingsHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        host.Window.Activated += (_, _) => UpdateStatus();
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void OnShown()
    {
        if (!previewCreated)
        {
            previewCreated = true;
            PreviewWell.Child = Services.ScreenSaver.CreatePreview();
        }

        Refresh();
        ShowSelectedTile(AnimationList);
        ShowSelectedTile(PictureList);
    }

    /// <inheritdoc />
    public void OnHidden()
    {
    }

    /// <inheritdoc />
    public void HandleRequest(SettingsRequest request)
    {
        if (request is OpenFilesRequest files)
        {
            AddPictures(files.Paths);
        }
    }

    /// <inheritdoc />
    public string HelpTopicFor(DependencyObject? focused)
    {
        foreach (DependencyObject node in Controls.ElementTree.SelfAndAncestors(focused))
        {
            if (ReferenceEquals(node, AnimationList) || ReferenceEquals(node, StyleBox))
            {
                return HelpTopics.AnimationsAndStyles;
            }

            if (ReferenceEquals(node, PictureList) || ReferenceEquals(node, PlacementBox))
            {
                return HelpTopics.BackgroundPictures;
            }

            if (ReferenceEquals(node, MessageBox) || ReferenceEquals(node, FontBox) || ReferenceEquals(node, SizeBox))
            {
                return HelpTopics.TextMessage;
            }

            if (ReferenceEquals(node, ShowOnBox))
            {
                return HelpTopics.ScreenSaverDisplays;
            }

            if (ReferenceEquals(node, StatusCard))
            {
                return HelpTopics.ScreenSaverOnOff;
            }
        }

        return HelpTopics.AboutScreenSaver;
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
        Services.Pictures.Changed -= OnPicturesChanged;
        Services.Bulbs.Changed -= OnCatalogChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// "Leading spaces and blank lines are removed as you type (5.4)": like 5.4 (on EN_UPDATE), every
    /// character below "!" at the start of the text goes; blank lines between the lines of the message stay.
    /// </summary>
    /// <param name="text">The typed text.</param>
    /// <returns>The message.</returns>
    public static string CleanMessage(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int start = 0;
        while (start < text.Length && text[start] < '!')
        {
            start++;
        }

        return text[start..];
    }

    private SaverLook Look => Services.Settings.Current.Current.Saver;

    private void Update(Func<SaverLook, SaverLook> transform, string description)
    {
        if (!updating)
        {
            Services.Settings.Update(s => s with { Current = s.Current with { Saver = transform(s.Current.Saver) } }, SettingsChange.Edit(description));
        }
    }

    private void Refresh()
    {
        updating = true;
        try
        {
            AppSettings settings = Services.Settings.Current;
            SaverLook look = settings.Current.Saver;
            if (CleanMessage(MessageBox.Text) != look.Message)
            {
                MessageBox.Text = look.Message;
            }

            FontBox.Family = look.Font.Family;
            SizeBox.Text = look.Font.SizePt.ToString(CultureInfo.CurrentCulture);
            BoldButton.IsChecked = look.Font.Bold;
            ItalicButton.IsChecked = look.Font.Italic;
            UnderlineButton.IsChecked = look.Font.Underline;
            StrikeoutButton.IsChecked = look.Font.Strikeout;
            TextSwatch.Background = Brush(look.Color);
            BackgroundSwatch.Background = Brush(look.Background);
            BuildAnimations(look);
            BuildPictures(look);
            PlacementBox.SelectedItem = PlacementBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, look.Placement));
            ShowOnBox.SelectedItem = ShowOnBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, settings.Saver.ShowOn));
            SmoothMotionCheck.IsChecked = settings.Look.SmoothSaverMotion;
        }
        finally
        {
            updating = false;
        }

        UpdateStatus();
    }

    private static SolidColorBrush Brush(RgbColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>The status card (PRODUCT-SPEC 3.5.2): read on page show, on window activation and after every change.</summary>
    private void UpdateStatus()
    {
        status = Services.ScreenSaverRegistration.GetStatus();
        (string text, InfoBarSeverity severity) = status switch
        {
            { PolicyManaged: true } => ("Your organization manages the screen saver on this PC.", InfoBarSeverity.Informational),
            { State: ScreenSaverState.Ours } => ("Holiday Lights is your screen saver.", InfoBarSeverity.Success),
            { State: ScreenSaverState.Legacy54 } => ("Your screen saver is still set to Holiday Lights 5.4, which can't start on this version of Windows.", InfoBarSeverity.Warning),
            { State: ScreenSaverState.OursButTurnedOff } => ("Holiday Lights is selected, but the screen saver is turned off.", InfoBarSeverity.Warning),
            _ => ("Holiday Lights isn't your screen saver.", InfoBarSeverity.Informational),
        };
        StatusText.Text = text;
        StatusCircle.SetResourceReference(TextBlock.ForegroundProperty, severity switch
        {
            InfoBarSeverity.Success => "SystemFillColorSuccessBrush",
            InfoBarSeverity.Warning => "SystemFillColorCautionBrush",
            _ => "AccentFillColorDefaultBrush",
        });
        StatusGlyph.Text = severity switch
        {
            InfoBarSeverity.Success => Glyphs.InfoBarSuccess,
            InfoBarSeverity.Warning => Glyphs.InfoBarWarning,
            _ => Glyphs.InfoBarInformational,
        };
        StatusCard.SetResourceReference(Border.BackgroundProperty, severity switch
        {
            InfoBarSeverity.Success => "InfoBarSuccessSeverityBackgroundBrush",
            InfoBarSeverity.Warning => "InfoBarWarningSeverityBackgroundBrush",
            _ => "InfoBarInformationalSeverityBackgroundBrush",
        });
        bool managed = status.PolicyManaged;
        UseButton.Visibility = !managed && status.State == ScreenSaverState.NotOurs ? Visibility.Visible : Visibility.Collapsed;
        UseNewButton.Visibility = !managed && status.State == ScreenSaverState.Legacy54 ? Visibility.Visible : Visibility.Collapsed;
        TurnOnButton.Visibility = !managed && status.State == ScreenSaverState.OursButTurnedOff ? Visibility.Visible : Visibility.Collapsed;
        bool ours = !managed && status.State == ScreenSaverState.Ours;
        StartAfterPanel.Visibility = ours ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = ours ? Visibility.Visible : Visibility.Collapsed;
        if (ours)
        {
            updating = true;
            try
            {
                int minutes = Math.Max(1, (int)Math.Round(status.TimeoutSeconds / 60.0));
                List<int> choices = [.. StartAfterMinutes];
                if (!choices.Contains(minutes))
                {
                    choices.Add(minutes);
                    choices.Sort();
                }

                StartAfterBox.ItemsSource = choices.Select(m => new ComboBoxItem { Content = MinutesText(m), Tag = m }).ToArray();
                StartAfterBox.SelectedItem = StartAfterBox.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == minutes);
            }
            finally
            {
                updating = false;
            }
        }
    }

    /// <summary>"1 minute", "45 minutes", "1 hour", "2 hours".</summary>
    /// <param name="minutes">The time.</param>
    /// <returns>The text.</returns>
    public static string MinutesText(int minutes) => minutes switch
    {
        1 => "1 minute",
        60 => "1 hour",
        _ when minutes % 60 == 0 => string.Create(CultureInfo.CurrentCulture, $"{minutes / 60} hours"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{minutes} minutes"),
    };

    /// <summary>"Use Holiday Lights as My Screen Saver" / "Use the New Screen Saver": one undo step; Cancel undoes it.</summary>
    private void OnUseClick(object sender, RoutedEventArgs e)
    {
        ScreenSaverPrevious previous = Services.ScreenSaverRegistration.Use();
        Services.Settings.Update(s => s with { Saver = s.Saver with { Previous = previous } }, SettingsChange.Internal);
        host?.History.Record(
            new UndoStep("Use Holiday Lights as the screen saver", () => Services.ScreenSaverRegistration.StopUsing(previous), () => Services.ScreenSaverRegistration.Use()),
            UndoCancelBehavior.RevertOnCancel);
        host?.Announce("Holiday Lights is your screen saver.");
        UpdateStatus();
    }

    /// <summary>"Stop Using It": the remembered values come back.</summary>
    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        ScreenSaverPrevious? previous = Services.Settings.Current.Saver.Previous;
        Services.ScreenSaverRegistration.StopUsing(previous);
        host?.History.Record(
            new UndoStep("Stop using Holiday Lights as the screen saver", () => Services.ScreenSaverRegistration.Use(), () => Services.ScreenSaverRegistration.StopUsing(previous)),
            UndoCancelBehavior.RevertOnCancel);
        host?.Announce("Holiday Lights isn't your screen saver anymore.");
        UpdateStatus();
    }

    /// <summary>"Turn It On": <c>ScreenSaveActive</c> = 1 (undo turns it off again, keeping the saver).</summary>
    private void OnTurnOnClick(object sender, RoutedEventArgs e)
    {
        if (status is not { } before)
        {
            return;
        }

        Services.ScreenSaverRegistration.TurnOn();
        var off = new ScreenSaverPrevious { ScrnsaveExe = before.ScrnsaveExe, Active = false, TimeoutSeconds = before.TimeoutSeconds };
        host?.History.Record(
            new UndoStep("Turn on the screen saver", () => Services.ScreenSaverRegistration.StopUsing(off), () => Services.ScreenSaverRegistration.TurnOn()),
            UndoCancelBehavior.RevertOnCancel);
        UpdateStatus();
    }

    /// <summary>"Start After": writes the Windows timeout (one undo step).</summary>
    private void OnStartAfterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || status is not { } before || StartAfterBox.SelectedItem is not ComboBoxItem { Tag: int minutes })
        {
            return;
        }

        TimeSpan old = TimeSpan.FromSeconds(before.TimeoutSeconds);
        TimeSpan chosen = TimeSpan.FromMinutes(minutes);
        Services.ScreenSaverRegistration.SetTimeout(chosen);
        host?.History.Record(
            new UndoStep($"Start the screen saver after {MinutesText(minutes)}", () => Services.ScreenSaverRegistration.SetTimeout(old), () => Services.ScreenSaverRegistration.SetTimeout(chosen)),
            UndoCancelBehavior.RevertOnCancel);
        UpdateStatus();
    }

    private void OnWindowsSettingsClick(object sender, RoutedEventArgs e) => Services.ScreenSaverRegistration.OpenWindowsSettings();

    private void OnPreviewClick(object sender, RoutedEventArgs e) => _ = PreviewAsync();

    /// <summary>"Preview Screen Saver": the real saver, full screen, without changing any Windows setting.</summary>
    private async Task PreviewAsync()
    {
        if (!Services.ScreenSaver.IsPreviewRunning)
        {
            try
            {
                await Services.ScreenSaver.RunPreviewAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The screen saver has logged the error and reported the session stopped (review r1 #73).
                MessageBar.Severity = InfoBarSeverity.Error;
                MessageBar.Message = "The screen saver couldn't start.";
                MessageBar.IsOpen = true;
            }
        }
    }

    private void OnMessageChanged(object sender, TextChangedEventArgs e)
    {
        if (updating)
        {
            return;
        }

        string cleaned = CleanMessage(MessageBox.Text);
        if (cleaned != MessageBox.Text)
        {
            int caret = Math.Max(0, MessageBox.CaretIndex - (MessageBox.Text.Length - cleaned.Length));
            updating = true;
            MessageBox.Text = cleaned;
            MessageBox.CaretIndex = Math.Min(caret, cleaned.Length);
            updating = false;
        }

        Update(l => l with { Message = cleaned }, "Change the screen saver message");
    }

    private void OnClearMessageClick(object sender, RoutedEventArgs e)
    {
        Update(l => l with { Message = "" }, "Clear the screen saver message");
        MessageBox.Focus();
    }

    private void OnFontChanged()
    {
        string family = FontBox.Family;
        if (!string.Equals(family, Look.Font.Family, StringComparison.Ordinal))
        {
            Update(l => l with { Font = l.Font with { Family = family } }, $"Use the {family} font");
        }
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SizeBox.SelectedItem is int size)
        {
            SetSize(size);
        }
    }

    private void OnSizeCommitted(object sender, KeyboardFocusChangedEventArgs e) => CommitTypedSize();

    private void OnSizeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTypedSize();
            e.Handled = true;
        }
    }

    /// <summary>Any typed size in 18-100 pt is accepted (5.4 limits); other text reverts.</summary>
    private void CommitTypedSize()
    {
        if (int.TryParse(SizeBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int size) && size is >= 18 and <= 100)
        {
            SetSize(size);
        }
        else
        {
            SizeBox.Text = Look.Font.SizePt.ToString(CultureInfo.CurrentCulture);
        }
    }

    private void SetSize(int size)
    {
        if (size != Look.Font.SizePt)
        {
            Update(l => l with { Font = l.Font with { SizePt = size } }, string.Create(CultureInfo.CurrentCulture, $"Change the text size to {size} pt"));
        }
    }

    /// <summary>
    /// Bold, Italic, Underline and Strikeout. Click, Checked and Unchecked all come here: UI Automation's Toggle changes
    /// IsChecked without a Click. Only a real difference is applied.
    /// </summary>
    private void OnFontStyleToggled(object sender, RoutedEventArgs e)
    {
        bool on = ((ToggleButton)sender).IsChecked == true;
        SaverFont font = Look.Font;
        bool current = ReferenceEquals(sender, BoldButton) ? font.Bold
            : ReferenceEquals(sender, ItalicButton) ? font.Italic
            : ReferenceEquals(sender, UnderlineButton) ? font.Underline
            : font.Strikeout;
        if (on == current)
        {
            return;
        }

        string name = System.Windows.Automation.AutomationProperties.GetName((ToggleButton)sender);
        Update(
            l => l with
            {
                Font = ReferenceEquals(sender, BoldButton) ? l.Font with { Bold = on }
                    : ReferenceEquals(sender, ItalicButton) ? l.Font with { Italic = on }
                    : ReferenceEquals(sender, UnderlineButton) ? l.Font with { Underline = on }
                    : l.Font with { Strikeout = on },
            },
            $"Turn {name} {(on ? "on" : "off")}");
    }

    private void OnTextColorClick(object sender, RoutedEventArgs e)
    {
        if (host is not null && ColorDialog.Show(host, Services, Look.Color) is { } color)
        {
            Update(l => l with { Color = color }, "Change the text color");
        }
    }

    private void OnBackgroundColorClick(object sender, RoutedEventArgs e)
    {
        if (host is not null && ColorDialog.Show(host, Services, Look.Background) is { } color)
        {
            Update(l => l with { Background = color }, "Change the background color");
        }
    }

    /// <summary>The 25 animations in the 5.4 order, plus "Bulb: &lt;name&gt;" when a bulb is the animation.</summary>
    private void BuildAnimations(SaverLook look)
    {
        var tiles = SaverAnimations.All.Select(a => new SaverTile(a, a, null, ArtFor(a))).ToList();
        bool missingBulb = false;
        if (SaverAnimations.TryGetBulbId(look.Animation, out string? bulbId))
        {
            if (Services.Bulbs.TryGetInfo(bulbId, out BulbInfo? info))
            {
                tiles.Add(new SaverTile(look.Animation, "Bulb: " + info.Name, info.Name, ArtFor(look.Animation)));
            }
            else
            {
                missingBulb = Services.Bulbs.Status.IsComplete;
            }
        }

        ShowTiles(AnimationList, tiles);
        AnimationList.SelectedItem = AnimationList.Items.Cast<SaverTile>().FirstOrDefault(t => t.Key == look.Animation) ?? (missingBulb ? tiles[0] : null);
        if (!string.Equals(shownAnimation, look.Animation, StringComparison.Ordinal))
        {
            shownAnimation = look.Animation;
            ShowSelectedTile(AnimationList);
        }

        AnimationBar.IsOpen = missingBulb;
        bool own = SaverAnimations.HasOwnMovement(look.Animation) || missingBulb;
        StyleBox.IsEnabled = !own;
        StyleBox.SelectedItem = StyleBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, look.Style));
        StyleNote.Text = own ? $"{(missingBulb ? SaverAnimations.None : ThemeActions.AnimationName(look.Animation, Services.Bulbs))} has its own movement that can't be changed." : "";
        StyleNote.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Scrolls a tile list so its selected tile shows (PRODUCT-SPEC 3.5.5, 3.5.6): when the page is shown and when the choice
    /// changed (Undo, a theme). Only the list's own scroll viewer moves, after layout; the page keeps its scroll position.
    /// </summary>
    /// <param name="list">The Animation or Background Picture list.</param>
    private static void ShowSelectedTile(ListBox list)
    {
        if (list.SelectedItem is not { } selected)
        {
            return;
        }

        list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!ReferenceEquals(list.SelectedItem, selected) || !list.IsVisible
                || list.ItemContainerGenerator.ContainerFromItem(selected) is not FrameworkElement tile
                || FirstDescendant<ScrollViewer>(list) is not { } viewer
                || FirstDescendant<ScrollContentPresenter>(viewer) is not { } viewport
                || !tile.IsDescendantOf(viewport))
            {
                return;
            }

            Rect bounds = tile.TransformToAncestor(viewport).TransformBounds(new Rect(tile.RenderSize));
            if (bounds.Top < 0)
            {
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + bounds.Top);
            }
            else if (bounds.Bottom > viewer.ViewportHeight)
            {
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + Math.Min(bounds.Top, bounds.Bottom - viewer.ViewportHeight));
            }
        });
    }

    /// <summary>The first element of a type below an element in the visual tree (breadth first).</summary>
    private static T? FirstDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>([root]);
        while (queue.TryDequeue(out DependencyObject? node))
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(node, i);
                if (child is T match)
                {
                    return match;
                }

                queue.Enqueue(child);
            }
        }

        return null;
    }

    /// <summary>Replaces a tile list only when its tiles changed, so the list keeps its scroll position.</summary>
    private static void ShowTiles(ListBox list, List<SaverTile> tiles)
    {
        if (list.ItemsSource is not IReadOnlyList<SaverTile> shown || !shown.Select(t => (t.Key, t.Name)).SequenceEqual(tiles.Select(t => (t.Key, t.Name))))
        {
            list.ItemsSource = tiles;
        }
    }

    private FrameworkElement ArtFor(string animation)
    {
        if (!animationArt.TryGetValue(animation, out FrameworkElement? art))
        {
            art = SaverAnimationArt.Create(animation, Services);
            animationArt[animation] = art;
        }

        return art;
    }

    private void OnAnimationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || AnimationList.SelectedItem is not SaverTile tile || tile.Key == Look.Animation)
        {
            return;
        }

        string animation = tile.Key;
        Update(
            l => l with { Animation = animation, Style = SaverAnimations.SelectsStyle(animation) ? SaverAnimations.DefaultStyleFor(animation) : l.Style },
            $"Use the {tile.Name} animation");
    }

    /// <summary>"Use a Bulb as the Animation...": Choose a Bulb (add-on bulbs); the bulb appears as an extra tile.</summary>
    private void OnChooseBulbClick(object sender, RoutedEventArgs e)
    {
        if (host is null)
        {
            return;
        }

        string? current = SaverAnimations.TryGetBulbId(Look.Animation, out string? id) ? id : null;
        if (ChooseBulbDialog.Show(host, Services, current) is { } chosen)
        {
            string animation = SaverAnimations.ForBulb(chosen);
            Update(l => l with { Animation = animation }, $"Use {ThemeActions.AnimationName(animation, Services.Bulbs)} as the animation");
        }
    }

    private void OnStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StyleBox.SelectedItem is ComboBoxItem { Tag: SaverMovementStyle style, Content: string name } && style != Look.Style)
        {
            Update(l => l with { Style = style }, $"Use the {name} style");
        }
    }

    /// <summary>"(None)", the bundled pictures A-Z, then yours; a missing picture shows "Picture not found".</summary>
    private void BuildPictures(SaverLook look)
    {
        noneTile ??= new SaverTile(SaverPictures.None, SaverPictures.None, null, SaverAnimationArt.NoneText());
        var tiles = new List<SaverTile> { noneTile };
        foreach (PictureInfo picture in Services.Pictures.Pictures)
        {
            tiles.Add(PictureTile(picture));
        }

        if (look.Picture != SaverPictures.None && tiles.All(t => !string.Equals(t.Key, look.Picture, StringComparison.OrdinalIgnoreCase)))
        {
            // A removed bundled picture can still be the chosen one (a theme chose it); the screen saver shows it, so its
            // tile shows too. "Picture not found" only when the file is missing.
            if (Services.Pictures.TryGetPicture(look.Picture, out PictureInfo? unlisted))
            {
                tiles.Add(PictureTile(unlisted));
            }
            else
            {
                tiles.Add(MissingPictureTile(look.Picture));
            }
        }

        ShowTiles(PictureList, tiles);
        PictureList.SelectedItem = PictureList.Items.Cast<SaverTile>().FirstOrDefault(t => string.Equals(t.Key, look.Picture, StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(shownPicture, look.Picture, StringComparison.OrdinalIgnoreCase))
        {
            shownPicture = look.Picture;
            ShowSelectedTile(PictureList);
        }
    }

    /// <summary>A picture's tile (kept per file, so its thumbnail is decoded once).</summary>
    private SaverTile PictureTile(PictureInfo picture)
    {
        string key = picture.Id + "|" + picture.FilePath;
        if (!pictureTiles.TryGetValue(key, out SaverTile? tile))
        {
            tile = new SaverTile(picture.Id, picture.Title, picture.Title, Thumbnail(picture));
            pictureTiles[key] = tile;
        }

        return tile;
    }

    /// <summary>"Picture not found": the chosen picture's file is missing.</summary>
    private static SaverTile MissingPictureTile(string id)
    {
        var warning = new TextBlock
        {
            Text = Glyphs.Warning,
            FontFamily = new FontFamily(Glyphs.FontFamilyName),
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
        return new SaverTile(id, "Picture not found", "The picture's file is missing; the screen saver runs without a picture.", warning);
    }

    /// <summary>A picture thumbnail decoded on the thread pool (bundled pictures through the library, yours scaled while decoding).</summary>
    private Image Thumbnail(PictureInfo picture)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        _ = Task.Run(() => DecodeThumbnail(picture)).ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully && t.Result is { } source)
                {
                    image.Dispatcher.BeginInvoke(() => image.Source = source);
                }
            },
            TaskScheduler.Default);
        return image;
    }

    private BitmapSource? DecodeThumbnail(PictureInfo picture)
    {
        try
        {
            if (picture.Origin == MediaOrigin.User)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(picture.FilePath);
                bitmap.DecodePixelWidth = 176;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }

            if (Services.Pictures.LoadImage(picture.Id) is not { } decoded)
            {
                return null;
            }

            BitmapSource source = BitmapSource.Create(decoded.Width, decoded.Height, 96, 96, PixelFormats.Bgra32, null, decoded.Pixels, decoded.Width * 4);
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Services.Log.Warn("Settings.ScreenSaver", $"Could not show the picture {picture.Title}.", ex);
            return null;
        }
    }

    private void OnPictureChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updating && PictureList.SelectedItem is SaverTile tile && !string.Equals(tile.Key, Look.Picture, StringComparison.OrdinalIgnoreCase))
        {
            string picture = tile.Key;
            Update(l => l with { Picture = picture }, picture == SaverPictures.None ? "Use no background picture" : $"Use the {tile.Name} picture");
        }
    }

    private void OnPlacementChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlacementBox.SelectedItem is ComboBoxItem { Tag: PicturePlacement placement, Content: string name } && placement != Look.Placement)
        {
            Update(l => l with { Placement = placement }, $"Place the picture: {name}");
        }
    }

    private void OnAddPictureClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Add Picture", Filter = PictureFilesFilter, Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            AddPictures(dialog.FileNames);
        }
    }

    /// <summary>Copies pictures into My Pictures; the first one is selected.</summary>
    private void AddPictures(IReadOnlyList<string> paths)
    {
        if (host is null)
        {
            return;
        }

        IReadOnlyList<MediaImportResult> results = MediaImport.AddPictures(Services, host, paths);
        if (MediaImport.AddedIds(results) is { Count: > 0 } added)
        {
            string first = added[0];
            Update(l => l with { Picture = first }, "Use the added picture");
        }

        if (MediaImport.Problem(results, name => $"{name} is already in your pictures.") is { } problem)
        {
            MessageBar.Severity = InfoBarSeverity.Warning;
            MessageBar.Message = problem;
            MessageBar.IsOpen = true;
        }
    }

    /// <summary>"Preview Screen Saver" (bold, 5.4), "Remove Picture" (5.4), "Show in Folder".</summary>
    private void OnPictureMenuOpening(object sender, ContextMenuEventArgs e)
    {
        ContextMenu menu = PictureList.ContextMenu;
        menu.Items.Clear();
        SaverTile? tile = e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(PictureList, source) is ListBoxItem { DataContext: SaverTile found }
            ? found
            : PictureList.SelectedItem as SaverTile;
        if (tile is null)
        {
            e.Handled = true;
            return;
        }

        var preview = new MenuItem { Header = "_Preview Screen Saver", FontWeight = FontWeights.SemiBold };
        preview.Click += (_, _) => _ = PreviewAsync();
        menu.Items.Add(preview);
        if (Services.Pictures.TryGetPicture(tile.Key, out PictureInfo? picture))
        {
            var remove = new MenuItem { Header = "_Remove Picture" };
            remove.Click += (_, _) => RemovePicture(picture);
            menu.Items.Add(remove);
            var show = new MenuItem { Header = "Show in _Folder" };
            show.Click += (_, _) => Services.Shell.ShowInFolder(picture.FilePath);
            menu.Items.Add(show);
        }
    }

    /// <summary>
    /// "Remove Picture": yours goes to the holding folder (snackbar [Undo]; Cancel brings it back); a bundled one is hidden.
    /// Removing the chosen picture also chooses "(None)" in the same undo step (5.4), so the screen saver stops showing it;
    /// Undo brings back both.
    /// </summary>
    /// <param name="picture">The picture.</param>
    internal void RemovePicture(PictureInfo picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (host is null)
        {
            return;
        }

        string id = picture.Id;

        // Your picture goes to the holding folder first: when that fails nothing changes, no undo step is recorded and no
        // "Removed" is shown (PRODUCT-SPEC 5.13; review r1 #40). A bundled picture is only hidden (a settings step).
        HeldItem? held = null;
        if (picture.Origin == MediaOrigin.User)
        {
            try
            {
                held = Services.Pictures.Remove(id);
            }
            catch (Exception e) when (FileProblems.Is(e))
            {
                Services.Log.Warn("Settings.ScreenSaver", "A picture could not be removed.", e);
                host.ShowMessage(FileProblems.Sentence($"Couldn't remove {picture.Title}", e), InfoBarSeverity.Error);
                return;
            }
        }

        using (host.History.BeginGroup($"Remove {picture.Title}"))
        {
            // "(None)" is recorded first, so Undo (which goes backwards) brings the picture back before choosing it again.
            if (MediaIds.Comparer.Equals(id, Look.Picture))
            {
                Services.Settings.Update(
                    s => s with { Current = s.Current with { Saver = s.Current.Saver with { Picture = SaverPictures.None } } },
                    SettingsChange.Edit("Use no background picture"));
            }

            if (picture.Origin != MediaOrigin.User)
            {
                held = Services.Pictures.Remove(id);
            }

            if (held is not null)
            {
                HeldItem current = held;
                host.History.Record(
                    new UndoStep($"Remove {picture.Title}", () => Services.Pictures.Restore(current), () => current = Services.Pictures.Remove(id) ?? current),
                    UndoCancelBehavior.RevertOnCancel);
            }
        }

        host.ShowSnackbar($"Removed {picture.Title}.", "Undo", host.UndoLast);
        host.Announce($"Removed {picture.Title}.");
    }

    /// <summary>"Open My Pictures Folder", "Restore Removed Pictures".</summary>
    private void OnPicturesMoreClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PicturesMoreButton, Placement = PlacementMode.Bottom };
        var open = new MenuItem { Header = "Open My _Pictures Folder" };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(Services.Paths.MyPicturesFolder);
            Services.Shell.OpenFolder(Services.Paths.MyPicturesFolder);
        };
        var restore = new MenuItem { Header = "_Restore Removed Pictures" };
        restore.Click += (_, _) => Services.Pictures.RestoreHiddenPictures();
        menu.Items.Add(open);
        menu.Items.Add(restore);
        menu.IsOpen = true;
    }

    private void OnTileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _ = PreviewAsync();
            e.Handled = true;
        }
    }

    private void OnShowOnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updating && ShowOnBox.SelectedItem is ComboBoxItem { Tag: SaverDisplays displays, Content: string name } && displays != Services.Settings.Current.Saver.ShowOn)
        {
            Services.Settings.Update(s => s with { Saver = s.Saver with { ShowOn = displays } }, SettingsChange.Edit($"Show the screen saver on: {name}"));
        }
    }

    /// <summary>"Smooth Motion" (Click, Checked and Unchecked; see <see cref="OnFontStyleToggled"/>).</summary>
    private void OnSmoothMotionToggled(object sender, RoutedEventArgs e)
    {
        bool on = SmoothMotionCheck.IsChecked == true;
        if (updating || on == Services.Settings.Current.Look.SmoothSaverMotion)
        {
            return;
        }

        Services.Settings.Update(s => s with { Look = s.Look with { SmoothSaverMotion = on } }, SettingsChange.Edit(on ? "Turn Smooth Motion on" : "Turn Smooth Motion off"));
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

    private void OnPicturesChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            Refresh();
        }
    });

    private void OnCatalogChanged(object? sender, BulbCatalogChangedEventArgs e)
    {
        if (e.Change != BulbCatalogChange.IndexProgress)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!disposed)
                {
                    Refresh();
                }
            });
        }
    }
}
