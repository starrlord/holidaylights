using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using HolidayLights.App.About;
using HolidayLights.App.Controls;
using HolidayLights.App.FirstRun;
using HolidayLights.App.Help;
using HolidayLights.App.Shell;
using HolidayLights.Core.Legacy;
using HolidayLights.Core.Seasons;
using HolidayLights.Core.Themes;
using HolidayLights.Tests.App.Fakes;
using HolidayLights.Tests.Shared;

namespace HolidayLights.Tests.App;

/// <summary>
/// The shell's windows built without being shown (no Application, no screen): About with every credit (PRODUCT-SPEC 3.9,
/// 6.5), Help with every topic (3.10, 6.7) and the Welcome card in its variants (3.11).
/// </summary>
public sealed class WindowTests : IDisposable
{
    private readonly TempDataRoot root = new();
    private readonly InMemorySettingsStore settings = new();
    private readonly FakeSettingsWindow settingsWindow = new();
    private readonly FakeAppShell shell = new();
    private readonly FakeServices services;

    public WindowTests()
    {
        var themes = new ThemeLibrary(root.Paths, new TestHoldingFolder(root.Paths), new RecordingLog());
        themes.Load();
        services = new FakeServices
        {
            Paths = root.Paths,
            Settings = settings,
            Calendar = new SeasonCalendar(),
            Themes = themes,
            ThemeService = new ThemeService(themes, new BundledSongs(), new BuiltInResolver()),
            Displays = new FakeDisplayService(),
            SettingsWindow = settingsWindow,
            AppShell = shell,
        };
    }

    public void Dispose() => root.Dispose();

    [Fact]
    public void AboutCreditsEveryArtistArrangerAndComponent() => StaThread.Run(() =>
    {
        CreditsContent credits = HelpContentStore.LoadCredits();
        var window = new AboutWindow(services, credits);

        Assert.Equal("About Holiday Lights", window.Title);
        Assert.Equal(640, window.Width);
        Assert.Equal(720, window.Height);
        Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
        Assert.Equal($"Holiday Lights - Modern Edition, version {VersionInfo.ProgramVersion}", window.EditionLine.Text);
        Assert.IsType<HeritageBanner>(window.BannerHost.Content);
        Assert.Equal(credits.PrivacyNotice, window.PrivacyNotice.Text);

        Expander[] sections = [.. window.Credits.Children.OfType<Expander>()];
        Assert.Equal(
            ["Built-In Bulb Artists", $"Add-On Bulb Artists ({credits.AddOnArtists.Count:N0})", "Music", "Screen Saver Art", "Software"],
            sections.Select(s => (string)s.Header));

        string text = string.Join("\n", Descendants(window.Credits).OfType<TextBlock>().Select(t => t.Text));
        Assert.All(credits.BuiltInArtists, a => Assert.Contains(a.Name, text));
        Assert.All(credits.Music, m => Assert.Contains(m.Arranger, text));
        Assert.All(credits.Software, s => Assert.Contains(s.Name, text));
        Assert.Contains("Tiger Technologies", string.Join(" ", Descendants((DependencyObject)window.Content).OfType<TextBlock>().Select(t => t.Text)));

        ListBox artists = Descendants(window.Credits).OfType<ListBox>().Single();
        Assert.Equal(credits.AddOnArtists.Count, artists.Items.Count);
        TextBox search = Descendants(window.Credits).OfType<TextBox>().First(t => !t.IsReadOnly);
        search.Text = credits.AddOnArtists[0].Name;
        Assert.InRange(artists.Items.Count, 1, credits.AddOnArtists.Count - 1);
        window.Close();
    });

    [Fact]
    public void EveryHelpTopicRenders() => StaThread.Run(() =>
    {
        int pictures = 0;
        var builder = new HelpDocumentBuilder(_ => { }, _ => { }, path =>
        {
            pictures++;
            return HelpDocumentBuilder.LoadEmbeddedImage(path);
        });
        foreach (HelpTopicEntry topic in HelpContentStore.LoadContents().Books.SelectMany(b => b.Topics))
        {
            FlowDocument document = builder.Build(MarkdownParser.Parse(HelpContentStore.ReadTopicMarkdown(topic.Id)), banner: null);
            Assert.True(document.Blocks.Count > 1, topic.Id);
        }

        Assert.True(pictures >= 5);
        Assert.NotNull(HelpDocumentBuilder.LoadEmbeddedImage("images/tray-menu.png"));
        Assert.Null(HelpDocumentBuilder.LoadEmbeddedImage("images/missing.png"));
    });

    [Fact]
    public void HelpNavigatesSearchesAndOpensSettingsPages() => StaThread.Run(() =>
    {
        var window = new HelpWindow(services, HelpContentStore.LoadContents());
        window.Navigate(HelpTopics.TaskbarIcon);
        Assert.Equal(HelpTopics.TaskbarIcon, window.CurrentTopic);
        FlowDocument document = window.TopicViewer.Document;
        Assert.IsType<BlockUIContainer>(document.Blocks.FirstBlock);
        Assert.Contains(document.Blocks.OfType<Table>(), t => t.RowGroups[0].Rows.Count == 11);

        // The page button at the end opens Settings there.
        Button page = (Button)((BlockUIContainer)document.Blocks.LastBlock).Child;
        Assert.Equal("Open the Home page", page.Content);
        page.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(SettingsPageId.Home, Assert.Single(settingsWindow.Shown).Page);

        window.Navigate("no-such-topic");
        Assert.Equal(HelpTopics.Welcome, window.CurrentTopic);

        window.SearchBox.Text = "volume";
        Assert.Equal(Visibility.Visible, window.ResultsList.Visibility);
        Assert.Equal(Visibility.Collapsed, window.ContentsTree.Visibility);
        Assert.NotEmpty(window.ResultsList.Items);
        window.SearchBox.Text = "";
        Assert.Equal(Visibility.Visible, window.ContentsTree.Visibility);
        window.Close();
    });

    [Fact]
    public void TheNewcomerCardOffersAutomaticAndThreeThemes() => StaThread.Run(() =>
    {
        var window = new WelcomeWindow(services, WelcomeVariant.Newcomer, new WelcomeSituation(null, ScreenSaverState.NotOurs), new ManualTime(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(-7))));

        Assert.Equal("Welcome to Holiday Lights", window.Title);
        Assert.Equal("Your desktop is decorated!", window.Heading.Text);
        Assert.Equal((560d, 640d), (window.Width, window.Height));
        ToggleButton[] cards = [.. window.ThemeCards.Children.OfType<ToggleButton>()];
        Assert.Equal(4, cards.Length);
        Assert.Equal(["Automatic, Halloween today", "Christmas 1", "Thanksgiving", "New Year"], cards.Select(System.Windows.Automation.AutomationProperties.GetName));
        Assert.Empty(window.LegacyLines.Children);
        Assert.IsType<Button>(window.ScreenSaverHost.Content);
        Assert.Equal(true, cards[0].IsChecked);
        Assert.Equal(false, window.MusicSwitch.IsChecked);
        Assert.Equal(true, window.StartupSwitch.IsChecked);

        // Two clicks from the card: Christmas 1 now, Automatic themes off.
        cards[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(settings.Current.Calendar.Enabled);
        Assert.True(services.ThemeService.Matches(services.Themes.Find("Christmas 1")!, settings.Current));
        Assert.Equal([false, true, false, false], cards.Select(c => c.IsChecked == true));

        cards[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(settings.Current.Calendar.Enabled);
        Assert.Equal(true, cards[0].IsChecked);

        window.MusicSwitch.IsChecked = true;
        window.MusicSwitch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(settings.Current.Music.Enabled);
        window.Close();
    });

    [Fact]
    public void TheFactoryDefaultCardBringsBackThe2003Lights() => StaThread.Run(() =>
    {
        ThemeableSettings christmas = services.ThemeService.Apply(new AppSettings(), services.Themes.Find("Christmas 1")!, new ThemeApplyOptions("x", DateTimeOffset.Now, false)).Current;
        settings.Update(s => s with
        {
            RecentSettings = [new RecentSettingsEntry { Label = LegacyImporter.RecentSettingsLabel, Date = DateTimeOffset.Now, Values = christmas }],
        }, SettingsChange.Internal);
        var situation = new WelcomeSituation(new LegacyLeftoverState(IsRunning: true, StartupShortcut: @"C:\Startup\Holiday Lights.lnk"), ScreenSaverState.Legacy54);
        var window = new WelcomeWindow(services, WelcomeVariant.Imported54FactoryDefaults, situation, new ManualTime(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero)));

        Assert.Equal("Welcome back!", window.Heading.Text);
        TextBlock[] lines = LegacyLines(window);
        Hyperlink[] links = [.. lines.SelectMany(l => l.Inlines.OfType<Hyperlink>())];
        Assert.Equal(
            ["Use My 2003 Lights", "Fix It", "Close It", "Turn Off", "Use Classic 2003 Look", "What's New in 6.0"],
            links.Select(Text));
        Assert.StartsWith("Holiday Lights 5.4 was using its standard settings, so your lights now follow the holidays.", Text(lines[0]));
        Assert.Equal("Prefer the exact 2003 look? Use Classic 2003 Look  ·  What's New in 6.0", Text(lines[4]));
        Assert.Null(window.ScreenSaverHost.Content);

        Click(links[0]);
        Assert.False(settings.Current.Calendar.Enabled);
        Assert.True(settings.Current.Music.Enabled);
        Assert.Equal(christmas, settings.Current.Current);
        Assert.Equal(SettingsChangeKind.Import, settings.History[^1].Kind);
        Assert.Equal("\uE73E Done.", Text(lines[0]));
        Assert.Empty(lines[0].Inlines.OfType<Hyperlink>());

        Click(links[4]);
        Assert.Equal(LookPreset.Classic2003, LookPresets.Detect(settings.Current.Look));
        Assert.Equal("\uE73E Done.  ·  What's New in 6.0", Text(lines[4]));
        window.Close();
    });

    [Fact]
    public void TheCustomizedCardOffersImportDetailsAndAFreshStart() => StaThread.Run(() =>
    {
        settings.Update(s => s with { Calendar = s.Calendar with { Enabled = false }, Music = s.Music with { Enabled = true } }, SettingsChange.Internal);
        var window = new WelcomeWindow(services, WelcomeVariant.Imported54Customized, new WelcomeSituation(new LegacyLeftoverState(false, null), ScreenSaverState.NotOurs), new ManualTime(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero)));

        TextBlock first = LegacyLines(window)[0];
        Assert.Equal("Your lights, themes and music choices from Holiday Lights 5.4 are here. Show Import Details  ·  Start Fresh Instead", Text(first));
        Hyperlink[] links = [.. first.Inlines.OfType<Hyperlink>()];
        Click(links[0]);
        Assert.IsType<ImportDetailsRequest>(Assert.Single(settingsWindow.Shown).Request);

        Click(links[1]);
        Assert.True(settings.Current.Calendar.Enabled);
        Assert.False(settings.Current.Music.Enabled);
        Assert.Equal(LegacyImporter.RecentSettingsLabel, settings.Current.RecentSettings[0].Label);
        Assert.Equal("Your lights, themes and music choices from Holiday Lights 5.4 are here. Show Import Details  ·  \uE73E Done.", Text(first));

        Hyperlink whatsNew = LegacyLines(window).SelectMany(t => t.Inlines.OfType<Hyperlink>()).Single(l => Text(l) == "What's New in 6.0");
        Click(whatsNew);
        Assert.Equal([HelpTopics.WhatsNew], shell.HelpTopics);
        window.Close();
    });

    /// <summary>
    /// The Welcome card keeps its 560 x 640 DIP (PRODUCT-SPEC 3.11) and still shows every choice without scrolling: the four
    /// theme cards in one row, both switches, the newcomer's screen saver button and the 5.4 lines (r1 review: they were
    /// 120-460 DIP below the fold).
    /// </summary>
    [Theory]
    [InlineData(WelcomeVariant.Newcomer, false)]
    [InlineData(WelcomeVariant.Imported54FactoryDefaults, false)]
    [InlineData(WelcomeVariant.Imported54FactoryDefaults, true)]
    [InlineData(WelcomeVariant.Imported54Customized, false)]
    public void TheWelcomeCardShowsEveryChoiceWithoutScrolling(WelcomeVariant variant, bool legacyScreenSaver) => StaThread.Run(() =>
    {
        var situation = new WelcomeSituation(
            variant == WelcomeVariant.Newcomer ? null : new LegacyLeftoverState(false, null),
            legacyScreenSaver ? ScreenSaverState.Legacy54 : ScreenSaverState.NotOurs);
        var window = new WelcomeWindow(services, variant, situation, new ManualTime(new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(-7))));
        Ui.Fakes.UiHarness.ApplyAppResources(window, dark: false);

        // The client area of the 560 x 640 DIP window inside the Windows 11 frame and caption.
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(544, 604));
        content.Arrange(new Rect(0, 0, 544, 604));
        content.UpdateLayout();

        Assert.True(window.Scroller.ExtentHeight <= window.Scroller.ViewportHeight + 0.5,
            $"{variant}: {window.Scroller.ExtentHeight:0} DIP of content in a {window.Scroller.ViewportHeight:0} DIP viewport");
        ToggleButton[] cards = [.. window.ThemeCards.Children.OfType<ToggleButton>()];
        Assert.Equal(4, cards.Length);
        Assert.Single(cards.Select(c => Math.Round(c.TranslatePoint(default, content).Y)).Distinct());
        Assert.All(cards, c => Assert.True(c.ActualWidth >= 110, $"a card is {c.ActualWidth:0} DIP wide"));
        window.Close();
    });

    /// <summary>The lines of the Welcome card's Holiday Lights 5.4 panel.</summary>
    private static TextBlock[] LegacyLines(WelcomeWindow window) => [.. Descendants(window.LegacyLines).OfType<TextBlock>().Where(t => t.Inlines.Count > 1)];

    private static string Text(TextBlock block) => string.Concat(block.Inlines.Select(Text));

    private static string Text(Inline inline) => inline switch
    {
        Run run => run.Text,
        Span span => string.Concat(span.Inlines.Select(Text)),
        _ => "",
    };

    private static void Click(Hyperlink link) => link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is DependencyObject element)
            {
                yield return element;
                foreach (DependencyObject descendant in Descendants(element))
                {
                    yield return descendant;
                }
            }
        }
    }
}
