using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui.PagesA;

/// <summary>
/// Review r1 #34 on the other pages: switches, radios and segments used through UI Automation (Toggle, Select), which
/// raise no Click, apply their setting; showing the settings again changes nothing.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class AutomationTogglesTests(UiThread ui)
{
    [Fact]
    public void HomeSwitchesAndSizesFollowUiAutomation() => PagesAHarness.WithWindow(ui, SettingsPageId.Home, (services, window) =>
    {
        var home = (HomePage)window.GetPage(SettingsPageId.Home);
        AppSettings Now() => services.Settings.Current;

        bool music = Now().Music.Enabled;
        PagesAHarness.Toggle(home.MusicToggle);
        Assert.Equal(!music, Now().Music.Enabled);

        PagesAHarness.Select(home.LargeRadio);
        Assert.Equal(BulbSize.Large, Now().Lights.Size);

        int changes = 0;
        services.Settings.Changed += (_, _) => changes++;
        PagesAHarness.Select(home.SmallRadio);
        Assert.Equal(BulbSize.Small, Now().Lights.Size);
        Assert.Equal(1, changes);
    });

    [Fact]
    public void ThemesAndBulbFactoryTogglesFollowUiAutomation() => PagesAHarness.WithWindow(ui, SettingsPageId.Themes, (services, window) =>
    {
        var themes = (ThemesPage)window.GetPage(SettingsPageId.Themes);
        bool notify = services.Settings.Current.Calendar.Notify;
        PagesAHarness.Toggle(themes.TellMeCheck);
        Assert.Equal(!notify, services.Settings.Current.Calendar.Notify);

        window.ShowPage(SettingsPageId.BulbFactory);
        var factory = (BulbFactoryPage)window.GetPage(SettingsPageId.BulbFactory);
        PagesAHarness.Toggle(factory.AllEdgesButton);
        Assert.Equal(true, factory.AllEdgesButton.IsChecked);
        Assert.Equal(false, factory.WholeFrameButton.IsChecked);

        // Toggling the chosen segment again keeps it chosen.
        PagesAHarness.Toggle(factory.AllEdgesButton);
        Assert.Equal(true, factory.AllEdgesButton.IsChecked);
    });
}
