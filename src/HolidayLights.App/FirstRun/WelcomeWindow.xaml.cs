using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using HolidayLights.App.About;
using HolidayLights.App.Controls;
using HolidayLights.App.Preview;
using HolidayLights.App.Shell;
using HolidayLights.Core.Legacy;
using HolidayLights.Core.Settings;

namespace HolidayLights.App.FirstRun;

/// <summary>
/// The Welcome card (PRODUCT-SPEC 3.11): where the red bulb lives, the theme cards ("Automatic", "Christmas 1" and the
/// next two holidays), "Play Holiday Music", "Automatically Start Holiday Lights When I Sign In", and, after a 5.4 import,
/// the lines with their one-click fixes ("Use My 2003 Lights", "Start Fresh Instead", the leftovers). Every choice applies
/// at once; closing the card in any way keeps what it shows. UI thread.
/// </summary>
public partial class WelcomeWindow : Window
{
    private const string LogSource = "FirstRun";

    /// <summary>A theme card's height; four cards share the row (PRODUCT-SPEC 3.11 asks 140 x 112, which does not fit four in 512 DIP).</summary>
    private const double CardHeight = 96;

    /// <summary>
    /// The height of a card's preview. The previews show the main display's top-left corner (the corner and the start of
    /// the top and left strips) at <see cref="LightStage.CornerScale"/> of its real size, so the bulbs are legible (PO
    /// decision 8): a whole 4K display in a card would draw them as 2 px dots.
    /// </summary>
    private const double PreviewHeight = 50;

    /// <summary>Between two links of a 5.4 line.</summary>
    private const string Separator = "  ·  ";

    /// <summary>Segoe Fluent Icons CheckMark.</summary>
    private static readonly string CheckMarkGlyph = char.ConvertFromUtf32(0xE73E);

    /// <summary>Segoe Fluent Icons Info (the InfoBar's informational glyph).</summary>
    private static readonly string InfoGlyph = char.ConvertFromUtf32(0xE946);

    private static readonly System.Windows.Media.FontFamily SymbolFont = new("Segoe Fluent Icons");

    private readonly IAppServices services;
    private readonly WelcomeVariant variant;
    private readonly TimeProvider time;
    private readonly List<(ToggleButton Card, ThemeDefinition? Theme)> cards = [];
    private StackPanel? legacyRows;
    private bool syncing;

    /// <summary>Creates the card.</summary>
    /// <param name="services">The services.</param>
    /// <param name="variant">Newcomer, or "Welcome back!" after a 5.4 import.</param>
    /// <param name="situation">The 5.4 leftovers and the screen saver state.</param>
    /// <param name="time">The clock (today's theme, Recent Settings dates).</param>
    public WelcomeWindow(IAppServices services, WelcomeVariant variant, WelcomeSituation situation, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(situation);
        ArgumentNullException.ThrowIfNull(time);
        this.services = services;
        this.variant = variant;
        this.time = time;
        InitializeComponent();

        AppSettings settings = services.Settings.Current;
        Heading.Text = variant == WelcomeVariant.Newcomer ? "Your desktop is decorated!" : "Welcome back!";
        AutomationProperties.SetName(this, $"Welcome to Holiday Lights. {Heading.Text}");
        BannerHost.Content = new HeritageBanner(services.SystemInfo.AnimationsEnabled, settings.Look.SmoothFading);
        TaskbarCorner.SetResourceReference(Image.SourceProperty, services.SystemInfo.TaskbarUsesLightTheme
            ? AppResourceKeys.IllustrationTaskbarCornerLight
            : AppResourceKeys.IllustrationTaskbarCornerDark);

        BuildLegacyLines(situation);
        BuildThemeCards(settings);
        BuildScreenSaverButton(situation);
        LegacyLines.Visibility = LegacyLines.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ScreenSaverHost.Visibility = ScreenSaverHost.Content is null ? Visibility.Collapsed : Visibility.Visible;
        Sync();
        PlaceOnMainDisplay();
        services.Settings.Changed += OnSettingsChanged;
        Closed += (_, _) => services.Settings.Changed -= OnSettingsChanged;
    }

    private DateTimeOffset Now => time.GetLocalNow();

    private DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>Centres the card on the main display's work area (the screen edges and their lights stay visible).</summary>
    private void PlaceOnMainDisplay()
    {
        DisplayInfo main = services.Displays.Primary;
        double scale = main.Scale;
        Left = main.WorkArea.Left / scale + Math.Max(0, (main.WorkArea.Width / scale - Width) / 2);
        Top = main.WorkArea.Top / scale + Math.Max(0, (main.WorkArea.Height / scale - Height) / 2);
    }

    private void Sync()
    {
        AppSettings settings = services.Settings.Current;
        syncing = true;
        try
        {
            foreach ((ToggleButton card, ThemeDefinition? theme) in cards)
            {
                card.IsChecked = IsChosen(theme);
            }

            MusicSwitch.IsChecked = settings.Music.Enabled;
            StartupSwitch.IsChecked = settings.Startup.Auto;
        }
        finally
        {
            syncing = false;
        }
    }

    /// <summary>True when a card's choice is in effect: Automatic themes, or this theme with Automatic themes off.</summary>
    private bool IsChosen(ThemeDefinition? theme)
    {
        AppSettings settings = services.Settings.Current;
        return theme is null ? settings.Calendar.Enabled : !settings.Calendar.Enabled && services.ThemeService.Matches(theme, settings);
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Sync();

    private void BuildThemeCards(AppSettings settings)
    {
        WelcomeThemeChoices choices = WelcomeThemeChoices.For(settings.Calendar, services.Calendar, Today);
        ThemeDefinition? today = AutomaticThemeRules.FindTheme(services.Themes, choices.TodayTheme);
        AddCard("Automatic", $"{choices.TodayTheme} today", today, theme: null);
        foreach (string name in choices.Themes)
        {
            if (AutomaticThemeRules.FindTheme(services.Themes, name) is { } theme)
            {
                AddCard(theme.Name, null, theme, theme);
            }
        }
    }

    /// <summary>
    /// Adds a theme card: a static preview of its arrangement on the main display alone (the top-left corner at a third
    /// of its size) that animates while hovered or focused.
    /// </summary>
    private void AddCard(string title, string? caption, ThemeDefinition? preview, ThemeDefinition? theme)
    {
        // The main display laid out alone, its top-left corner shown at a legible size (PO decision 8): the stage's own
        // corner view (LightStage.CornerScale of real size) draws only the card's pixels, not the whole display.
        var stage = new LightStage
        {
            Services = services,
            Geometry = StageGeometry.DisplayAlone,
            Zoom = StageZoom.Corner,
            ZoomCorner = Corner.TopLeft,
            Arrangement = preview?.Arrangement ?? SlotAssignment.Classic54Default,
            Flash = new FlashOptions
            {
                Pattern = preview?.Flash?.Pattern ?? FlashPatternId.FlashTogether,
                SmoothFading = services.Settings.Current.Look.SmoothFading,
            },
            IsAnimated = false,
            Height = PreviewHeight,
            ClipToBounds = true,
        };
        var well = new NightWell { CornerRadius = new CornerRadius(4), Child = stage };

        var content = new StackPanel();
        content.Children.Add(well);
        content.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        if (caption is not null)
        {
            var captionText = new TextBlock { Text = caption, TextTrimming = TextTrimming.CharacterEllipsis };
            captionText.SetResourceReference(StyleProperty, AppResourceKeys.CaptionTextStyle);
            content.Children.Add(captionText);
        }

        var card = new ToggleButton
        {
            Content = content,
            Height = CardHeight,
            Margin = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetName(card, caption is null ? title : $"{title}, {caption}");
        void Animate() => stage.IsAnimated = card.IsMouseOver || card.IsKeyboardFocusWithin;
        card.MouseEnter += (_, _) => Animate();
        card.MouseLeave += (_, _) => Animate();
        card.GotKeyboardFocus += (_, _) => Animate();
        card.LostKeyboardFocus += (_, _) => Animate();
        // A click, or UI Automation's Toggle (which raises no Click; review r1 #34): the card chooses its theme once.
        card.Click += (_, _) =>
        {
            if (IsChosen(theme))
            {
                Sync();
            }
            else
            {
                ChooseTheme(theme);
            }
        };
        card.Checked += (_, _) =>
        {
            if (!syncing && !IsChosen(theme))
            {
                ChooseTheme(theme);
            }
        };
        card.Unchecked += (_, _) =>
        {
            if (!syncing)
            {
                Sync();
            }
        };
        cards.Add((card, theme));
        ThemeCards.Children.Add(card);
    }

    /// <summary>A card was chosen: a theme loads at once (Automatic themes turn off); "Automatic" turns them back on.</summary>
    private void ChooseTheme(ThemeDefinition? theme)
    {
        if (theme is null)
        {
            SettingsActions.SetAutomaticThemes(true).ApplyTo(services.Settings);
        }
        else
        {
            SettingsActions.LoadTheme(services.ThemeService, theme, Now).ApplyTo(services.Settings);
        }

        Sync();
    }

    /// <summary>
    /// The Holiday Lights 5.4 lines (PRODUCT-SPEC 3.11): one InfoBar-style panel, after the choices, with a line per
    /// situation and its one-click fix as a link; a fix that succeeds turns into a check glyph and "Done.". Lines of text
    /// with links keep the card within its 560 x 640 DIP (an InfoBar per line would push the choices below the fold).
    /// </summary>
    private void BuildLegacyLines(WelcomeSituation situation)
    {
        AppSettings settings = services.Settings.Current;
        if (variant == WelcomeVariant.Imported54FactoryDefaults)
        {
            AddFixLine(
                "Holiday Lights 5.4 was using its standard settings, so your lights now follow the holidays." + WelcomeSituation.ThemesSentence(settings.Import54),
                "Use My 2003 Lights", UseMy2003Lights);
        }
        else if (variant == WelcomeVariant.Imported54Customized)
        {
            TextBlock line = AddLine("Your lights, themes and music choices from Holiday Lights 5.4 are here.");
            line.Inlines.Add(Link("Show Import Details", () => services.SettingsWindow.Show(SettingsPageId.General, new ImportDetailsRequest())));
            line.Inlines.Add(new Run(Separator));
            AddFix(line, "Start Fresh Instead", StartFresh);
        }
        else
        {
            return;
        }

        if (situation.ScreenSaver == ScreenSaverState.Legacy54)
        {
            AddFixLine("Your old screen saver can't start on this version of Windows.", "Fix It", UseAsScreenSaver);
        }

        if (situation.Leftovers?.IsRunning == true)
        {
            AddFixLine("Holiday Lights 5.4 is still running.", "Close It", () => services.LegacyLeftovers.CloseRunningInstance());
        }

        if (situation.Leftovers?.StartupShortcut is not null)
        {
            AddFixLine("Holiday Lights 5.4 also starts with Windows.", "Turn Off", () => services.LegacyLeftovers.RemoveStartupShortcut());
        }

        // "Prefer the exact 2003 look?" and the What's New link share a line.
        TextBlock classic = AddLine("Prefer the exact 2003 look?");
        AddFix(classic, "Use Classic 2003 Look", () =>
        {
            SettingsActions.UseClassicLook().ApplyTo(services.Settings);
            return true;
        });
        classic.Inlines.Add(new Run(Separator));
        classic.Inlines.Add(Link("What's New in 6.0", () => services.AppShell.ShowHelp(HelpTopics.WhatsNew)));
    }

    /// <summary>The panel's lines (the panel is created with the first line).</summary>
    private StackPanel LegacyRows => legacyRows ??= CreateLegacyPanel();

    /// <summary>The InfoBar-style panel: the informational colours and glyph of an InfoBar, with room for several lines.</summary>
    private StackPanel CreateLegacyPanel()
    {
        var rows = new StackPanel();
        var icon = new TextBlock
        {
            Text = InfoGlyph,
            FontFamily = SymbolFont,
            FontSize = 16,
            Margin = new Thickness(0, 2, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        AutomationProperties.SetName(icon, "Information");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(icon);
        Grid.SetColumn(rows, 1);
        grid.Children.Add(rows);
        var panel = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = grid,
        };
        panel.SetResourceReference(Border.BackgroundProperty, "InfoBarInformationalSeverityBackgroundBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "InfoBarBorderBrush");
        LegacyLines.Children.Add(panel);
        return rows;
    }

    /// <summary>Adds a line of the panel with its text.</summary>
    private TextBlock AddLine(string message)
    {
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, LegacyRows.Children.Count > 0 ? 6 : 0, 0, 0) };
        line.Inlines.Add(new Run(message + " "));
        LegacyRows.Children.Add(line);
        return line;
    }

    /// <summary>A line with a one-click fix that turns into "Done." when the fix succeeds.</summary>
    private void AddFixLine(string message, string actionText, Func<bool> fix) => AddFix(AddLine(message), actionText, fix);

    /// <summary>Adds a fix link to a line; when the fix succeeds, the line's text up to the link becomes a check glyph and "Done.".</summary>
    private void AddFix(TextBlock line, string actionText, Func<bool> fix)
    {
        Inline? first = line.Inlines.LastInline is Run { Text: not Separator } message ? message : null;
        Hyperlink link = null!;
        link = Link(actionText, () =>
        {
            if (!TryFix(fix))
            {
                return;
            }

            Inline from = first is not null && line.Inlines.Contains(first) ? first : link;
            line.Inlines.InsertBefore(from, new Run(CheckMarkGlyph) { FontFamily = SymbolFont });
            line.Inlines.InsertBefore(from, new Run(" Done."));
            line.Inlines.Remove(link);
            if (from != link)
            {
                line.Inlines.Remove(from);
            }
        });
        line.Inlines.Add(link);
    }

    /// <summary>A link that runs an action (keyboard: Tab, then Enter).</summary>
    private static Hyperlink Link(string text, Action action)
    {
        var link = new Hyperlink(new Run(text));
        link.Click += (_, _) => action();
        return link;
    }
    private bool TryFix(Func<bool> fix)
    {
        try
        {
            return fix();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            services.Log.Warn(LogSource, "A Welcome card fix failed.", e);
            return false;
        }
    }

    /// <summary>
    /// "Use My 2003 Lights" (PRODUCT-SPEC 2.5.2): the 5.4 settings exactly (the "Holiday Lights 5.4 Settings" entry of
    /// Recent Settings), Automatic themes off, "Play Holiday Music" on unless 5.4 played "Never"; the first song starts
    /// when the card closes.
    /// </summary>
    private bool UseMy2003Lights()
    {
        AppSettings current = services.Settings.Current;
        ThemeableSettings? values = current.RecentSettings
            .FirstOrDefault(e => string.Equals(e.Label, LegacyImporter.RecentSettingsLabel, StringComparison.Ordinal))?.Values
            ?? services.LegacyImporter.Analyze()?.LegacyValues;
        if (values is null)
        {
            return false;
        }

        var entry = new RecentSettingsEntry { Label = LegacyImporter.RecentSettingsLabel, Date = Now, Values = values };
        services.Settings.Update(
            s =>
            {
                AppSettings restored = services.ThemeService.RestoreRecent(s, entry, Now);
                return restored with
                {
                    Calendar = restored.Calendar with { Enabled = false },
                    Music = restored.Music with { Enabled = restored.Current.Music.Mode != PlayMode.Never },
                };
            },
            new SettingsChange(SettingsChangeKind.Import, "Use my 2003 lights"));
        return true;
    }

    /// <summary>"Start Fresh Instead" (PRODUCT-SPEC 2.5.3): the newcomer settings; the imported themes stay and Recent Settings keeps the 5.4 values.</summary>
    private bool StartFresh()
    {
        DateTimeOffset now = Now;
        var context = new FirstRunContext(Today, services.SystemInfo.RegionCode, services.SystemInfo.AnimationsEnabled, now);
        AppSettings fresh = NewcomerSettings.Create(services.Settings.Current, context, services.Calendar, services.Themes, services.ThemeService,
            LegacyImporter.RecentSettingsLabel);
        services.Settings.Update(_ => fresh, new SettingsChange(SettingsChangeKind.Import, "Start fresh"));
        return true;
    }

    /// <summary>"Use Holiday Lights as My Screen Saver" / "Fix It": Windows keeps its timeout; the previous saver is remembered for "Stop Using It".</summary>
    private bool UseAsScreenSaver()
    {
        ScreenSaverPrevious previous = services.ScreenSaverRegistration.Use();
        services.Settings.Update(s => s with { Saver = s.Saver with { Previous = previous } }, SettingsChange.Internal);
        return true;
    }

    private void BuildScreenSaverButton(WelcomeSituation situation)
    {
        if (variant != WelcomeVariant.Newcomer || situation.ScreenSaver is ScreenSaverState.Ours or ScreenSaverState.OursButTurnedOff)
        {
            return;
        }

        var button = new Button { Content = "Use Holiday Lights as My Screen Saver", HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) =>
        {
            if (TryFix(UseAsScreenSaver))
            {
                ScreenSaverHost.Content = Confirmation("Holiday Lights is your screen saver.");
            }
        };
        ScreenSaverHost.Content = button;
    }

    /// <summary>A line with a check glyph (Segoe Fluent Icons CheckMark).</summary>
    private static StackPanel Confirmation(string text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock
        {
            Text = CheckMarkGlyph,
            FontFamily = SymbolFont,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(line, text);
        return line;
    }

    /// <summary>Click, and Checked/Unchecked (UI Automation's Toggle raises no Click; review r1 #34): only a real difference applies.</summary>
    private void OnMusicSwitch(object sender, RoutedEventArgs e)
    {
        bool on = MusicSwitch.IsChecked == true;
        if (on != services.Settings.Current.Music.Enabled)
        {
            SettingsActions.SetMusicEnabled(on).ApplyTo(services.Settings);
        }
    }

    private void OnStartupSwitch(object sender, RoutedEventArgs e)
    {
        bool on = StartupSwitch.IsChecked == true;
        if (on != services.Settings.Current.Startup.Auto)
        {
            SettingsActions.SetStartup(on).ApplyTo(services.Settings);
        }
    }

    private void OnMoreThemes(object sender, RoutedEventArgs e) => services.SettingsWindow.Show(SettingsPageId.Themes);

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        services.SettingsWindow.Show(SettingsPageId.Home);
        Close();
    }

    private void OnDone(object sender, RoutedEventArgs e) => Close();
}
