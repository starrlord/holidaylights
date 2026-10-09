using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using HolidayLights.App.Settings;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>
/// The visual harness of the Settings window: every page of the real window, in the light and the dark Fluent theme at
/// 150 %, rendered off-screen. With <c>HL_UI_SNAPSHOTS=&lt;folder&gt;</c> it writes one PNG per page and theme (the
/// window as seen, and the whole scrolling page); without it, it renders the light theme as a smoke test.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class SettingsSnapshotTests(UiThread ui)
{
    [Fact]
    [Trait("Category", "Desktop")]
    public void EveryPageRendersInLightAndDarkThemes()
    {
        string? folder = UiHarness.SnapshotFolder;
        ui.Run(() =>
        {
            using var services = new UiTestServices();
            if (folder is not null)
            {
                services.IndexTask.Wait(TimeSpan.FromSeconds(120));
            }

            foreach (bool dark in folder is null ? [false] : new[] { false, true })
            {
                RenderAllPages(services, dark, folder);
            }
        }, TimeSpan.FromMinutes(4));
    }

    private static void RenderAllPages(UiTestServices services, bool dark, string? folder)
    {
        var window = new SettingsWindow(services);
        UiHarness.ApplyAppResources(window, dark);
        UiHarness.PlaceOffScreen(window, 1240, 800);
        window.Show();
        try
        {
            string theme = dark ? "dark" : "light";
            foreach (SettingsPageId page in Enum.GetValues<SettingsPageId>())
            {
                window.ShowPage(page);
                UiHarness.Pump(TimeSpan.FromMilliseconds(folder is null ? 100 : 900));
                BitmapSource shot = UiHarness.Render((FrameworkElement)window.Content, dark);
                Assert.True(shot.PixelWidth >= 1800);
                if (folder is null)
                {
                    continue;
                }

                UiHarness.SavePng(shot, Path.Combine(folder, $"{page}-{theme}.png"));
                if (window.GetPage(page) is FrameworkElement { Parent: ScrollViewer } content)
                {
                    UiHarness.SavePng(UiHarness.Render(content, dark), Path.Combine(folder, $"{page}-full-{theme}.png"));
                }
            }
        }
        finally
        {
            window.Close();
        }
    }
}
