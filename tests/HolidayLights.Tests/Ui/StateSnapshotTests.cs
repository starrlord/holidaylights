using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using HolidayLights.App.Controls;
using HolidayLights.App.Gallery;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Audio;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>
/// The visual harness of the Settings window's less common states: InfoBars, the lights turned off, a song playing, open
/// menus and drop-down lists, the Holiday Lights 5.4 group, theme badges and Recent Settings, every screen saver status
/// and the navigation rail. Each state is asserted; with <c>HL_UI_SNAPSHOTS</c> it is also written as PNG files in the
/// light and the dark theme at 150 %.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class StateSnapshotTests(UiThread ui)
{
    private const string MissingBulb = "user:Lost Star";

    [Fact]
    public void HomeShowsTheLightsTurnedOffAndThePlayingSong()
    {
        WithWindow(SettingsPageId.Home, 1240, 800, (services, window, shoot) =>
        {
            services.LightsFake.SetLightsOn(false);
            SongInfo song = services.Songs.Songs[0];
            services.MusicFake.SetState(new MusicState { Status = MusicStatus.Playing, CurrentSong = song, Position = TimeSpan.FromSeconds(42) });
            UiHarness.Pump(TimeSpan.FromMilliseconds(150));
            var home = (HomePage)window.GetPage(SettingsPageId.Home);
            Assert.Equal("The lights are off.", home.StatusText.Text);
            Assert.Equal($"Playing: {song.Title}", home.MusicText.Text);
            shoot("Home-LightsOff", (FrameworkElement)window.Content);
            shoot("Home-LightsOff-full", (FrameworkElement)home);
        });
    }

    [Fact]
    public void BulbFactoryShowsItsInfoBarsAndTheDetailsView()
    {
        AppSettings initial = new AppSettings() with
        {
            Current = new ThemeableSettings { Arrangement = SlotAssignment.Classic54Default.WithEdge(Side.Top, [BulbIds.BuiltIn("standard-bulbs"), MissingBulb]) },
        };
        WithWindow(SettingsPageId.BulbFactory, 1240, 800, (services, window, shoot) =>
        {
            services.LightsFake.SetStatus(services.Lights.Status with { Health = LightsHealth.SoftwareRendering });
            var page = (BulbFactoryPage)window.GetPage(SettingsPageId.BulbFactory);
            RadioButton details = UiHarness.Descendants<RadioButton>(page.List).Single(r => System.Windows.Automation.AutomationProperties.GetName(r) == "Details");
            details.IsChecked = true;
            Assert.True(page.List.Select(BulbIds.BuiltIn("candy-canes")));
            UiHarness.Pump(TimeSpan.FromMilliseconds(300));
            Assert.True(page.MissingBar.IsOpen);
            Assert.Equal("1 bulb in your arrangement is missing.", page.MissingBar.Message);
            Assert.True(page.SoftwareDrawingBar.IsOpen);
            Assert.Equal(BulbListView.Details, services.Settings.Current.Ui.Gallery.View);
            shoot("BulbFactory-InfoBars", (FrameworkElement)window.Content);
        }, initial);
    }

    [Fact]
    public void TheBulbListShowsSkeletonTilesWhileAddOnsAreIndexed()
    {
        AppSettings initial = new AppSettings();
        ui.Run(() =>
        {
            string? folder = UiHarness.SnapshotFolder;
            foreach (bool dark in folder is null ? [false] : new[] { false, true })
            {
                using var services = new UiTestServices(initial);
                services.IndexTask.Wait(TimeSpan.FromSeconds(120));
                services.CatalogOverride = new LoadingCatalog(services.Bulbs);
                var window = new SettingsWindow(services);
                UiHarness.ApplyAppResources(window, dark);
                UiHarness.PlaceOffScreen(window, 1240, 800);
                window.Show();
                try
                {
                    window.ShowPage(SettingsPageId.BulbFactory);
                    UiHarness.Pump(TimeSpan.FromMilliseconds(folder is null ? 200 : 900));
                    var page = (BulbFactoryPage)window.GetPage(SettingsPageId.BulbFactory);
                    Assert.Equal(49, page.List.Items.Count);
                    Assert.Equal("Loading add-on bulbs… 640 of 1,501", page.List.ResultLine.Text);
                    ListBox tiles = UiHarness.Descendants<ListBox>(page.List).First(l => l.IsVisible);
                    Assert.Equal(49 + SkeletonItem.MaxShown, tiles.Items.Count);

                    // Select All selects the bulbs, never the placeholders.
                    tiles.SelectAll();
                    Assert.Equal(49, tiles.SelectedItems.Count);
                    Assert.All(tiles.SelectedItems.Cast<object>(), item => Assert.IsType<BulbItem>(item));
                    Assert.True(page.List.Select(BulbIds.BuiltIn("candy-canes")));
                    if (folder is not null)
                    {
                        // The row where the listed bulbs end and the placeholders begin.
                        tiles.ScrollIntoView(tiles.Items[49]);
                        UiHarness.Pump(TimeSpan.FromMilliseconds(400));
                        UiHarness.SavePng(UiHarness.Render((FrameworkElement)window.Content, dark), Path.Combine(folder, $"State-BulbList-Loading-{(dark ? "dark" : "light")}.png"));
                    }
                }
                finally
                {
                    window.Close();
                }
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void MenusAndDropDownsOpenOverTheBulbFactory()
    {
        WithWindow(SettingsPageId.BulbFactory, 1240, 800, (services, window, shoot) =>
        {
            var page = (BulbFactoryPage)window.GetPage(SettingsPageId.BulbFactory);
            ComboBox patterns = UiHarness.Descendants<ComboBox>(page).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Flash Pattern");
            patterns.IsDropDownOpen = true;
            UiHarness.Pump(TimeSpan.FromMilliseconds(300));
            FrameworkElement list = Assert.Single(UiHarness.OpenPopups());
            shoot("Popup-FlashPatterns", list);
            patterns.IsDropDownOpen = false;
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));

            Assert.True(page.List.Select(BulbIds.BuiltIn("candy-canes")));
            page.List.OpenMenu(page.Bar);
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            FrameworkElement bulbMenu = Assert.Single(UiHarness.OpenPopups());
            shoot("Popup-BulbMenu", bulbMenu);
            CloseMenus(bulbMenu);

            ArrangementChip chip = UiHarness.Descendants<ArrangementChip>(page.Editor).First(c => c.BulbId is not null && !c.IsPlus);
            chip.Focus();
            UiHarness.Press(chip, Key.Apps);
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            FrameworkElement chipMenu = Assert.Single(UiHarness.OpenPopups());
            shoot("Popup-ChipMenu", chipMenu);
            CloseMenus(chipMenu);
        });
    }

    [Fact]
    public void GeneralShowsTheHolidayLights54GroupWithItsFixes()
    {
        AppSettings initial = new AppSettings() with
        {
            Import54 = new Import54Record { Date = new DateOnly(2026, 10, 8), FactoryDefaults = true, Themes = 11 },
        };
        WithWindow(SettingsPageId.General, 1240, 800, (services, window, shoot) =>
        {
            services.LegacyImporterFake.Present = true;
            services.LegacyLeftoversFake.State = new LegacyLeftoverState(true, @"C:\Users\Pat\Start Menu\Programs\Startup\Holiday Lights.lnk");
            services.SaverRegistrationFake.Status = new ScreenSaverStatus(ScreenSaverState.Legacy54, @"C:\WINDOWS\system32\HOLIDA~1.SCR", 600, false);
            var page = (GeneralPage)window.GetPage(SettingsPageId.General);
            page.OnShown();
            UiHarness.Pump(TimeSpan.FromMilliseconds(150));
            Assert.Equal(Visibility.Visible, page.LegacyCard.Visibility);
            Assert.Equal(3, page.Leftovers.Children.Count);
            Assert.StartsWith("Imported on ", page.ImportStatus.Text, StringComparison.Ordinal);
            shoot("General-54-full", page);
        }, initial);
    }

    [Fact]
    public void ThemesShowsMyThemesTheirBadgesAndRecentSettings()
    {
        WithWindow(SettingsPageId.Themes, 1240, 800, (services, window, shoot) =>
        {
            AppSettings settings = services.Settings.Current;
            services.Themes.Save(services.ThemeService.Capture("Grandma's Lights", settings) with
            {
                Arrangement = SlotAssignment.Classic54Default.WithCorner(Corner.TopLeft, MissingBulb),
            });
            ThemeDefinition christmas2 = services.Themes.Find(ShippedThemeNames.Christmas2)!;
            services.Themes.Save(christmas2 with { Flash = new ThemeFlash { Pattern = FlashPatternId.Twinkle, Interval = 4 } });
            services.Settings.Update(
                s => s with
                {
                    RecentSettings =
                    [
                        new RecentSettingsEntry { Label = "Before Halloween (automatic)", Date = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), Values = s.Current },
                        new RecentSettingsEntry { Label = "Holiday Lights 5.4 Settings", Date = new DateTimeOffset(2026, 10, 8, 9, 30, 0, TimeSpan.Zero), Values = s.Current },
                    ],
                },
                SettingsChange.Internal);
            var page = (ThemesPage)window.GetPage(SettingsPageId.Themes);
            page.RecentExpander.IsExpanded = true;
            UiHarness.Pump(TimeSpan.FromMilliseconds(400));
            Assert.Equal(Visibility.Visible, page.MyThemesHeader.Visibility);
            shoot("Themes-Mine-full", page);
        });
    }

    [Theory]
    [InlineData(ScreenSaverState.Ours, false, "Holiday Lights is your screen saver.")]
    [InlineData(ScreenSaverState.OursButTurnedOff, false, "Holiday Lights is selected, but the screen saver is turned off.")]
    [InlineData(ScreenSaverState.Legacy54, false, "Your screen saver is still set to Holiday Lights 5.4, which can't start on this version of Windows.")]
    [InlineData(ScreenSaverState.NotOurs, true, "Your organization manages the screen saver on this PC.")]
    public void ScreenSaverStatusCardShowsEveryState(ScreenSaverState state, bool policy, string text)
    {
        WithWindow(SettingsPageId.ScreenSaver, 1240, 800, (services, window, shoot) =>
        {
            services.SaverRegistrationFake.Status = new ScreenSaverStatus(state, @"C:\HolidayLights\Holiday Lights.scr", 900, policy);
            var page = (ScreenSaverPage)window.GetPage(SettingsPageId.ScreenSaver);
            page.OnShown();
            UiHarness.Pump(TimeSpan.FromMilliseconds(150));
            Assert.Equal(text, page.StatusText.Text);
            shoot($"ScreenSaver-Status-{state}{(policy ? "-Policy" : "")}", page.StatusCard);
        });
    }

    [Theory]
    [InlineData(SettingsPageId.Home)]
    [InlineData(SettingsPageId.MusicBox)]
    [InlineData(SettingsPageId.ScreenSaver)]
    [InlineData(SettingsPageId.Themes)]
    [InlineData(SettingsPageId.General)]
    public void EveryPageFitsTheNarrowWindow(SettingsPageId id)
    {
        WithWindow(id, 900, 700, (services, window, shoot) =>
        {
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            var content = (FrameworkElement)window.Content;
            Assert.True(window.ActualWidth < SettingsWindow.CompactWidth);
            shoot($"Narrow-{id}", content);
        });
    }

    [Theory]
    [InlineData(SettingsPageId.Home)]
    [InlineData(SettingsPageId.BulbFactory)]
    [InlineData(SettingsPageId.General)]
    public void PagesRenderWithTheHighContrastResources(SettingsPageId id) => ui.Run(() =>
    {
        using var services = new UiTestServices();
        services.IndexTask.Wait(TimeSpan.FromSeconds(120));
        var window = new SettingsWindow(services);
        UiHarness.ApplyAppResources(window, dark: false, highContrast: true);
        UiHarness.PlaceOffScreen(window, 1240, 800);
        window.Show();
        try
        {
            window.ShowPage(id);
            UiHarness.Pump(TimeSpan.FromMilliseconds(UiHarness.SnapshotFolder is null ? 150 : 700));
            Assert.Same(SystemColors.WindowBrush, window.FindResource(AppResourceKeys.NightWellBrush));
            BitmapSource shot = UiHarness.Render((FrameworkElement)window.Content, dark: false);
            Assert.True(shot.PixelWidth > 100);
            if (UiHarness.SnapshotFolder is { } folder)
            {
                UiHarness.SavePng(shot, Path.Combine(folder, $"State-HighContrast-{id}.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }, TimeSpan.FromMinutes(3));

    private static void CloseMenus(FrameworkElement popupRoot)
    {
        foreach (ContextMenu menu in UiHarness.Descendants<ContextMenu>(popupRoot))
        {
            menu.IsOpen = false;
        }

        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Opens the real window off-screen on a page, runs the state's code and closes the window; with snapshots it does so
    /// in the light and the dark theme, and the state's code passes the elements to shoot.
    /// </summary>
    private void WithWindow(SettingsPageId page, double width, double height, Action<UiTestServices, SettingsWindow, Action<string, FrameworkElement>> state, AppSettings? initial = null) => ui.Run(() =>
    {
        string? folder = UiHarness.SnapshotFolder;
        foreach (bool dark in folder is null ? [false] : new[] { false, true })
        {
            using var services = new UiTestServices(initial);
            services.IndexTask.Wait(TimeSpan.FromSeconds(120));
            var window = new SettingsWindow(services);
            UiHarness.ApplyAppResources(window, dark);
            UiHarness.PlaceOffScreen(window, width, height);
            window.Show();
            try
            {
                window.ShowPage(page);
                UiHarness.Pump(TimeSpan.FromMilliseconds(folder is null ? 150 : 700));
                state(services, window, (name, element) =>
                {
                    UiHarness.Pump(TimeSpan.FromMilliseconds(folder is null ? 50 : 400));
                    BitmapSource shot = UiHarness.Render(element, dark);
                    Assert.True(shot.PixelWidth > 100, name);
                    if (folder is not null)
                    {
                        UiHarness.SavePng(shot, Path.Combine(folder, $"State-{name}-{(dark ? "dark" : "light")}.png"));
                    }
                });
            }
            finally
            {
                window.Close();
            }
        }
    }, TimeSpan.FromMinutes(3));
}
