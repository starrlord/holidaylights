using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HolidayLights.App.Controls;
using HolidayLights.App.Gallery;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Dialogs;
using HolidayLights.App.Settings.Groups;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Audio;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>
/// The pages of a real Settings window (off-screen, closed at once): Home's theme cards, loading themes, the Music Box
/// switch and Remove Song, the screen saver status card, General's displays and Look presets, Cancel (acceptance
/// scenario 8, and renamed calendar themes), the live status lines that follow the lights, the music, the displays and
/// the hot keys, file operations that fail (theme delete, rename, save and restore, bulb removal, undo), the navigation
/// pane's keys, header actions in a narrow window, the flash patterns and the theme cards' summaries and descriptions.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class SettingsPagesTests(UiThread ui)
{
    [Fact]
    public void HomeOffersEightDifferentThemesWithAutomaticFirst()
    {
        WithWindow(SettingsPageId.Home, (services, window) =>
        {
            var home = (HomePage)window.GetPage(SettingsPageId.Home);
            IReadOnlyList<ThemeCardItem> cards = home.Cards;
            Assert.Equal(8, cards.Count);
            Assert.True(cards[0].IsAutomatic);
            Assert.Equal("Automatic", cards[0].Name);
            Assert.Equal(cards.Count, cards.Select(c => c.Name).Distinct(StringComparer.CurrentCultureIgnoreCase).Count());
            Assert.Contains(cards, c => c.Name == ShippedThemeNames.Christmas1);
            Assert.True(cards[0].IsSelected);
            Assert.Equal("Holiday Lights", HomePage.Greeting(""));
        });
    }

    [Fact]
    public void LoadingAThemeTurnsAutomaticThemesOffAndUndoRestores()
    {
        WithWindow(SettingsPageId.Themes, (services, window) =>
        {
            AppSettings before = services.Settings.Current;
            Assert.True(before.Calendar.Enabled);
            ThemeDefinition theme = services.Themes.Find(ShippedThemeNames.Christmas1)!;
            Assert.True(ThemeActions.Load(services, window, theme));
            AppSettings after = services.Settings.Current;
            Assert.True(services.ThemeService.Matches(theme, after));
            Assert.False(after.Calendar.Enabled);
            Assert.Equal("Before Christmas 1", after.RecentSettings[0].Label);
            Assert.Equal("Christmas 1 theme loaded. Automatic themes are off.", window.Snackbar.Message);
            Assert.Equal("Load the Christmas 1 theme", window.Session.History.UndoDescription);
            window.UndoLast();
            Assert.Equal(before.Current, services.Settings.Current.Current);
            Assert.True(services.Settings.Current.Calendar.Enabled);
        });
    }

    [Fact]
    public void TurningMusicOnWhileTheModeIsNeverPlaysAlways()
    {
        AppSettings initial = new AppSettings() with { Current = new ThemeableSettings() with { Music = new CurrentMusic { Mode = PlayMode.Never } } };
        WithWindow(SettingsPageId.MusicBox, (services, window) =>
        {
            var page = (MusicBoxPage)window.GetPage(SettingsPageId.MusicBox);
            page.MusicToggle.IsChecked = true;
            page.MusicToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(services.Settings.Current.Music.Enabled);
            Assert.Equal(PlayMode.Always, services.Settings.Current.Current.Music.Mode);
            Assert.True(page.AlwaysRadio.IsChecked);
        }, initial);
    }

    [Fact]
    public void CancelRestoresTheLookAndBringsBackARemovedSong()
    {
        WithWindow(SettingsPageId.MusicBox, (services, window) =>
        {
            AppSettings before = services.Settings.Current;
            var page = (MusicBoxPage)window.GetPage(SettingsPageId.MusicBox);
            SongRow song = page.Rows[0];
            int songs = services.Songs.Songs.Count;
            services.Settings.Update(s => s with { Current = s.Current with { Flash = new FlashSettings { Pattern = FlashPatternId.BulbChase, Interval = 3 } } }, SettingsChange.Edit("Change the flash pattern"));
            services.Settings.Update(s => s with { Current = s.Current with { Saver = s.Current.Saver with { Background = RgbColor.White } } }, SettingsChange.Edit("Change the background color"));
            page.RemoveSong(song);
            Assert.Equal(songs - 1, services.Songs.Songs.Count);
            window.Session.Cancel();
            Assert.Equal(before.Current.Flash, services.Settings.Current.Current.Flash);
            Assert.Equal(before.Current.Saver.Background, services.Settings.Current.Current.Saver.Background);
            Assert.Equal(songs, services.Songs.Songs.Count);
            Assert.Contains(services.Songs.Songs, s => s.Id == song.Id);
        });
    }

    [Fact]
    public void UsingHolidayLightsAsTheScreenSaverIsUndoneByCancel()
    {
        WithWindow(SettingsPageId.ScreenSaver, (services, window) =>
        {
            var page = (ScreenSaverPage)window.GetPage(SettingsPageId.ScreenSaver);
            Assert.Equal(Visibility.Visible, page.UseButton.Visibility);
            page.UseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(["Use"], services.SaverRegistrationFake.Calls);
            Assert.NotNull(services.Settings.Current.Saver.Previous);
            Assert.Equal(Visibility.Visible, page.StopButton.Visibility);
            window.Session.Cancel();
            Assert.Equal(["Use", "StopUsing"], services.SaverRegistrationFake.Calls);
        });
    }

    [Fact]
    public void PickingLeavesSelectsFallingLeavesAndOtherAnimationsKeepTheStyle()
    {
        WithWindow(SettingsPageId.ScreenSaver, (services, window) =>
        {
            var page = (ScreenSaverPage)window.GetPage(SettingsPageId.ScreenSaver);
            SaverTile Tile(string key) => page.AnimationList.Items.Cast<SaverTile>().First(t => t.Key == key);
            page.AnimationList.SelectedItem = Tile(SaverAnimations.Leaves);
            Assert.Equal(SaverAnimations.Leaves, services.Settings.Current.Current.Saver.Animation);
            Assert.Equal(SaverMovementStyle.FallingLeaves, services.Settings.Current.Current.Saver.Style);
            page.AnimationList.SelectedItem = Tile("Santa");
            Assert.Equal(SaverMovementStyle.FallingLeaves, services.Settings.Current.Current.Saver.Style);
            Assert.True(page.StyleBox.IsEnabled);
            page.AnimationList.SelectedItem = Tile(SaverAnimations.Snow);
            Assert.False(page.StyleBox.IsEnabled);
            Assert.Equal("Snow has its own movement that can't be changed.", page.StyleNote.Text);
        });
    }

    [Fact]
    public void MainDisplayOnlyKeepsTheLastDisplayChecked()
    {
        WithWindow(SettingsPageId.General, (services, window) =>
        {
            var page = (GeneralPage)window.GetPage(SettingsPageId.General);
            page.MainOnlyLink.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            DisplayInfo second = services.Displays.Displays.First(d => !d.IsPrimary);
            Assert.Equal([second.DeviceId], services.Settings.Current.Lights.Displays.Disabled);
            CheckBox[] checks = [.. page.DisplayDiagram.Children.OfType<Border>().Select(b => ((StackPanel)b.Child).Children.OfType<CheckBox>().Single())];
            Assert.Equal(2, checks.Length);
            Assert.Single(checks, c => c.IsChecked == true && !c.IsEnabled);
            page.ClassicButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(LookPresets.ValuesOf(LookPreset.Classic2003), services.Settings.Current.Look);
            Assert.True(page.ClassicButton.IsChecked);
        });
    }

    [Fact]
    public void HomeFollowsTheLightsAndTheMusic()
    {
        WithWindow(SettingsPageId.Home, (services, window) =>
        {
            var home = (HomePage)window.GetPage(SettingsPageId.Home);
            services.LightsFake.SetStatus(services.Lights.Status with { Health = LightsHealth.NoGraphicsDevice });
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal("Your lights can't be drawn right now (no graphics device). Holiday Lights keeps trying.", home.StatusText.Text);

            SongInfo song = services.Songs.Songs[0];
            services.MusicFake.SetState(new MusicState { Status = MusicStatus.Playing, CurrentSong = song });
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal($"Playing: {song.Title}", home.MusicText.Text);
            Assert.Equal(Visibility.Visible, home.PauseButton.Visibility);
            home.PauseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Pause", services.MusicFake.Commands[^1]);

            // While the user's pause holds, the button offers Resume (review r1 #35).
            services.MusicFake.SetState(new MusicState { Status = MusicStatus.Paused, CurrentSong = song });
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal("Paused", home.MusicText.Text);
            Assert.Equal(Visibility.Visible, home.PauseButton.Visibility);
            Assert.Equal("Resume", home.PauseButton.Content);
            Assert.Equal(Visibility.Collapsed, home.NextSongButton.Visibility);
            home.PauseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Resume", services.MusicFake.Commands[^1]);
        });
    }

    [Fact]
    public void GeneralFollowsTheDisplaysAndTheHotKeys()
    {
        WithWindow(SettingsPageId.General, (services, window) =>
        {
            var page = (GeneralPage)window.GetPage(SettingsPageId.General);
            Assert.Equal(Visibility.Visible, page.DisplayDiagram.Visibility);
            services.DisplaysFake.SetDisplays([services.Displays.Primary]);
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal(Visibility.Collapsed, page.DisplayDiagram.Visibility);
            Assert.Equal("One display: 3840 x 2160 at 150 %.", page.SingleDisplayText.Text);

            Assert.Equal(Visibility.Collapsed, page.LocationProblem.Visibility);
            services.HotKeysFake.Registration = HotKeyRegistration.InUse;
            services.HotKeysFake.RaiseStatusChanged();
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal(Visibility.Visible, page.LocationProblem.Visibility);
        });
    }

    [Fact]
    public void CancelAfterRenamingCalendarThemesKeepsTheCalendarOnTheNewNames()
    {
        WithWindow(SettingsPageId.Themes, (services, window) =>
        {
            var page = (ThemesPage)window.GetPage(SettingsPageId.Themes);
            string HalloweenTheme() => services.Settings.Current.Calendar.Entries.Single(e => e.Id == "halloween").Theme;
            ThemeCardItem Card(string name)
            {
                UiHarness.Pump(TimeSpan.FromMilliseconds(50));
                return page.Cards.Single(c => c.Name == name);
            }

            page.CommitRename(Card(ShippedThemeNames.Halloween), "Spooky Halloween");
            Assert.Equal("Spooky Halloween", HalloweenTheme());
            page.CommitRename(Card(ShippedThemeNames.ClassicLights), "Old Faithful");
            page.CommitRename(Card("Old Faithful"), "Older Still");
            Assert.Equal("Older Still", services.Settings.Current.Calendar.Between);

            // Undone renames are not kept, so the calendar goes back with them.
            page.CommitRename(Card(ShippedThemeNames.Thanksgiving), "Turkey Day");
            window.UndoLast();
            Assert.Equal(ShippedThemeNames.Thanksgiving, services.Settings.Current.Calendar.Entries.Single(e => e.Id == "thanksgiving").Theme);

            window.Session.Cancel();

            // Cancel keeps renames (PRODUCT-SPEC 2.3) and the restored calendar uses the names that exist.
            Assert.NotNull(services.Themes.Find("Spooky Halloween"));
            Assert.Null(services.Themes.Find(ShippedThemeNames.Halloween));
            Assert.Equal("Spooky Halloween", HalloweenTheme());
            Assert.Equal("Older Still", services.Settings.Current.Calendar.Between);
            Assert.NotNull(services.Themes.Find(ShippedThemeNames.Thanksgiving));
            Assert.Equal(ShippedThemeNames.Thanksgiving, services.Settings.Current.Calendar.Entries.Single(e => e.Id == "thanksgiving").Theme);
            Assert.All(services.Settings.Current.Calendar.Entries, e => Assert.NotNull(services.Themes.Find(e.Theme)));
        });
    }

    [Fact]
    public void ThemeFilesThatCantBeWrittenSayWhyAndRecordNothing()
    {
        AppSettings initial = new AppSettings() with { Themes = new ThemePreferences { LastName = "Locked Theme" } };
        WithWindow(SettingsPageId.Themes, (services, window) =>
        {
            var page = (ThemesPage)window.GetPage(SettingsPageId.Themes);
            ThemeCardItem halloween = page.Cards.Single(c => c.Name == ShippedThemeNames.Halloween);
            string file = Path.Combine(services.Paths.ThemesFolder, "Halloween.json");
            Assert.True(File.Exists(file));
            using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                page.Delete(halloween);
                Assert.True(page.MessageBar.IsOpen);
                Assert.Equal(InfoBarSeverity.Error, page.MessageBar.Severity);
                Assert.StartsWith("Couldn't delete Halloween: ", page.MessageBar.Message, StringComparison.Ordinal);

                page.CommitRename(halloween, "Spooky Halloween");
                Assert.StartsWith("Couldn't rename Halloween: ", page.MessageBar.Message, StringComparison.Ordinal);
            }

            Assert.NotNull(services.Themes.Find(ShippedThemeNames.Halloween));
            Assert.Null(services.Themes.Find("Spooky Halloween"));
            Assert.Equal(ShippedThemeNames.Halloween, services.Settings.Current.Calendar.Entries.Single(e => e.Id == "halloween").Theme);
            Assert.Empty(services.Holding.Items);
            Assert.False(window.Session.History.CanUndo);

            // Save Theme: the dialog stays open with the reason (a folder stands where the theme file would go).
            Directory.CreateDirectory(Path.Combine(services.Paths.ThemesFolder, "Locked Theme.json"));
            var save = new SaveThemeDialog(window, services, window.Session.History);
            Assert.True(save.SaveForTestAsync().IsCompleted);
            Assert.Null(save.SavedName);
            Assert.StartsWith("Couldn't save the Locked Theme theme: ", save.Note, StringComparison.Ordinal);
            Assert.Null(services.Themes.Find("Locked Theme"));
            Assert.False(window.Session.History.CanUndo);
            save.Close();

            // Restore Built-In Themes: what could be restored is one undo step, the rest is reported.
            _ = services.Themes.Delete(ShippedThemeNames.EasterEggs);
            _ = services.Themes.Delete(ShippedThemeNames.Halloween);
            Directory.CreateDirectory(Path.Combine(services.Paths.ThemesFolder, "Halloween.json"));
            var restore = new RestoreThemesDialog(window, services, window);
            restore.RestoreForTest();
            Assert.Equal(1, restore.Restored);
            Assert.NotNull(services.Themes.Find(ShippedThemeNames.EasterEggs));
            Assert.Null(services.Themes.Find(ShippedThemeNames.Halloween));
            Assert.StartsWith("Couldn't restore 1 of 2 themes: ", restore.Problem, StringComparison.Ordinal);
            Assert.True(window.WindowInfoBar.IsOpen);
            Assert.Equal(restore.Problem, window.WindowInfoBar.Message);
            Assert.Equal($"Restore {ShippedThemeNames.EasterEggs}", window.Session.History.UndoDescription);
        }, initial);
    }

    [Fact]
    public void AnUndoThatCantMoveAFileBackSaysWhyAndCanBeTriedAgain()
    {
        WithWindow(SettingsPageId.Themes, (services, window) =>
        {
            var page = (ThemesPage)window.GetPage(SettingsPageId.Themes);
            page.Delete(page.Cards.Single(c => c.Name == ShippedThemeNames.Halloween));
            HeldItem held = Assert.Single(services.Holding.Items);
            using (new FileStream(held.HeldPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                window.UndoLast();
            }

            Assert.True(window.WindowInfoBar.IsOpen);
            Assert.StartsWith("Couldn't undo \"Delete Halloween\": ", window.WindowInfoBar.Message, StringComparison.Ordinal);
            Assert.Null(services.Themes.Find(ShippedThemeNames.Halloween));
            Assert.Equal("Delete Halloween", window.Session.History.UndoDescription);

            window.UndoLast();
            Assert.NotNull(services.Themes.Find(ShippedThemeNames.Halloween));
            Assert.Equal("Delete Halloween", window.Session.History.RedoDescription);
        });
    }

    [Fact]
    public void RemovingBulbsWhenOneFileIsLockedRemovesTheOthersAndSaysWhy()
    {
        WithWindow(SettingsPageId.BulbFactory, (services, window) =>
        {
            var page = (BulbFactoryPage)window.GetPage(SettingsPageId.BulbFactory);
            Directory.CreateDirectory(services.Paths.MyBulbsFolder);
            string[] bundled = [.. Directory.EnumerateFiles(services.Paths.BundledBulbsFolder, "*.bul").Take(2)];
            string freeFile = Path.Combine(services.Paths.MyBulbsFolder, "Free Star.bul");
            string lockedFile = Path.Combine(services.Paths.MyBulbsFolder, "Locked Star.bul");
            File.Copy(bundled[0], freeFile);
            File.Copy(bundled[1], lockedFile);
            string free = services.Bulbs.LoadUserBulb(freeFile)?.Id ?? throw new InvalidOperationException("The copied bulb did not load.");
            string locked = services.Bulbs.LoadUserBulb(lockedFile)?.Id ?? throw new InvalidOperationException("The copied bulb did not load.");
            services.Settings.Update(
                s => s with { Current = s.Current with { Arrangement = s.Current.Arrangement.WithEdge(Side.Top, [free, locked]) } },
                SettingsChange.Internal);

            using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                page.Run(new BulbActionRequest(BulbAction.Remove, [free, locked]));
            }

            Assert.False(services.Bulbs.TryGetBulb(free, out _));
            Assert.True(services.Bulbs.TryGetBulb(locked, out _));
            Assert.Equal([locked], services.Settings.Current.Current.Arrangement.GetEdge(Side.Top));
            Assert.StartsWith("Removed ", window.Snackbar.Message, StringComparison.Ordinal);
            Assert.True(page.MessageBar.IsOpen);
            Assert.StartsWith("Couldn't remove ", page.MessageBar.Message, StringComparison.Ordinal);
            Assert.Single(services.Holding.Items);

            window.UndoLast();
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            Assert.True(services.Bulbs.TryGetBulb(free, out _));
            Assert.Equal([free, locked], services.Settings.Current.Current.Arrangement.GetEdge(Side.Top));
        });
    }

    [Fact]
    public void NavigationArrowsMoveTheFocusAndEnterOrSpaceOpensThePage()
    {
        WithWindow(SettingsPageId.Themes, (services, window) =>
        {
            ListBoxItem Item(SettingsPageId id) => (ListBoxItem)window.Navigation.ItemContainerGenerator.ContainerFromIndex((int)id);
            foreach (Key key in new[] { Key.Down, Key.Up, Key.Home, Key.End, Key.PageDown })
            {
                UiHarness.Press(Item(SettingsPageId.Themes), key);
                Assert.Equal(SettingsPageId.Themes, window.CurrentPage);
                Assert.Equal((int)SettingsPageId.Themes, window.Navigation.SelectedIndex);
            }

            UiHarness.Press(Item(SettingsPageId.General), Key.Enter);
            Assert.Equal(SettingsPageId.General, window.CurrentPage);
            UiHarness.Press(Item(SettingsPageId.Home), Key.Space);
            Assert.Equal(SettingsPageId.Home, window.CurrentPage);
        });
    }

    [Fact]
    public void TheHeaderActionsMoveUnderTheTitleWhenTheHeaderIsTooNarrow()
    {
        WithWindow(SettingsPageId.Themes, (services, window) =>
        {
            var page = (ThemesPage)window.GetPage(SettingsPageId.Themes);
            void AssertTitleReadable(string what)
            {
                TextBlock heading = UiHarness.Descendants<TextBlock>(window.Header).First(t => t.Text == window.Header.Title);
                var full = new TextBlock { Text = heading.Text, FontSize = heading.FontSize, FontWeight = heading.FontWeight, FontFamily = heading.FontFamily };
                full.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(heading.ActualWidth >= full.DesiredSize.Width - 0.5, $"{what}: the title is {heading.ActualWidth} DIP wide; it needs {full.DesiredSize.Width}.");
            }

            // Every page keeps a readable title at the minimum width (760 DIP).
            foreach (SettingsPageId id in Enum.GetValues<SettingsPageId>())
            {
                window.ShowPage(id);
                UiHarness.Pump(TimeSpan.FromMilliseconds(150));
                AssertTitleReadable(id.ToString());
            }

            // A header too narrow for the title, the actions and Undo/Redo side by side: the actions go under the title.
            window.ShowPage(SettingsPageId.Themes);
            window.MinWidth = 400;
            window.Width = 600;
            UiHarness.Pump(TimeSpan.FromMilliseconds(150));
            Assert.Equal(Visibility.Visible, window.HeaderActionsBelow.Visibility);
            Assert.Same(page.HeaderActions, window.HeaderActionsBelow.Content);
            Assert.Null(window.Header.Actions);
            AssertTitleReadable("Themes at 600 DIP");

            window.Width = 1240;
            UiHarness.Pump(TimeSpan.FromMilliseconds(150));
            Assert.Equal(Visibility.Collapsed, window.HeaderActionsBelow.Visibility);
            Assert.Null(window.HeaderActionsBelow.Content);
            Assert.Same(page.HeaderActions, window.Header.Actions);
        }, width: 760);
    }

    [Fact]
    public void FlashSettingsOfferWavesAndCombinationAndNameSmoothFading()
    {
        WithWindow(SettingsPageId.Home, (services, window) =>
        {
            var home = (HomePage)window.GetPage(SettingsPageId.Home);
            ComboBox combo = home.FlashGroup.PatternCombo;
            ComboBoxItem[] items = [.. combo.Items.OfType<ComboBoxItem>().Where(i => i.Content is PatternChoice)];
            FlashPatternId[] patterns = [.. items.Select(i => ((PatternChoice)i.Content).Pattern)];
            Assert.Equal([.. FlashPatterns.Classic, .. FlashPatterns.New, FlashPatternId.Waves, FlashPatternId.Combination], patterns);

            combo.SelectedItem = items[^2];
            Assert.Equal(FlashPatternId.Waves, services.Settings.Current.Current.Flash.Pattern);
            services.Settings.Update(s => s with { Current = s.Current with { Flash = s.Current.Flash with { Pattern = FlashPatternId.Combination } } }, SettingsChange.Internal);
            Assert.Same(items[^1], combo.SelectedItem);

            CheckBox smooth = home.FlashGroup.SmoothFadingCheck;
            Assert.Equal("Smooth Fading", UIElementAutomationPeer.CreatePeerForElement(smooth).GetName());
            Assert.Equal('M', char.ToUpperInvariant(UiHarness.Descendants<AccessText>(smooth).Single().AccessKey));
        });
    }

    [Fact]
    public void ThemeCardsShowTheWholeSummaryAndGroupsSayWhatTheyDo()
    {
        WithWindow(SettingsPageId.Themes, (services, window) =>
        {
            var page = (ThemesPage)window.GetPage(SettingsPageId.Themes);
            TextBlock[] summaries = [.. UiHarness.Descendants<TextBlock>(page).Where(t => t.DataContext is ThemeCardItem card && t.Text == card.Summary && t.IsVisible)];
            Assert.True(summaries.Length >= 19, $"{summaries.Length} summaries are shown.");
            double pixelsPerDip = VisualTreeHelper.GetDpi(page).PixelsPerDip;
            foreach (TextBlock summary in summaries)
            {
                var text = new FormattedText(summary.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(summary.FontFamily, summary.FontStyle, summary.FontWeight, summary.FontStretch), summary.FontSize, Brushes.Black, pixelsPerDip)
                {
                    MaxTextWidth = summary.ActualWidth,
                };
                Assert.True(text.Height <= summary.ActualHeight + 0.5, $"\"{summary.Text}\" needs {text.Height} DIP; the card gives it {summary.ActualHeight}.");
            }

            var home = (HomePage)window.GetPage(SettingsPageId.Home);
            window.ShowPage(SettingsPageId.Home);
            UiHarness.Pump(TimeSpan.FromMilliseconds(150));
            Card[] cards = [.. UiHarness.Descendants<Card>(page).Concat(UiHarness.Descendants<Card>(home)).Where(c => c.Header is string)];
            Assert.True(cards.Length >= 6);
            Assert.All(cards, card => Assert.False(string.IsNullOrWhiteSpace(card.Description), $"\"{card.Header}\" has no description."));
        });
    }

    private void WithWindow(SettingsPageId page, Action<UiTestServices, SettingsWindow> test, AppSettings? initial = null, double width = 1240) => ui.Run(() =>
    {
        using var services = new UiTestServices(initial);
        services.IndexTask.Wait(TimeSpan.FromSeconds(120));
        var window = new SettingsWindow(services);
        UiHarness.ApplyAppResources(window, dark: false);
        UiHarness.PlaceOffScreen(window, width, 800);
        window.Show();
        try
        {
            window.ShowPage(page);
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            test(services, window);
        }
        finally
        {
            window.Close();
        }
    }, TimeSpan.FromMinutes(3));
}
