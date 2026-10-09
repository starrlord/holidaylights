using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HolidayLights.App.Controls;

namespace HolidayLights.App.Gallery;

/// <summary>
/// The selected-bulb bar (PRODUCT-SPEC 3.2.6): the bulb's art (top-edge run and corner art), name, favorite star, source
/// tag, facts, credits and where it is on the screen, with "Use for &lt;Target&gt;", "Add To...", "Edit Bulb..." or
/// "Edit Categories...", "Bulb Credits" and "...". With several bulbs: "Add To..." and "Add to Favorites".
/// </summary>
public partial class SelectedBulbBar : UserControl
{
    /// <summary>The 5.4 sentence under the credits.</summary>
    public const string PermissionText = "This art may not be used for other purposes without the author's permission.";

    /// <summary>Below this width the corner art is not shown beside the run (the facts keep their room).</summary>
    private const double CornerWellMinimumWidth = 420;

    private IAppServices? services;
    private IReadOnlyList<BulbItem> selection = [];
    private bool showingStar;
    private string description = "";
    private string credits = "";
    private bool compact;
    private bool hasCorner;
    private ArrangementTarget target = ArrangementTarget.WholeFrame;
    private bool useKeyRegistered;

    /// <summary>Creates the bar.</summary>
    public SelectedBulbBar()
    {
        InitializeComponent();
        UpdateUseButton();
        SizeChanged += (_, _) => UpdateCornerWell();
    }

    /// <summary>A command for the page (the same commands as the Bulb List menu).</summary>
    public event EventHandler<BulbActionRequest>? ActionRequested;

    /// <summary>False in the Choose a Bulb dialog: the bar shows the bulb without its commands.</summary>
    public bool ShowCommands
    {
        get => Commands.Visibility == Visibility.Visible;
        set
        {
            Commands.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            ManyPanel.Visibility = value && selection.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            NothingText.Text = value
                ? "Select a bulb to see it here. Double-click a bulb to use it, or drag it into a box."
                : "Select a bulb to see it here. Double-click a bulb to choose it.";
        }
    }

    /// <summary>
    /// True while the Bulb List is short (the default window height): the bar tightens its spacing and the permission
    /// sentence uses a smaller size, so the tiles keep their room; the credits always stay visible (PRODUCT-SPEC 3.2.6).
    /// </summary>
    public bool IsCompact
    {
        get => compact;
        set
        {
            if (compact != value)
            {
                compact = value;
                UpdateCredits();
            }
        }
    }

    /// <summary>The list whose selection the bar shows ("..." opens its menu).</summary>
    public BulbList? List { get; set; }

    /// <summary>The services.</summary>
    public IAppServices? Services
    {
        get => services;
        set
        {
            services = value;
            Run.Services = value;
            CornerArt.Services = value;
        }
    }

    /// <summary>The target named by the primary button ("Use for the Whole Frame").</summary>
    public ArrangementTarget Target
    {
        get => target;
        set
        {
            target = value ?? ArrangementTarget.WholeFrame;
            UpdateUseButton();
        }
    }

    /// <summary>Shows a selection.</summary>
    /// <param name="items">The selected bulbs in selection order.</param>
    /// <param name="arrangement">The arrangement ("On your screen: ...").</param>
    public void Show(IReadOnlyList<BulbItem> items, SlotAssignment arrangement)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(arrangement);
        selection = items;
        NothingText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ManyPanel.Visibility = items.Count > 1 && ShowCommands ? Visibility.Visible : Visibility.Collapsed;
        OnePanel.Visibility = items.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (items.Count > 1)
        {
            ManyText.Text = string.Create(CultureInfo.CurrentCulture, $"{items.Count} bulbs selected");
            ManyFavoritesButton.Content = items.All(i => i.IsFavorite) ? "Remove from Favorite_s" : "Add to Favorite_s";
            return;
        }

        if (items.Count == 0)
        {
            return;
        }

        BulbItem item = items[0];
        Run.SingleBulbId = item.Id;
        Run.MaxArtScale = 2 * ArtScale.Factor(services?.Settings.Current.Lights.Size ?? BulbSize.Standard);
        hasCorner = HasCornerArt(item.Id);
        CornerArt.BulbId = hasCorner ? item.Id : null;
        UpdateCornerWell();
        NameText.Text = item.Name;
        NameText.ToolTip = item.Name;
        showingStar = true;
        try
        {
            StarButton.IsChecked = item.IsFavorite;
        }
        finally
        {
            showingStar = false;
        }

        StarGlyph.Text = item.IsFavorite ? Glyphs.FavoriteStarFill : Glyphs.FavoriteStar;
        StarGlyph.SetResourceReference(TextBlock.ForegroundProperty, item.IsFavorite ? "HL.Brush.WarmGold" : "TextFillColorPrimaryBrush");
        SourceText.Text = item.SourceText;
        BigBadge.Visibility = item.IsBig ? Visibility.Visible : Visibility.Collapsed;
        FactsText.Text = item.FactsText;
        FactsText.ToolTip = item.FactsText;
        IReadOnlyList<string> places = ArrangementEdits.PlacesOf(arrangement, item.Id);
        UsageText.Text = places.Count == 0 ? "Not on your screen" : "On your screen: " + string.Join(", ", places);
        UsageText.ToolTip = UsageText.Text;
        description = item.Description;
        credits = item.CreditsText.Length == 0 ? PermissionText : $"{item.CreditsText}. {PermissionText}";
        DescriptionText.Text = description;
        CreditsText.Text = item.CreditsText;
        CreditsText.ToolTip = item.CreditsText.Length == 0 ? null : Tip(item.CreditsText);
        CreditsText.Visibility = item.CreditsText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PermissionLine.Text = PermissionText;
        UpdateCredits();
        EditButton.Content = item.Info.IsEditable ? "_Edit Bulb…" : "_Edit Categories…";
    }

    private static TextBlock Tip(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };

    /// <summary>
    /// The credits stay on screen in both sizes: "Art: ..." on one line (full text in its tooltip) and the permission
    /// sentence wrapped on its own line; compact tightens the spacing. Screen readers get both as help text.
    /// </summary>
    private void UpdateCredits()
    {
        DescriptionText.Margin = new Thickness(0, compact ? 4 : 8, 0, 0);
        CreditsPanel.Margin = new Thickness(0, compact ? 1 : 2, 0, 0);
        PermissionLine.FontSize = compact ? 11 : 12;
        Commands.Margin = new Thickness(0, compact ? 6 : 8, 0, 0);
        DescriptionText.ToolTip = Tip(description);
        System.Windows.Automation.AutomationProperties.SetHelpText(this, $"{description} {credits}");
    }

    private void UpdateCornerWell() =>
        CornerWell.Visibility = hasCorner && ActualWidth >= CornerWellMinimumWidth ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>"Use for ..." keeps Alt+U whatever the target: an underlined U while the text starts with "Use", else a registered key.</summary>
    private void UpdateUseButton()
    {
        string text = target.ButtonText();
        bool startsWithUse = text.StartsWith("Use", StringComparison.Ordinal);
        UseButton.Content = startsWithUse ? "_" + text : text.Replace("_", "__", StringComparison.Ordinal);
        System.Windows.Automation.AutomationProperties.SetName(UseButton, text);
        if (startsWithUse && useKeyRegistered)
        {
            AccessKeyManager.Unregister("U", UseButton);
            useKeyRegistered = false;
        }
        else if (!startsWithUse && !useKeyRegistered)
        {
            AccessKeyManager.Register("U", UseButton);
            useKeyRegistered = true;
        }
    }

    private bool HasCornerArt(string bulbId)
    {
        if (services is null || !services.Bulbs.TryGetBulb(bulbId, out IBulb? bulb))
        {
            return false;
        }

        SizeI size = bulb.GetCellSize(CellSlot.TopLeft, 0, 0);
        return size.Width > 0 && size.Height > 0 && !bulb.GetCell(CellSlot.TopLeft, 0, 0).IsPlaceholder;
    }

    private void Raise(BulbAction action) =>
        ActionRequested?.Invoke(this, new BulbActionRequest(action, [.. selection.Select(i => i.Id)]));

    private void OnUseClick(object sender, RoutedEventArgs e) => Raise(BulbAction.Use);

    private void OnEditClick(object sender, RoutedEventArgs e) => Raise(BulbAction.Edit);

    private void OnCreditsClick(object sender, RoutedEventArgs e) => Raise(BulbAction.Credits);

    private void OnMoreClick(object sender, RoutedEventArgs e) => List?.OpenMenu(MoreButton);

    /// <summary>
    /// Click, and Checked/Unchecked (UI Automation's Toggle raises no Click; review r1 #34): the bulb becomes what the star
    /// shows (setting the same state again changes nothing); never while a selection is being shown.
    /// </summary>
    private void OnStarClick(object sender, RoutedEventArgs e)
    {
        if (!showingStar && selection.Count == 1)
        {
            services?.Bulbs.SetFavorite(selection[0].Id, StarButton.IsChecked == true);
        }
    }

    private void OnManyFavoritesClick(object sender, RoutedEventArgs e)
    {
        if (services is null || selection.Count == 0)
        {
            return;
        }

        bool favorite = selection.Any(i => !i.IsFavorite);
        using (UndoScope.Of(this, services).BeginGroup(favorite ? $"Add {selection.Count} bulbs to Favorites" : $"Remove {selection.Count} bulbs from Favorites"))
        {
            foreach (BulbItem item in selection)
            {
                services.Bulbs.SetFavorite(item.Id, favorite);
            }
        }
    }

    private void OnAddToClick(object sender, RoutedEventArgs e)
    {
        if (selection.Count == 0 || sender is not FrameworkElement anchor)
        {
            return;
        }

        string title = selection.Count == 1 ? $"Add \"{selection[0].Name}\" to" : string.Create(CultureInfo.CurrentCulture, $"Add {selection.Count} bulbs to");
        SlotAssignment arrangement = services?.Settings.Current.Current.Arrangement ?? SlotAssignment.Empty;
        AddToFlyout.Show(anchor, [.. selection.Select(i => i.Id)], title, arrangement, request => ActionRequested?.Invoke(this, request));
    }
}
