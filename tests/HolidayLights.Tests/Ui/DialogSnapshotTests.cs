using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HolidayLights.App.Controls;
using HolidayLights.App.Gallery;
using HolidayLights.App.Settings.Pages;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Dialogs;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>
/// The visual harness of the Settings dialogs (Save Theme, Theme Calendar, Restore Built-In Themes, Change Hot Key,
/// Import Details, Color, Choose a Bulb) and of the narrow Settings window, in the light and the dark Fluent theme at
/// 150 %, rendered off-screen and closed at once. With <c>HL_UI_SNAPSHOTS</c> it writes PNG files; without it, it renders
/// the light theme as a smoke test.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class DialogSnapshotTests(UiThread ui)
{
    [Fact]
    public void EveryDialogRendersInLightAndDarkThemes()
    {
        string? folder = UiHarness.SnapshotFolder;
        ui.Run(() =>
        {
            using var services = new UiTestServices();
            services.IndexTask.Wait(TimeSpan.FromSeconds(120));
            var record = new Import54Record
            {
                Date = new DateOnly(2026, 10, 8),
                Themes = 11,
                Report =
                [
                    new ImportReportItem { Item = "Flash Interval", Status = ImportItemStatus.Imported },
                    new ImportReportItem { Item = @"Themes\Christmas 1", Status = ImportItemStatus.AlreadyIncluded },
                    new ImportReportItem { Item = "Trial Start Date", Status = ImportItemStatus.NotImported, Reason = "obsolete" },
                ],
            };
            var dialogs = new (string Name, Func<Window> Create)[]
            {
                ("SaveTheme", () => new SaveThemeDialog(null, services, null)),
                ("ThemeCalendar", () => new ThemeCalendarDialog(null, services)),
                ("RestoreThemes", () => new RestoreThemesDialog(null, services, null)),
                ("ChangeHotKey", () => new ChangeHotKeyDialog(null, services, HotKeyAction.SwitchLocation, services.Settings.Current.HotKeys.Location)),
                ("ImportDetails", () => new ImportDetailsDialog(null, record)),
                ("Color", () => new ColorDialog(null, services, RgbColor.Red)),
                ("ChooseBulb", () => new ChooseBulbDialog(null, services, null)),
            };
            foreach (bool dark in folder is null ? [false] : new[] { false, true })
            {
                foreach ((string name, Func<Window> create) in dialogs)
                {
                    Window dialog = create();
                    UiHarness.ApplyAppResources(dialog, dark);
                    dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                    dialog.Left = -30000;
                    dialog.Top = -30000;
                    dialog.ShowActivated = false;
                    dialog.Show();
                    try
                    {
                        UiHarness.Pump(TimeSpan.FromMilliseconds(folder is null ? 100 : 700));
                        BitmapSource shot = UiHarness.Render((FrameworkElement)dialog.Content, dark);
                        Assert.True(shot.PixelWidth > 300, name);
                        if (folder is not null)
                        {
                            UiHarness.SavePng(shot, Path.Combine(folder, $"Dialog-{name}-{(dark ? "dark" : "light")}.png"));
                        }
                    }
                    finally
                    {
                        dialog.Close();
                    }
                }

                RenderNarrowWindow(services, dark, folder);
                RenderOverlays(services, dark, folder);
            }
        }, TimeSpan.FromMinutes(4));
    }

    /// <summary>A selected bulb with its "Add To..." flyout, a snackbar and a confirmation over the window.</summary>
    private static void RenderOverlays(UiTestServices services, bool dark, string? folder)
    {
        var window = new SettingsWindow(services);
        UiHarness.ApplyAppResources(window, dark);
        UiHarness.PlaceOffScreen(window, 1240, 800);
        window.Show();
        try
        {
            window.ShowPage(SettingsPageId.BulbFactory);
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            var page = (BulbFactoryPage)window.GetPage(SettingsPageId.BulbFactory);
            Assert.True(page.List.Select(BulbIds.BuiltIn("candy-canes")));
            UiHarness.Pump(TimeSpan.FromMilliseconds(folder is null ? 100 : 600));
            System.Windows.Controls.Primitives.Popup popup = AddToFlyout.Show(page.Bar, ["builtin:candy-canes"], "Add \"Candy Canes\" to",
                services.Settings.Current.Current.Arrangement, _ => { });
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            var flyout = (FrameworkElement)popup.Child;
            Assert.True(flyout.ActualWidth > 300);
            BitmapSource addTo = UiHarness.Render(flyout, dark);
            window.ShowSnackbar("Using Candy Canes on the whole frame.", "Undo", window.UndoLast);
            UiHarness.Pump(TimeSpan.FromMilliseconds(400));
            BitmapSource bar = UiHarness.Render((FrameworkElement)window.Content, dark);
            popup.IsOpen = false;
            _ = ContentDialogs.ShowAsync(window, new ContentDialogOptions("Reset Holiday Lights?",
                "This puts every setting back the way it was when Holiday Lights was installed. Your themes, bulbs, songs and pictures are kept, and Recent Settings keeps your current settings.",
                "Reset", "Cancel", PrimaryIsDestructive: true));
            UiHarness.Pump(TimeSpan.FromMilliseconds(folder is null ? 100 : 500));
            BitmapSource confirm = UiHarness.Render((FrameworkElement)VisualTreeHelper.GetChild(window, 0), dark);
            if (folder is not null)
            {
                string theme = dark ? "dark" : "light";
                UiHarness.SavePng(addTo, Path.Combine(folder, $"Overlay-AddTo-{theme}.png"));
                UiHarness.SavePng(bar, Path.Combine(folder, $"Overlay-SelectedBulb-{theme}.png"));
                UiHarness.SavePng(confirm, Path.Combine(folder, $"Overlay-Confirm-{theme}.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The window at 900 x 700 DIP: the navigation rail and the narrow Bulb Factory.</summary>
    private static void RenderNarrowWindow(UiTestServices services, bool dark, string? folder)
    {
        var window = new SettingsWindow(services);
        UiHarness.ApplyAppResources(window, dark);
        UiHarness.PlaceOffScreen(window, 900, 700);
        window.Show();
        try
        {
            window.ShowPage(SettingsPageId.BulbFactory);
            UiHarness.Pump(TimeSpan.FromMilliseconds(folder is null ? 100 : 900));
            BitmapSource shot = UiHarness.Render((FrameworkElement)window.Content, dark);
            Assert.True(shot.PixelWidth >= 1300);
            if (folder is not null)
            {
                UiHarness.SavePng(shot, Path.Combine(folder, $"Narrow-BulbFactory-{(dark ? "dark" : "light")}.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }
}
