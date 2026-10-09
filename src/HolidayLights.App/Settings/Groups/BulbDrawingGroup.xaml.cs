using System.Windows;
using System.Windows.Controls;
using HolidayLights.App.Controls;

namespace HolidayLights.App.Settings.Groups;

/// <summary>
/// "Bulb Drawing" (PRODUCT-SPEC 3.2.8): "On Desktop" with "Behind the Desktop Icons", or "On Top"; while a fallback layer
/// stands in for the chosen one, a warning line with "Try Again". Home shows the compact form (the two radios only;
/// behind or in front of the icons is chosen on Bulb Factory and General).
/// </summary>
public partial class BulbDrawingGroup : UserControl
{
    private IAppServices? services;
    private bool compact;

    /// <summary>Creates the group.</summary>
    public BulbDrawingGroup()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    /// <summary>The services (set by the page).</summary>
    public IAppServices? Services
    {
        get => services;
        set
        {
            Detach();
            services = value;
            if (IsLoaded)
            {
                Attach();
            }
        }
    }

    /// <summary>Home's compact form: the two radios with short descriptions, no icon check box and no link.</summary>
    public bool IsCompact
    {
        get => compact;
        set
        {
            compact = value;
            BehindIconsCheck.Visibility = MoreLink.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            OnTopRadio.Content = new TwoLineLabel
            {
                Label = "On _Top",
                Description = value ? "Above all windows." : "Above all windows. Clicks go through the bulbs.",
            };
        }
    }

    private void Attach()
    {
        if (services is null)
        {
            return;
        }

        Detach();
        services.Settings.Changed += OnSettingsChanged;
        services.Lights.StatusChanged += OnStatusChanged;
        Refresh();
    }

    private void Detach()
    {
        if (services is not null)
        {
            services.Settings.Changed -= OnSettingsChanged;
            services.Lights.StatusChanged -= OnStatusChanged;
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Refresh();

    private void OnStatusChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (services is null)
        {
            return;
        }

        LightsSettings lights = services.Settings.Current.Lights;
        DesktopRadio.IsChecked = lights.Drawing == BulbDrawing.Desktop;
        OnTopRadio.IsChecked = lights.Drawing == BulbDrawing.OnTop;
        BehindIconsCheck.IsChecked = lights.BehindIcons;
        BehindIconsCheck.IsEnabled = lights.Drawing == BulbDrawing.Desktop;

        StatusLine? fallback = LightsStatusText.Fallback(services.Lights.Status);
        StatusRow.Visibility = fallback is null ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = fallback?.Text ?? "";
    }

    private void SetDrawing(BulbDrawing drawing)
    {
        services?.Settings.Update(
            s => s.Lights.Drawing == drawing ? s : s with { Lights = s.Lights with { Drawing = drawing } },
            SettingsChange.Edit(drawing == BulbDrawing.OnTop ? "Draw the bulbs on top of all windows" : "Draw the bulbs on the desktop"));
    }

    private void OnDesktopClick(object sender, RoutedEventArgs e) => SetDrawing(BulbDrawing.Desktop);

    private void OnTopClick(object sender, RoutedEventArgs e) => SetDrawing(BulbDrawing.OnTop);

    private void OnBehindIconsClick(object sender, RoutedEventArgs e)
    {
        bool behind = BehindIconsCheck.IsChecked == true;
        services?.Settings.Update(
            s => s.Lights.BehindIcons == behind ? s : s with { Lights = s.Lights with { BehindIcons = behind } },
            SettingsChange.Edit(behind ? "Draw the bulbs behind the desktop icons" : "Draw the bulbs in front of the desktop icons"));
    }

    private void OnTryAgainClick(object sender, RoutedEventArgs e) => services?.Lights.RetryPreferredLayer();

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is ISettingsHost host)
        {
            host.Navigate(SettingsPageId.General);
        }
    }
}
