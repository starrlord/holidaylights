using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HolidayLights.App.Controls;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui.PagesA;

/// <summary>
/// The General page (PRODUCT-SPEC 3.7) in a real Settings window: its access keys, its switches and radios used through UI
/// Automation, a settings change that neither reads the system again nor rebuilds the display diagram, and Import Again.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class GeneralPageFixTests(UiThread ui)
{
    [Fact]
    public void TheNewAccessKeysAreUniqueOnThePage() => PagesAHarness.WithWindow(ui, SettingsPageId.General, (services, window) =>
    {
        Assert.Empty(PagesAHarness.DuplicateAccessKeys(window));
        IReadOnlyList<(char Key, string Text)> keys = PagesAHarness.VisibleAccessKeys(window);
        Assert.Contains(('F', "Hide the Lights on a Display While a _Full-Screen App or Game Is Running There"), keys);
        Assert.Contains(('C', "Pause Musi_c While a Full-Screen App, Game or Presentation Is Running"), keys);
        Assert.Contains(('2', "Main Display Only (Like _2003)"), keys);
    });

    [Fact]
    public void UiAutomationTogglesAndSelectsApplyTheSettings() => PagesAHarness.WithWindow(ui, SettingsPageId.General, (services, window) =>
    {
        var page = (GeneralPage)window.GetPage(SettingsPageId.General);
        AppSettings Now() => services.Settings.Current;

        bool rest = Now().Rest.FullScreen;
        PagesAHarness.Toggle(page.RestFullScreenCheck);
        Assert.Equal(!rest, Now().Rest.FullScreen);
        Assert.StartsWith($"Turn {(rest ? "off" : "on")} \"Hide the Lights on a Display While a Full-Screen", window.Session.History.UndoDescription, StringComparison.Ordinal);

        PagesAHarness.Select(page.OnTopCard);
        Assert.Equal(BulbDrawing.OnTop, Now().Lights.Drawing);
        PagesAHarness.Select(page.BehindIconsCard);
        Assert.Equal(BulbDrawing.Desktop, Now().Lights.Drawing);
        Assert.True(Now().Lights.BehindIcons);

        PagesAHarness.Select(page.CrispRadio);
        Assert.Equal(SpriteStyle.Crisp, Now().Look.Pixels);
        PagesAHarness.Select(page.AllTogetherRadio);
        Assert.Equal(FrameMode.AllDisplaysTogether, Now().Lights.FrameMode);

        bool hotKey = Now().HotKeys.Lights.Enabled;
        PagesAHarness.Toggle(page.LightsHotKeyCheck);
        Assert.Equal(!hotKey, Now().HotKeys.Lights.Enabled);

        DisplayInfo second = services.Displays.Displays.First(d => !d.IsPrimary);
        CheckBox showLights = page.DisplayDiagram.Children.OfType<Border>()
            .Select(b => ((StackPanel)b.Child).Children.OfType<CheckBox>().Single())
            .Single(c => AutomationPropertiesName(c) == $"Show lights on display {second.Number}");
        PagesAHarness.Toggle(showLights);
        Assert.Equal([second.DeviceId], Now().Lights.Displays.Disabled);
    });

    [Fact]
    public void AClickChangesASettingOnce() => PagesAHarness.WithWindow(ui, SettingsPageId.General, (services, window) =>
    {
        var page = (GeneralPage)window.GetPage(SettingsPageId.General);
        int changes = 0;
        services.Settings.Changed += (_, _) => changes++;

        bool fading = services.Settings.Current.Look.SmoothFading;
        PagesAHarness.Click(page.SmoothFadingCheck);
        Assert.Equal(1, changes);
        Assert.Equal(!fading, services.Settings.Current.Look.SmoothFading);

        PagesAHarness.Click(page.LargeRadio);
        Assert.Equal(2, changes);
        Assert.Equal(BulbSize.Large, services.Settings.Current.Lights.Size);

        // Clicking the chosen radio again changes nothing.
        PagesAHarness.Click(page.LargeRadio);
        Assert.Equal(2, changes);
    });

    /// <summary>Review r1 #27: every 5.4 import (factory defaults too) gets the notice, naming the key that was imported.</summary>
    [Fact]
    public void TheHotKeyNoticeShowsForEveryImportAndNamesTheImportedKey()
    {
        AppSettings initial = new AppSettings() with
        {
            Import54 = new Import54Record { Date = new DateOnly(2026, 10, 8), FactoryDefaults = true, Themes = 11 },
        };
        initial = initial with
        {
            HotKeys = initial.HotKeys with
            {
                Location = initial.HotKeys.Location with { Enabled = true, Modifiers = HotKeyModifiers.Ctrl | HotKeyModifiers.Alt | HotKeyModifiers.Shift, Key = "L" },
            },
        };
        PagesAHarness.WithWindow(ui, SettingsPageId.General, (services, window) =>
        {
            var page = (GeneralPage)window.GetPage(SettingsPageId.General);
            Assert.True(page.HotKeyImportBar.IsOpen);
            Assert.Contains("Ctrl+Shift+L", page.HotKeyImportBar.Message, StringComparison.Ordinal);
            Assert.Contains("Ctrl+Alt+Shift+L", page.HotKeyImportBar.Message, StringComparison.Ordinal);
            Assert.Equal("Use Ctrl+Shift+L Anyway", page.HotKeyImportBar.ActionText);

            page.HotKeyImportBar.ActionCommand!.Execute(null);

            Assert.Equal(HotKeyModifiers.Ctrl | HotKeyModifiers.Shift, services.Settings.Current.HotKeys.Location.Modifiers);
            Assert.Equal("Use Ctrl+Shift+L as the hot key", window.Session.History.UndoDescription);
            Assert.False(page.HotKeyImportBar.IsOpen);
        }, initial);
    }

    [Fact]
    public void ASettingsChangeNeitherReadsTheSystemNorRebuildsTheDisplayDiagram()
    {
        AppSettings initial = new AppSettings() with
        {
            Import54 = new Import54Record { Date = new DateOnly(2026, 10, 8), FactoryDefaults = true, Themes = 11 },
        };
        PagesAHarness.WithWindow(ui, SettingsPageId.General, (services, window) =>
        {
            var page = (GeneralPage)window.GetPage(SettingsPageId.General);
            Assert.Equal(Visibility.Visible, page.LegacyCard.Visibility);
            Assert.Empty(page.Leftovers.Children);
            Assert.False(page.StartupBar.IsOpen);
            UIElement tile = page.DisplayDiagram.Children[0];

            // The system changes behind the page's back; a settings change shows the settings only.
            services.LegacyLeftoversFake.State = new LegacyLeftoverState(true, null);
            services.StartupFake.IsDisabledByWindows = true;
            bool fading = services.Settings.Current.Look.SmoothFading;
            services.Settings.Update(s => s with { Look = s.Look with { SmoothFading = !fading } }, SettingsChange.Edit("Change Smooth Fading"));
            Assert.Equal(!fading, page.SmoothFadingCheck.IsChecked);
            Assert.Same(tile, page.DisplayDiagram.Children[0]);
            Assert.Empty(page.Leftovers.Children);
            Assert.False(page.StartupBar.IsOpen);

            // A display setting rebuilds the diagram.
            services.Settings.Update(s => s with { Lights = s.Lights with { FrameMode = FrameMode.AllDisplaysTogether } }, SettingsChange.Edit("Frame all displays together"));
            Assert.NotSame(tile, page.DisplayDiagram.Children[0]);
            Assert.True(page.AllTogetherRadio.IsChecked);

            // Showing the page reads the system again.
            page.OnShown();
            Assert.Single(page.Leftovers.Children);
            Assert.True(page.StartupBar.IsOpen);
        }, initial);
    }

    [Fact]
    public void ImportAgainImportsAfterTheConfirmation() => PagesAHarness.WithWindow(ui, SettingsPageId.General, (services, window) =>
    {
        services.LegacyImporterFake.Present = true;
        var page = (GeneralPage)window.GetPage(SettingsPageId.General);
        page.OnShown();
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        Assert.Equal(Visibility.Visible, page.LegacyCard.Visibility);
        Assert.Null(services.Settings.Current.Import54);

        page.ImportAgainButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(PagesAHarness.PumpUntil(() => UiHarness.Descendants<ContentDialogCard>(window).Any()), "No confirmation.");
        ContentDialogCard card = UiHarness.Descendants<ContentDialogCard>(window).Single();
        UiHarness.Pump(TimeSpan.FromMilliseconds(150));
        card.ApplyTemplate();
        var import = Assert.IsType<Button>(card.Template.FindName("PART_PrimaryButton", card));
        Assert.Equal("Import", card.PrimaryButtonText);
        import.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.True(PagesAHarness.PumpUntil(() => services.Settings.Current.Import54 is not null), "The import did not finish.");
        Assert.True(PagesAHarness.PumpUntil(() => page.ImportAgainButton.IsEnabled));
        Assert.Equal("Import Holiday Lights 5.4 settings again", window.Session.History.UndoDescription);
        Assert.Equal("Imported your Holiday Lights 5.4 settings.", window.Snackbar.Message);
        Assert.StartsWith("Imported on ", page.ImportStatus.Text, StringComparison.Ordinal);
        Assert.False(page.ImportBar.IsOpen);
    });

    private static string AutomationPropertiesName(DependencyObject element) => System.Windows.Automation.AutomationProperties.GetName(element);
}
