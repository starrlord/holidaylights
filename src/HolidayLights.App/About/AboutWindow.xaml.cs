using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using HolidayLights.App.Shell;
using HolidayLights.Audio.Midi;

namespace HolidayLights.App.About;

/// <summary>
/// About Holiday Lights (PRODUCT-SPEC 3.9, 6.5): the banner, the edition, its creator and the link to the project's home
/// page, the licence, the credits of Tiger Technologies, every bulb artist and music arranger, the screen saver art and the
/// software, and "Copy Version Info" for bug reports. UI thread.
/// </summary>
public partial class AboutWindow : Window
{
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(3);

    private readonly IAppServices services;
    private readonly DispatcherTimer statusTimer;

    /// <summary>Creates the window.</summary>
    /// <param name="services">The services (look, displays and lights status for the version info).</param>
    /// <param name="credits">The credits (<c>HelpContent/credits.json</c>).</param>
    public AboutWindow(IAppServices services, CreditsContent credits)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(credits);
        this.services = services;
        InitializeComponent();
        BannerHost.Content = new HeritageBanner(services.SystemInfo.AnimationsEnabled, services.Settings.Current.Look.SmoothFading);
        EditionLine.Text = $"Holiday Lights - Modern Edition, version {VersionInfo.ProgramVersion}";
        CreatorLine.Text = $"Holiday Lights 6 \u2014 the modern edition \u2014 was created by {ProjectInfo.Author}.";
        ProjectLinkText.Text = ProjectInfo.HomePageText;
        ProjectLink.ToolTip = ProjectInfo.HomePage;
        AutomationProperties.SetName(ProjectLink, $"{ProjectInfo.HomePageText} (opens in your web browser)");
        LicenseLine.Text = $"Open source under the {ProjectInfo.License}.";
        ArtworkNotice.Text = credits.ArtworkNotice;
        PrivacyNotice.Text = credits.PrivacyNotice;
        BuildCredits(credits);
        statusTimer = new DispatcherTimer { Interval = StatusDuration };
        statusTimer.Tick += (_, _) =>
        {
            statusTimer.Stop();
            CopyStatus.Text = "";
        };
    }

    private void BuildCredits(CreditsContent credits)
    {
        Credits.Children.Add(Section("Built-In Bulb Artists", BuiltInArtists(credits.BuiltInArtists)));
        Credits.Children.Add(Section($"Add-On Bulb Artists ({credits.AddOnArtists.Count.ToString("N0", CultureInfo.CurrentCulture)})", AddOnArtists(credits.AddOnArtists)));
        Credits.Children.Add(Section("Music", Music(credits.Music)));
        Credits.Children.Add(Section("Screen Saver Art", Paragraph(credits.ScreenSaverArt)));
        Credits.Children.Add(Section("Software", Software(credits.Software)));
    }

    private static Expander Section(string header, UIElement content) => new()
    {
        Header = header,
        Content = content,
        Margin = new Thickness(0, 0, 0, 4),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private static TextBlock Paragraph(string text, bool secondary = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        if (secondary)
        {
            block.SetResourceReference(StyleProperty, AppResourceKeys.SecondaryTextStyle);
        }

        return block;
    }

    private static TextBlock Strong(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        block.SetResourceReference(StyleProperty, AppResourceKeys.BodyStrongTextStyle);
        return block;
    }

    private static StackPanel BuiltInArtists(IReadOnlyList<ArtistCredit> artists)
    {
        var panel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
        foreach (ArtistCredit artist in artists)
        {
            panel.Children.Add(Strong(artist.Name));
            panel.Children.Add(Paragraph(string.Join(", ", artist.Bulbs)));
            if (!string.IsNullOrEmpty(artist.Note))
            {
                panel.Children.Add(Paragraph(artist.Note, secondary: true));
            }
        }

        return panel;
    }

    /// <summary>Every add-on artist A-Z with their bulb counts, and a search box of its own.</summary>
    private static StackPanel AddOnArtists(IReadOnlyList<ArtistCredit> artists)
    {
        string[] lines = [.. artists.Select(a => a.BulbCount == 1 ? $"{a.Name} (1 bulb)" : $"{a.Name} ({a.BulbCount} bulbs)")];
        var list = new ListBox { ItemsSource = lines, Height = 220, Margin = new Thickness(0, 8, 0, 0) };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        AutomationProperties.SetName(list, "Add-On Bulb Artists");
        var search = new TextBox();
        AutomationProperties.SetName(search, "Search artists");
        search.TextChanged += (_, _) =>
        {
            string query = search.Text.Trim();
            list.ItemsSource = query.Length == 0
                ? lines
                : lines.Where(l => l.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        };

        var panel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
        panel.Children.Add(Paragraph("Search artists", secondary: true));
        panel.Children.Add(search);
        panel.Children.Add(list);
        return panel;
    }

    private static StackPanel Music(IReadOnlyList<MusicCredit> arrangers)
    {
        var panel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
        foreach (MusicCredit credit in arrangers)
        {
            panel.Children.Add(Strong(credit.Arranger));
            panel.Children.Add(Paragraph(string.Join(", ", credit.Songs)));
        }

        return panel;
    }

    /// <summary>Each component with its copyright and licence, the licence text behind "View License".</summary>
    private static StackPanel Software(IReadOnlyList<SoftwareCredit> components)
    {
        var panel = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
        foreach (SoftwareCredit component in components)
        {
            panel.Children.Add(Strong(component.Name));
            panel.Children.Add(Paragraph($"{component.Copyright} ({component.License})"));
            var license = new TextBox
            {
                Text = component.LicenseText,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0),
            };
            AutomationProperties.SetName(license, $"{component.Name} license");
            var view = new Button { Content = "View License", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            AutomationProperties.SetName(view, $"View the {component.Name} license");
            view.Click += (_, _) => license.Visibility = license.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            panel.Children.Add(view);
            panel.Children.Add(license);
        }

        return panel;
    }

    /// <summary>"Copy Version Info": version, Windows build, displays, the requested and effective layer modes, the MIDI device.</summary>
    private void OnCopyVersionInfo(object sender, RoutedEventArgs e)
    {
        var inputs = new VersionInfoInputs(
            VersionInfo.ProgramVersion,
            services.SystemInfo.OsBuild,
            services.Displays.Displays,
            services.Lights.Scene.RequestedLayer,
            services.Lights.Status,
            services.Settings.Current.Music.MidiDevice,
            MidiSequencer.GetDeviceNames());
        try
        {
            Clipboard.SetText(VersionInfo.Format(inputs));
            CopyStatus.Text = "Version info copied.";
        }
        catch (COMException exception)
        {
            services.Log.Warn("About", "The clipboard was busy.", exception);
            CopyStatus.Text = "The clipboard is busy. Try again.";
        }

        statusTimer.Stop();
        statusTimer.Start();
    }

    /// <summary>The project link (mouse, or Tab then Enter): the home page in the default browser.</summary>
    private void OnProjectLink(object sender, RoutedEventArgs e) => services.Shell.OpenProjectHomePage();

    private void OnOk(object sender, RoutedEventArgs e) => Close();
}
