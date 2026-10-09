using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using HolidayLights.App.Controls;
using HolidayLights.App.Gallery;
using HolidayLights.App.Preview;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Pages;
using HolidayLights.App.Styles;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui.Visuals;

/// <summary>
/// Review round 1 (ui-visuals): live regions speak, a snackbar's Undo never reverts another step, the missing-font note is
/// legible, the selected theme card says so, overlays follow the window's resources, the favorite star stays visible in
/// High Contrast, a removed bulb added again is shown, and the selected-bulb bar keeps its credits.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class UiFixesTests(UiThread ui)
{
    private static readonly string CandyCanes = BulbIds.BuiltIn("candy-canes");

    [Fact]
    public void TextLiveRegionsRaiseLiveRegionChangedWhenTheirTextChanges() => ui.Run(() =>
    {
        Assert.True(LiveRegions.IsInstalled);
        var live = new TextBlock();
        AutomationProperties.SetLiveSetting(live, AutomationLiveSetting.Polite);
        var quiet = new TextBlock();
        var raised = new List<UIElement>();
        void OnRaising(UIElement element) => raised.Add(element);
        Announcer.LiveRegionRaising += OnRaising;
        var window = new Window { Content = new StackPanel { Children = { live, quiet } } };
        UiHarness.PlaceOffScreen(window, 300, 200);
        window.Show();
        try
        {
            UiHarness.Pump(TimeSpan.FromMilliseconds(50));
            raised.Clear();
            live.Text = "1,550 bulbs";
            live.Text = "37 bulbs match \"snow\"";
            quiet.Text = "Not a live region";
            UiHarness.Pump(TimeSpan.FromMilliseconds(50));
            // Two quick changes are read once (with the last text); a text block that is not a live region stays quiet.
            Assert.Equal(1, raised.Count(e => ReferenceEquals(e, live)));
            Assert.DoesNotContain(quiet, raised);
        }
        finally
        {
            Announcer.LiveRegionRaising -= OnRaising;
            window.Close();
        }
    });

    [Fact]
    public void ASnackbarUndoNeverRevertsANewerStep() => WithPage((services, window, page) =>
    {
        page.Run(new BulbActionRequest(BulbAction.Use, [CandyCanes]));
        Assert.True(window.Snackbar.IsShown);
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        Assert.Equal("Undo", window.Snackbar.ActionText);

        // Smooth Fading records its own step within the 6 s: "Undo" would now revert it, not Candy Canes.
        CheckBox smooth = page.FlashGroup.SmoothFadingCheck;
        smooth.IsChecked = smooth.IsChecked != true;
        smooth.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        Assert.True(window.Snackbar.IsShown);
        Assert.Null(window.Snackbar.ActionText);

        // A snackbar shown after the last step keeps its Undo, which reverts that step.
        page.Run(new BulbActionRequest(BulbAction.Use, [BulbIds.BuiltIn("standard-bulbs")]));
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        Assert.Equal("Undo", window.Snackbar.ActionText);
    });

    [Fact]
    public void AMissingFontShowsItsNameInTheSubstituteAndTheNoteInTheUiFont()
    {
        FontChoice choice = FontPicker.ChoiceFor("Creepy", family => family == "Chiller");
        Assert.Equal("Creepy", choice.Name);
        Assert.Equal("(not installed - using Chiller)", choice.Note);
        Assert.Equal("Creepy (not installed - using Chiller)", choice.Text);
        Assert.Equal("Chiller", choice.Face.Source);

        FontChoice installed = FontPicker.ChoiceFor("Arial", _ => true);
        Assert.Null(installed.Note);
        Assert.Equal("Arial", installed.Name);
    }

    [Fact]
    public void TheSelectedHomeCardSaysSoToScreenReaders()
    {
        var card = new ThemeCardItem("Automatic", null, isAutomatic: true) { DateText = "Halloween today" };
        Assert.Equal("Automatic themes, Halloween today", card.AccessibleName);
        var changed = new List<string?>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        card.IsSelected = true;
        Assert.Equal("Automatic themes, Halloween today, selected", card.AccessibleName);
        Assert.Contains(nameof(ThemeCardItem.AccessibleName), changed);
    }

    [Fact]
    public void TheFavoriteStarUsesTheHighlightColourInHighContrast() => ui.Run(() =>
    {
        var resources = new ResourceDictionary();
        HighContrastResources.Apply(resources, highContrast: true);
        Assert.Same(SystemColors.HighlightBrush, resources[ThemeKeys.FavoriteStarBrush]);
        HighContrastResources.Apply(resources, highContrast: false);
        Assert.False(resources.Contains(ThemeKeys.FavoriteStarBrush));
    });

    [Fact]
    public void OverlaysFollowTheWindowsOwnResources() => ui.Run(() =>
    {
        using var services = new UiTestServices();
        var pill = new SolidColorBrush(Colors.DarkRed);
        var smokeFill = new SolidColorBrush(Color.FromArgb(0x4D, 0, 0, 0));
        var stage = new LightStage { Services = services, Width = 300, Height = 170 };
        var window = new Window { Content = new Grid { Children = { stage } } };
        window.Resources[ThemeKeys.StagePillBrush] = pill;
        window.Resources["ContentDialogSmokeFill"] = smokeFill;
        UiHarness.PlaceOffScreen(window, 400, 300);
        window.Show();
        try
        {
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            var panel = (Border)VisualTreeHelper.GetChild(stage, 0);
            Assert.Contains(panel, LogicalTreeHelper.GetChildren(stage).Cast<object>());
            Assert.Same(pill, panel.Background);

            _ = ContentDialogs.ShowAsync(window, new ContentDialogOptions("Discard Changes?", "Your changes will be lost.", "Discard", "Keep Editing", true));
            UiHarness.Pump(TimeSpan.FromMilliseconds(100));
            var content = (UIElement)window.Content;
            Adorner overlay = Assert.Single(AdornerLayer.GetAdornerLayer(content)!.GetAdorners(content)!);
            Border smoke = UiHarness.Descendants<Border>(overlay).First();
            Assert.Same(smokeFill, smoke.Background);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ARemovedBulbIsShownUnderRemovedBulbsWhenSelected() => WithPage((services, window, page) =>
    {
        string arrow = BulbIds.AddOn("Arrow");
        services.Bulbs.Hide(arrow);
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        page.List.Reload();
        Assert.True(page.List.Select(arrow));
        Assert.Equal(ShowOption.RemovedKey, page.List.CurrentOption.Key);

        // A listed bulb is found under All Bulbs again.
        Assert.True(page.List.Select(CandyCanes));
        Assert.Equal(ShowOption.AllKey, page.List.CurrentOption.Key);
    });

    [Fact]
    public void TheSelectedBulbBarKeepsItsCreditsInTheCompactSize() => WithPage((services, window, page) =>
    {
        Assert.True(page.List.Select(CandyCanes));
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        page.Bar.IsCompact = true;
        UiHarness.Pump(TimeSpan.FromMilliseconds(50));
        Assert.Equal(Visibility.Visible, page.Bar.CreditsText.Visibility);
        Assert.StartsWith("Art: ", page.Bar.CreditsText.Text, StringComparison.Ordinal);
        Assert.Equal(SelectedBulbBar.PermissionText, page.Bar.PermissionLine.Text);
        Assert.Equal(TextWrapping.Wrap, page.Bar.PermissionLine.TextWrapping);
        Assert.True(page.Bar.PermissionLine.IsVisible);
    });

    private void WithPage(Action<UiTestServices, SettingsWindow, BulbFactoryPage> test) => ui.Run(() =>
    {
        using var services = new UiTestServices();
        services.IndexTask.Wait(TimeSpan.FromSeconds(120));
        var window = new SettingsWindow(services);
        UiHarness.ApplyAppResources(window, dark: false);
        UiHarness.PlaceOffScreen(window, 1240, 800);
        window.Show();
        try
        {
            window.ShowPage(SettingsPageId.BulbFactory);
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            test(services, window, (BulbFactoryPage)window.GetPage(SettingsPageId.BulbFactory));
        }
        finally
        {
            window.Close();
        }
    }, TimeSpan.FromMinutes(3));
}
