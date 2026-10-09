using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HolidayLights.App.Controls;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui.PagesA;

/// <summary>
/// The Screen Saver page (PRODUCT-SPEC 3.5) in a real Settings window: the text message's blank lines, Remove Picture on
/// the chosen picture, "Picture not found", the chosen tiles scrolled into view, and the font and motion toggles used
/// through UI Automation.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class ScreenSaverPageFixTests(UiThread ui)
{
    [Theory]
    [InlineData("Merry Christmas\r\n\r\nFrom the Smiths", "Merry Christmas\r\n\r\nFrom the Smiths")]
    [InlineData("\r\n\r\n  Line one\r\n\r\nLine two", "Line one\r\n\r\nLine two")]
    [InlineData(" \t\r\nHappy Holidays!\r\n", "Happy Holidays!\r\n")]
    [InlineData("Line one\r\n\r\n", "Line one\r\n\r\n")]
    [InlineData("  \r\n ", "")]
    public void TheMessageLosesOnlyItsLeadingBlanksLike54(string typed, string message) =>
        Assert.Equal(message, ScreenSaverPage.CleanMessage(typed));

    [Fact]
    public void ABlankLineBetweenTheLinesOfTheMessageIsKept() => PagesAHarness.WithWindow(ui, SettingsPageId.ScreenSaver, (services, window) =>
    {
        var page = (ScreenSaverPage)window.GetPage(SettingsPageId.ScreenSaver);
        page.MessageBox.Text = "Merry Christmas\r\n\r\nFrom the Smiths";
        Assert.Equal("Merry Christmas\r\n\r\nFrom the Smiths", services.Settings.Current.Current.Saver.Message);

        page.MessageBox.Text = "\r\n  Hi";
        Assert.Equal("Hi", page.MessageBox.Text);
        Assert.Equal("Hi", services.Settings.Current.Current.Saver.Message);
    });

    [Fact]
    public void RemovingTheChosenPictureChoosesNoneInTheSameUndoStep() => PagesAHarness.WithWindow(ui, SettingsPageId.ScreenSaver, (services, window) =>
    {
        var page = (ScreenSaverPage)window.GetPage(SettingsPageId.ScreenSaver);
        PictureInfo chosen = services.Pictures.Pictures[0];
        services.Settings.Update(s => s with { Current = s.Current with { Saver = s.Current.Saver with { Picture = chosen.Id } } }, SettingsChange.Internal);

        page.RemovePicture(chosen);
        AppSettings after = services.Settings.Current;
        Assert.Equal(SaverPictures.None, after.Current.Saver.Picture);
        Assert.Contains(chosen.Id, after.Pictures.Hidden);
        SaverTile[] tiles = [.. page.PictureList.Items.Cast<SaverTile>()];
        Assert.DoesNotContain(tiles, t => t.Name == "Picture not found");
        Assert.DoesNotContain(tiles, t => MediaIds.Comparer.Equals(t.Key, chosen.Id));
        Assert.Equal(SaverPictures.None, Assert.IsType<SaverTile>(page.PictureList.SelectedItem).Key);
        Assert.Equal($"Remove {chosen.Title}", window.Session.History.UndoDescription);

        window.UndoLast();
        Assert.Equal(chosen.Id, services.Settings.Current.Current.Saver.Picture);
        Assert.DoesNotContain(chosen.Id, services.Settings.Current.Pictures.Hidden);
        Assert.Equal(chosen.Id, Assert.IsType<SaverTile>(page.PictureList.SelectedItem).Key);
    });

    [Fact]
    public void AChosenPictureShowsItsOwnTileUnlessItsFileIsMissing() => PagesAHarness.WithWindow(ui, SettingsPageId.ScreenSaver, (services, window) =>
    {
        var page = (ScreenSaverPage)window.GetPage(SettingsPageId.ScreenSaver);
        PictureInfo picture = services.Pictures.Pictures[0];

        // A theme can choose a removed bundled picture; the screen saver still shows it.
        services.Settings.Update(
            s => s with
            {
                Pictures = new HiddenItems { Hidden = [picture.Id] },
                Current = s.Current with { Saver = s.Current.Saver with { Picture = picture.Id } },
            },
            SettingsChange.Internal);
        SaverTile shown = Assert.IsType<SaverTile>(page.PictureList.SelectedItem);
        Assert.Equal(picture.Id, shown.Key);
        Assert.Equal(picture.Title, shown.Name);

        services.Settings.Update(s => s with { Current = s.Current with { Saver = s.Current.Saver with { Picture = MediaIds.User("Gone.png") } } }, SettingsChange.Internal);
        Assert.Equal("Picture not found", Assert.IsType<SaverTile>(page.PictureList.SelectedItem).Name);
    });

    [Fact]
    public void TheChosenAnimationScrollsIntoViewWithoutMovingThePage()
    {
        string last = SaverAnimations.All[^1];
        AppSettings initial = new AppSettings() with { Current = new ThemeableSettings() with { Saver = new SaverLook { Animation = last } } };
        PagesAHarness.WithWindow(ui, SettingsPageId.ScreenSaver, (services, window) =>
        {
            var page = (ScreenSaverPage)window.GetPage(SettingsPageId.ScreenSaver);
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            ScrollViewer list = UiHarness.Descendants<ScrollViewer>(page.AnimationList).First();
            ScrollViewer pageViewer = ElementTree.SelfAndAncestors(page).OfType<ScrollViewer>().First();
            Assert.True(list.ScrollableHeight > 0, "The animation list must scroll at this window size.");
            Assert.Equal(last, Assert.IsType<SaverTile>(page.AnimationList.SelectedItem).Key);
            Assert.True(IsInView(page.AnimationList), "The chosen animation is out of view.");
            Assert.Equal(0, pageViewer.VerticalOffset);

            // Another animation chosen elsewhere (Undo, a theme): the list follows.
            string first = SaverAnimations.All[0];
            services.Settings.Update(s => s with { Current = s.Current with { Saver = s.Current.Saver with { Animation = first } } }, SettingsChange.Edit("Use another animation"));
            UiHarness.Pump(TimeSpan.FromMilliseconds(200));
            Assert.True(IsInView(page.AnimationList), "The newly chosen animation is out of view.");
            Assert.Equal(0, list.VerticalOffset);
            Assert.Equal(0, pageViewer.VerticalOffset);
        }, initial);
    }

    [Fact]
    public void UiAutomationTogglesApplyTheFontStylesAndSmoothMotion() => PagesAHarness.WithWindow(ui, SettingsPageId.ScreenSaver, (services, window) =>
    {
        var page = (ScreenSaverPage)window.GetPage(SettingsPageId.ScreenSaver);
        bool bold = services.Settings.Current.Current.Saver.Font.Bold;
        PagesAHarness.Toggle(page.BoldButton);
        Assert.Equal(!bold, services.Settings.Current.Current.Saver.Font.Bold);
        PagesAHarness.Toggle(page.StrikeoutButton);
        Assert.True(services.Settings.Current.Current.Saver.Font.Strikeout);

        bool smooth = services.Settings.Current.Look.SmoothSaverMotion;
        PagesAHarness.Toggle(page.SmoothMotionCheck);
        Assert.Equal(!smooth, services.Settings.Current.Look.SmoothSaverMotion);

        int changes = 0;
        services.Settings.Changed += (_, _) => changes++;
        PagesAHarness.Click(page.ItalicButton);
        Assert.Equal(1, changes);
        Assert.True(services.Settings.Current.Current.Saver.Font.Italic);
    });

    /// <summary>True when the selected tile of a tile list lies inside the list's viewport.</summary>
    private static bool IsInView(ListBox list)
    {
        var tile = (FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
        ScrollContentPresenter viewport = UiHarness.Descendants<ScrollContentPresenter>(list).First();
        Rect bounds = tile.TransformToAncestor(viewport).TransformBounds(new Rect(tile.RenderSize));
        return bounds.Top >= -0.5 && bounds.Bottom <= viewport.ActualHeight + 0.5;
    }
}
