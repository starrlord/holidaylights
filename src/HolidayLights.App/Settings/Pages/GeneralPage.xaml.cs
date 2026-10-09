using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using HolidayLights.App.Controls;

namespace HolidayLights.App.Settings.Pages;

/// <summary>
/// The "General" page of the Settings window (owner: settings-ui; PRODUCT-SPEC 3.7): everything rarely changed.
/// </summary>
public partial class GeneralPage : UserControl, ISettingsPage, IDisposable
{
    private ISettingsHost? host;
    private bool updating;
    private bool disposed;
    private bool legacyPresent;

    /// <summary>Creates the page.</summary>
    /// <param name="services">The application services.</param>
    public GeneralPage(IAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
        InitializeComponent();
        StartupBar.ActionCommand = new DelegateCommand(() => Services.Shell.OpenSettingsUri("ms-settings:startupapps"));
        HotKeyImportBar.ActionCommand = new DelegateCommand(UseImportedHotKey);
        HotKeyImportBar.Closed += (_, _) => DismissHotKeyNotice();
        BulbsPath.Text = services.Paths.MyBulbsFolder;
        MusicPath.Text = services.Paths.MyMusicFolder;
        PicturesPath.Text = services.Paths.MyPicturesFolder;
        services.Settings.Changed += OnSettingsChanged;
        services.Lights.StatusChanged += OnLightsChanged;
        services.Lights.SceneChanged += OnLightsChanged;
        services.HotKeys.StatusChanged += OnLightsChanged;
        services.Displays.DisplaysChanged += OnDisplaysChanged;
        Refresh(services.Settings.Current, before: null, readSystem: false);
    }

    /// <summary>The application services.</summary>
    public IAppServices Services { get; }

    /// <inheritdoc />
    public event EventHandler? HeaderChanged;

    /// <inheritdoc />
    public SettingsPageId PageId => SettingsPageId.General;

    /// <inheritdoc />
    public string Title => "General";

    /// <inheritdoc />
    public string Description => "Where the bulbs are drawn, your displays, startup, hot keys and the rest.";

    /// <inheritdoc />
    public FrameworkElement? HeaderActions => null;

    /// <inheritdoc />
    public bool ScrollsAsWhole => true;

    /// <inheritdoc />
    public void Attach(ISettingsHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    /// <remarks>The page reads the system state (Startup approval, the 5.4 install and its leftovers) only here.</remarks>
    public void OnShown() => Refresh(Services.Settings.Current, before: null, readSystem: true);

    /// <inheritdoc />
    public void OnHidden()
    {
    }

    /// <inheritdoc />
    public void HandleRequest(SettingsRequest request)
    {
        switch (request)
        {
            case ResetRequest:
                _ = ResetAsync();
                break;
            case ImportDetailsRequest:
                ShowImportDetails();
                break;
        }
    }

    /// <inheritdoc />
    public string HelpTopicFor(DependencyObject? focused)
    {
        foreach (DependencyObject node in Controls.ElementTree.SelfAndAncestors(focused))
        {
            if (ReferenceEquals(node, LocationCards))
            {
                return HelpTopics.DesktopOrOnTop;
            }

            if (ReferenceEquals(node, DisplayDiagram) || ReferenceEquals(node, EachDisplayRadio) || ReferenceEquals(node, AllTogetherRadio))
            {
                return HelpTopics.SeveralDisplays;
            }

            if (ReferenceEquals(node, HotKeyRows))
            {
                return HelpTopics.LocationHotKey;
            }

            if (ReferenceEquals(node, GlowBox) || ReferenceEquals(node, SmoothRadio) || ReferenceEquals(node, CrispRadio) || ReferenceEquals(node, ModernButton))
            {
                return HelpTopics.SizeGlowLook;
            }

            if (ReferenceEquals(node, LegacyCard))
            {
                return HelpTopics.ComingFrom54;
            }
        }

        return HelpTopics.ForPage(SettingsPageId.General);
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
        Services.Lights.StatusChanged -= OnLightsChanged;
        Services.Lights.SceneChanged -= OnLightsChanged;
        Services.HotKeys.StatusChanged -= OnLightsChanged;
        Services.Displays.DisplaysChanged -= OnDisplaysChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Shows the settings. Without <paramref name="before"/> everything is shown; after a change only what it touched: the
    /// display diagram is rebuilt only for the display settings, and nothing is read from the registry, the Startup folder
    /// or the screen saver (CONTRACTS 4: a slider drag on another page must not do that work on every tick).
    /// </summary>
    /// <param name="settings">The settings now.</param>
    /// <param name="before">The settings before a change, or null to show everything.</param>
    /// <param name="readSystem">Also read the Startup approval and the 5.4 install and leftovers (when the page is shown).</param>
    private void Refresh(AppSettings settings, AppSettings? before, bool readSystem)
    {
        updating = true;
        try
        {
            UpdateLocation(settings);
            if (before is null || DisplaySettingsChanged(before, settings))
            {
                UpdateDisplays(settings);
            }

            UpdateLook(settings);
            AutoStartCheck.IsChecked = settings.Startup.Auto;
            if (readSystem || (before is not null && before.Startup.Auto != settings.Startup.Auto))
            {
                StartupBar.IsOpen = Services.Startup.IsDisabledByWindows;
            }

            UpdateHotKeys(settings);
            RestFullScreenCheck.IsChecked = settings.Rest.FullScreen;
            RestPresentationCheck.IsChecked = settings.Rest.Presentation;
            EnergySaverBox.SelectedItem = EnergySaverBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, settings.Rest.EnergySaver));
            MusicFullScreenCheck.IsChecked = settings.Rest.MusicFullScreen;
            MusicFocusCheck.IsChecked = settings.Rest.MusicFocus;
            MusicLockCheck.IsChecked = settings.Rest.MusicLock;
            LimitFlashingCheck.IsChecked = settings.Accessibility.LimitFlashing;
            DecorateCheck.IsChecked = settings.Ui.DecorateWindow;
            AssociateCheck.IsChecked = settings.Files.AssociateBul;
            if (readSystem)
            {
                ReadLegacyState(settings);
            }

            UpdateLegacyStatus(settings);
        }
        finally
        {
            updating = false;
        }
    }

    /// <summary>True when a change touched what the Displays group shows (the per-display switches, the frame mode, the bulb size of the caption).</summary>
    private static bool DisplaySettingsChanged(AppSettings before, AppSettings after) =>
        before.Lights.FrameMode != after.Lights.FrameMode
        || before.Lights.Size != after.Lights.Size
        || !before.Lights.Displays.Disabled.SequenceEqual(after.Lights.Displays.Disabled, StringComparer.OrdinalIgnoreCase);

    /// <summary>The three layer-mode cards, the fallback line and the hot key line.</summary>
    private void UpdateLocation(AppSettings settings)
    {
        bool onTop = settings.Lights.Drawing == BulbDrawing.OnTop;
        BehindIconsCard.IsChecked = !onTop && settings.Lights.BehindIcons;
        InFrontCard.IsChecked = !onTop && !settings.Lights.BehindIcons;
        OnTopCard.IsChecked = onTop;
        StatusLine? fallback = LightsStatusText.Fallback(Services.Lights.Status);
        FallbackRow.Visibility = fallback is null ? Visibility.Collapsed : Visibility.Visible;
        FallbackText.Text = fallback?.Text ?? "";
        HotKeyBinding location = settings.HotKeys.Location;
        HotKeyLine.Visibility = location.Enabled ? Visibility.Visible : Visibility.Collapsed;
        HotKeyLine.Text = $"{HotKeyText.Compact(location)} switches between On Desktop and On Top.";
    }

    /// <summary>
    /// The display diagram at the real arrangement (each with its number, "Main" and "Show Lights"), or "One display: 3840
    /// x 2160 at 150 %."; the size caption and the Frame radios.
    /// </summary>
    private void UpdateDisplays(AppSettings settings)
    {
        IReadOnlyList<DisplayInfo> displays = [.. Services.Displays.Displays.OrderBy(d => d.Number)];
        var disabled = new HashSet<string>(settings.Lights.Displays.Disabled, StringComparer.OrdinalIgnoreCase);
        DisplayDiagram.Children.Clear();
        bool single = displays.Count <= 1;
        DisplayDiagram.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        SingleDisplayText.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        MainOnlyLink.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        if (single && displays.Count == 1)
        {
            DisplayInfo only = displays[0];
            SingleDisplayText.Text = string.Create(CultureInfo.CurrentCulture, $"One display: {only.Bounds.Width} x {only.Bounds.Height} at {Math.Round(only.Scale * 100)} %.");
        }
        else if (!single)
        {
            DrawDiagram(displays, disabled);
        }

        DisplaysCaption.Text = SizeCaption(displays, settings.Lights.Size);
        EachDisplayRadio.IsChecked = settings.Lights.FrameMode == FrameMode.EachDisplay;
        AllTogetherRadio.IsChecked = settings.Lights.FrameMode == FrameMode.AllDisplaysTogether;
    }

    private void DrawDiagram(IReadOnlyList<DisplayInfo> displays, HashSet<string> disabled)
    {
        int left = displays.Min(d => d.Bounds.Left);
        int top = displays.Min(d => d.Bounds.Top);
        int right = displays.Max(d => d.Bounds.Left + d.Bounds.Width);
        int bottom = displays.Max(d => d.Bounds.Top + d.Bounds.Height);
        double scale = Math.Min(DisplayDiagram.Width / Math.Max(1, right - left), DisplayDiagram.Height / Math.Max(1, bottom - top));
        int enabledCount = displays.Count(d => !disabled.Contains(d.DeviceId));
        foreach (DisplayInfo display in displays)
        {
            bool enabled = !disabled.Contains(display.DeviceId);
            var check = new CheckBox
            {
                Content = "Show Lights",
                IsChecked = enabled,
                IsEnabled = !(enabled && enabledCount == 1),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
            };
            AutomationProperties.SetName(check, string.Create(CultureInfo.CurrentCulture, $"Show lights on display {display.Number}"));
            string deviceId = display.DeviceId;
            int number = display.Number;

            // Click, Checked and Unchecked: UI Automation's Toggle changes IsChecked without a Click; only a real change is applied.
            RoutedEventHandler apply = (_, _) => SetDisplayEnabled(deviceId, number, check.IsChecked == true);
            check.Click += apply;
            check.Checked += apply;
            check.Unchecked += apply;
            var label = new TextBlock { FontWeight = FontWeights.SemiBold, Text = display.IsPrimary ? $"{display.Number} Main" : display.Number.ToString(CultureInfo.CurrentCulture) };
            var tile = new Border
            {
                Width = Math.Max(60, display.Bounds.Width * scale - 4),
                Height = Math.Max(48, display.Bounds.Height * scale - 4),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(enabled ? 2 : 1),
                Padding = new Thickness(6, 4, 6, 4),
                Child = new StackPanel { Children = { label, check } },
                ToolTip = Describe(display),
            };
            tile.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorSecondaryBrush");
            tile.SetResourceReference(Border.BorderBrushProperty, enabled ? "AccentFillColorDefaultBrush" : "ControlStrongStrokeColorDefaultBrush");
            AutomationProperties.SetName(tile, Describe(display));
            Canvas.SetLeft(tile, (display.Bounds.Left - left) * scale + 2);
            Canvas.SetTop(tile, (display.Bounds.Top - top) * scale + 2);
            DisplayDiagram.Children.Add(tile);
        }
    }

    /// <summary>"Display 2 - DELL U2723QE - 3840 x 2160 - 150 %".</summary>
    private static string Describe(DisplayInfo display) => string.IsNullOrWhiteSpace(display.FriendlyName)
        ? display.Describe()
        : string.Create(CultureInfo.CurrentCulture,
            $"Display {display.Number} - {display.FriendlyName} - {display.Bounds.Width} x {display.Bounds.Height} - {Math.Round(display.Scale * 100)} %");

    /// <summary>"Standard Bulbs are 48 px tall on Display 1 and Display 2." (or per display when they differ).</summary>
    private string SizeCaption(IReadOnlyList<DisplayInfo> displays, BulbSize size)
    {
        if (displays.Count == 0 || !Services.Bulbs.TryGetBulb(BulbIds.BuiltIn("standard-bulbs"), out IBulb? bulb))
        {
            return "";
        }

        int cell = bulb.GetCellSize(CellSlot.Top, 0, 0).Height;
        (DisplayInfo Display, int Pixels)[] sizes = [.. displays.Select(d => (d, ArtScale.Scale(cell, ArtScale.Effective(d.Scale, size))))];
        if (sizes.Select(s => s.Pixels).Distinct().Count() == 1)
        {
            string names = ArrangementTexts.JoinWithAnd([.. sizes.Select(s => string.Create(CultureInfo.CurrentCulture, $"Display {s.Display.Number}"))]);
            return string.Create(CultureInfo.CurrentCulture, $"Standard Bulbs are {sizes[0].Pixels} px tall on {names}.");
        }

        string parts = ArrangementTexts.JoinWithAnd([.. sizes.Select((s, i) => string.Create(CultureInfo.CurrentCulture,
            $"{s.Pixels} px{(i == 0 ? " tall" : "")} on Display {s.Display.Number}"))]);
        return $"Standard Bulbs are {parts}.";
    }

    /// <summary>The Look segments, Bulb Size, Pixels, Glow and the two smoothness check boxes.</summary>
    private void UpdateLook(AppSettings settings)
    {
        LookPreset preset = LookPresets.Detect(settings.Look);
        ModernButton.IsChecked = preset == LookPreset.ModernGlow;
        BrightButton.IsChecked = preset == LookPreset.BrightGlow;
        ClassicButton.IsChecked = preset == LookPreset.Classic2003;
        CustomButton.IsChecked = preset == LookPreset.Custom;
        BulbSize size = settings.Lights.Size;
        SmallRadio.IsChecked = size == BulbSize.Small;
        StandardRadio.IsChecked = size == BulbSize.Standard;
        LargeRadio.IsChecked = size == BulbSize.Large;
        ExtraLargeRadio.IsChecked = size == BulbSize.ExtraLarge;
        SmoothRadio.IsChecked = settings.Look.Pixels == SpriteStyle.Smooth;
        CrispRadio.IsChecked = settings.Look.Pixels == SpriteStyle.Crisp;
        GlowBox.SelectedItem = GlowBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, settings.Look.Glow));
        SmoothFadingCheck.IsChecked = settings.Look.SmoothFading;
        SmoothMotionCheck.IsChecked = settings.Look.SmoothSaverMotion;
    }

    /// <summary>The two hot key rows (check box, key caps, "Change..."), their registration problems and the 5.4 notice.</summary>
    private void UpdateHotKeys(AppSettings settings)
    {
        HotKeyBinding location = settings.HotKeys.Location;
        HotKeyBinding lights = settings.HotKeys.Lights;
        LocationHotKeyCheck.IsChecked = location.Enabled;
        LightsHotKeyCheck.IsChecked = lights.Enabled;
        LocationCaps.Binding = location;
        LightsCaps.Binding = lights;
        bool InUse(HotKeyAction action) => Services.HotKeys.Status.FirstOrDefault(s => s.Action == action) is { Binding.Enabled: true, Registration: HotKeyRegistration.InUse or HotKeyRegistration.Unsafe };
        LocationProblem.Visibility = InUse(HotKeyAction.SwitchLocation) ? Visibility.Visible : Visibility.Collapsed;
        LightsProblem.Visibility = InUse(HotKeyAction.ToggleLights) ? Visibility.Visible : Visibility.Collapsed;
        // Every 5.4 import (factory defaults too) moved Ctrl+Shift+<key> to Ctrl+Alt+Shift+<key> (review r1 #27).
        bool open = settings.Import54 is not null
            && location.Enabled
            && location.Modifiers == (HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift)
            && !settings.Onboarding.HotKeyImportNoticeDismissed;
        if (open)
        {
            string letter = location.Key;
            HotKeyImportBar.Message = letter == "B"
                ? "Holiday Lights 5.4 used Ctrl+Shift+B, which web browsers use for the bookmarks bar, so your hot key is now Ctrl+Alt+Shift+B."
                : $"Holiday Lights 5.4 used Ctrl+Shift+{letter}, which other apps often use, so your hot key is now Ctrl+Alt+Shift+{letter}.";
            HotKeyImportBar.ActionText = $"Use Ctrl+Shift+{letter} Anyway";
        }

        HotKeyImportBar.IsOpen = open;
    }

    /// <summary>Shows the current settings (a nested change may already have followed this one), updating only what changed since <see cref="SettingsChangedEventArgs.OldSettings"/>.</summary>
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Refresh(Services.Settings.Current, e.OldSettings, readSystem: false);

    private void OnLightsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            updating = true;
            try
            {
                AppSettings settings = Services.Settings.Current;
                UpdateLocation(settings);
                UpdateHotKeys(settings);
            }
            finally
            {
                updating = false;
            }
        }
    });

    private void OnDisplaysChanged(object? sender, DisplaysChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!disposed)
        {
            updating = true;
            try
            {
                UpdateDisplays(Services.Settings.Current);
            }
            finally
            {
                updating = false;
            }
        }
    });
}
