using System.Windows;
using System.Windows.Controls;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>
/// Access keys (PRODUCT-SPEC 3.0.4): every access key shown on a page is unique there, with the page's richest state on
/// display (a selected bulb on Bulb Factory, Holiday Lights as the screen saver, the Advanced expander open), and the
/// 5.4 keys sit where the spec puts them.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class AccessKeyTests(UiThread ui)
{
    [Fact]
    public void EveryPageShowsUniqueAccessKeys() => ui.Run(() =>
    {
        using var services = new UiTestServices();
        services.IndexTask.Wait(TimeSpan.FromSeconds(120));
        services.SaverRegistrationFake.Status = new ScreenSaverStatus(ScreenSaverState.Ours, @"C:\HolidayLights\Holiday Lights.scr", 600, false);
        var window = new SettingsWindow(services);
        UiHarness.ApplyAppResources(window, dark: false);
        UiHarness.PlaceOffScreen(window, 1240, 3000);
        window.Show();
        try
        {
            var keys = new Dictionary<SettingsPageId, IReadOnlyList<(char Key, string Text)>>();
            foreach (SettingsPageId id in Enum.GetValues<SettingsPageId>())
            {
                window.ShowPage(id);
                UiHarness.Pump(TimeSpan.FromMilliseconds(250));
                PrepareRichestState(window, id);
                UiHarness.Pump(TimeSpan.FromMilliseconds(250));
                keys[id] = VisibleAccessKeys(window);
                string[] duplicates = [.. keys[id].GroupBy(k => k.Key).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(" | ", g.Select(k => k.Text))}")];
                Assert.True(duplicates.Length == 0, $"{id} repeats access keys: {string.Join("; ", duplicates)}");
            }

            Assert.Contains(('P', "_Peek"), keys[SettingsPageId.BulbFactory]);
            Assert.Contains(('B', "Clear All _Bulbs"), keys[SettingsPageId.BulbFactory]);
            Assert.Contains(('V', "Sa_ve Settings As Theme\u2026"), keys[SettingsPageId.BulbFactory]);
            Assert.Contains(('O', "Add T_o\u2026"), keys[SettingsPageId.BulbFactory]);
            Assert.Contains(('R', "Start Afte_r"), keys[SettingsPageId.ScreenSaver]);
            Assert.Contains(('U', "MIDI O_utput:"), keys[SettingsPageId.MusicBox]);
        }
        finally
        {
            window.Close();
        }
    }, TimeSpan.FromMinutes(3));

    /// <summary>Shows what a page can show at most: a selected bulb, the open Advanced expander.</summary>
    private static void PrepareRichestState(SettingsWindow window, SettingsPageId id)
    {
        switch (window.GetPage(id))
        {
            case BulbFactoryPage factory:
                Assert.True(factory.List.Select(BulbIds.BuiltIn("candy-canes")));
                break;
            case MusicBoxPage music:
                foreach (Expander expander in UiHarness.Descendants<Expander>(music))
                {
                    expander.IsExpanded = true;
                }

                break;
        }
    }

    /// <summary>The access keys of the visible labels of the window (pages, header, navigation and bottom bar).</summary>
    private static IReadOnlyList<(char Key, string Text)> VisibleAccessKeys(SettingsWindow window) =>
    [
        .. UiHarness.Descendants<AccessText>((DependencyObject)window.Content)
            .Where(text => text.IsVisible && text.AccessKey != default)
            .Select(text => (char.ToUpperInvariant(text.AccessKey), text.Text)),
    ];
}
