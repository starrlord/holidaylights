using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Pages;
using HolidayLights.Audio;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui.PagesA;

/// <summary>
/// The Music Box page (PRODUCT-SPEC 3.4) in a real Settings window: switches, radios and song check boxes used through UI
/// Automation, a click changing a setting once, double-clicks on a row's buttons, Add Song's results, Resume after the
/// paused song went away, the access keys while paused, and the progress bar with keyboard focus.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class MusicBoxPageFixTests(UiThread ui)
{
    [Fact]
    public void UiAutomationTogglesAndSelectsApplyTheSettings() => PagesAHarness.WithWindow(ui, SettingsPageId.MusicBox, (services, window) =>
    {
        var page = (MusicBoxPage)window.GetPage(SettingsPageId.MusicBox);
        AppSettings Now() => services.Settings.Current;

        bool music = Now().Music.Enabled;
        PagesAHarness.Toggle(page.MusicToggle);
        Assert.Equal(!music, Now().Music.Enabled);
        Assert.Equal(music, page.MusicOffBar.IsOpen);

        bool muted = Now().Music.Muted;
        PagesAHarness.Toggle(page.MuteButton);
        Assert.Equal(!muted, Now().Music.Muted);

        FlashPatternId pattern = Now().Current.Flash.Pattern;
        Assert.NotEqual(FlashPatternId.DanceToMusic, pattern);
        PagesAHarness.Toggle(page.DanceCheck);
        Assert.Equal(FlashPatternId.DanceToMusic, Now().Current.Flash.Pattern);
        PagesAHarness.Toggle(page.DanceCheck);
        Assert.Equal(pattern, Now().Current.Flash.Pattern);

        PagesAHarness.Select(page.NeverRadio);
        Assert.Equal(PlayMode.Never, Now().Current.Music.Mode);
        PagesAHarness.Select(page.IntermittentRadio);
        Assert.Equal(PlayMode.Intermittently, Now().Current.Music.Mode);

        var item = (ListBoxItem)page.SongList.ItemContainerGenerator.ContainerFromIndex(1);
        CheckBox box = UiHarness.Descendants<CheckBox>(item).First();
        SongRow row = page.Rows[1];
        Assert.DoesNotContain(row.Id, Now().Current.Music.DisabledSongs);
        PagesAHarness.Toggle(box);
        Assert.Contains(row.Id, Now().Current.Music.DisabledSongs);
        Assert.False(row.IsChecked);
        PagesAHarness.Toggle(box);
        Assert.DoesNotContain(row.Id, Now().Current.Music.DisabledSongs);
        Assert.True(row.IsChecked);
    });

    [Fact]
    public void AClickChangesASettingOnce() => PagesAHarness.WithWindow(ui, SettingsPageId.MusicBox, (services, window) =>
    {
        var page = (MusicBoxPage)window.GetPage(SettingsPageId.MusicBox);
        int changes = 0;
        services.Settings.Changed += (_, _) => changes++;

        PagesAHarness.Click(page.MuteButton);
        Assert.Equal(1, changes);
        Assert.True(services.Settings.Current.Music.Muted);

        PagesAHarness.Click(page.NeverRadio);
        Assert.Equal(2, changes);
        Assert.Equal(PlayMode.Never, services.Settings.Current.Current.Music.Mode);

        var item = (ListBoxItem)page.SongList.ItemContainerGenerator.ContainerFromIndex(0);
        SongRow row = page.Rows[0];
        PagesAHarness.Click(UiHarness.Descendants<CheckBox>(item).First());
        Assert.Equal(3, changes);
        Assert.Contains(row.Id, services.Settings.Current.Current.Music.DisabledSongs);
        Assert.Equal($"Turn off {row.Title}", window.Session.History.UndoDescription);
    });

    [Fact]
    public void ADoubleClickOnASongsCheckBoxOrPlayButtonDoesNotPlayIt() => PagesAHarness.WithWindow(ui, SettingsPageId.MusicBox, (services, window) =>
    {
        var page = (MusicBoxPage)window.GetPage(SettingsPageId.MusicBox);
        var item = (ListBoxItem)page.SongList.ItemContainerGenerator.ContainerFromIndex(0);
        CheckBox box = UiHarness.Descendants<CheckBox>(item).First();
        Button play = UiHarness.Descendants<Button>(item).First();

        PagesAHarness.DoubleClick(item, UiHarness.Descendants<FrameworkElement>(box).FirstOrDefault() ?? box);
        PagesAHarness.DoubleClick(item, box);
        PagesAHarness.DoubleClick(item, UiHarness.Descendants<TextBlock>(play).First());
        Assert.DoesNotContain(services.MusicFake.Commands, c => c.StartsWith("PlayNow", StringComparison.Ordinal));

        SongRow row = page.Rows[0];
        TextBlock title = UiHarness.Descendants<TextBlock>(item).First(t => t.Text == row.Title);
        PagesAHarness.DoubleClick(item, title);
        Assert.Equal([$"PlayNow {row.Id}"], services.MusicFake.Commands);
    });

    [Fact]
    public void AddSongResultsStayUntilClosed() => PagesAHarness.WithWindow(ui, SettingsPageId.MusicBox, (services, window) =>
    {
        var page = (MusicBoxPage)window.GetPage(SettingsPageId.MusicBox);
        string duplicate = services.Songs.Songs.First(s => s.Origin == MediaOrigin.Bundled).FilePath;

        page.HandleRequest(new OpenFilesRequest([duplicate]));
        Assert.True(page.ImportBar.IsOpen);
        Assert.Equal($"{Path.GetFileNameWithoutExtension(duplicate)} is already in your Music Box.", page.ImportBar.Message);

        // The progress timer refreshes the engine problems' bar twice a second; the import sentence stays.
        UiHarness.Pump(TimeSpan.FromMilliseconds(1300));
        Assert.True(page.ImportBar.IsOpen);
        Assert.False(page.ProblemBar.IsOpen);
    });

    [Fact]
    public void ResumeStaysAvailableWhenThePausedSongWentAway() => PagesAHarness.WithWindow(ui, SettingsPageId.MusicBox, (services, window) =>
    {
        var page = (MusicBoxPage)window.GetPage(SettingsPageId.MusicBox);
        services.MusicFake.SetState(new MusicState { Status = MusicStatus.Paused, CurrentSong = null });
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        Assert.Equal("_Resume", page.PauseButton.Content);
        Assert.True(page.PauseButton.IsEnabled);

        page.PauseButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal("Resume", services.MusicFake.Commands[^1]);
    });

    [Fact]
    public void AccessKeysStayUniqueWhileTheMusicIsPaused() => PagesAHarness.WithWindow(ui, SettingsPageId.MusicBox, (services, window) =>
    {
        var page = (MusicBoxPage)window.GetPage(SettingsPageId.MusicBox);
        services.MusicFake.SetState(new MusicState { Status = MusicStatus.Paused, CurrentSong = services.Songs.Songs[0] });
        foreach (Expander expander in UiHarness.Descendants<Expander>(page))
        {
            expander.IsExpanded = true;
        }

        UiHarness.Pump(TimeSpan.FromMilliseconds(200));
        Assert.True(page.PreviousButton.IsEnabled);
        Assert.True(page.PauseButton.IsEnabled);
        Assert.Empty(PagesAHarness.DuplicateAccessKeys(window));
        Assert.Contains(('R', "_Resume"), PagesAHarness.VisibleAccessKeys(window));
    });

    [Fact]
    public void TheProgressBarFollowsTheSongWhileItHasKeyboardFocus() => PagesAHarness.WithWindow(ui, SettingsPageId.MusicBox, (services, window) =>
    {
        var page = (MusicBoxPage)window.GetPage(SettingsPageId.MusicBox);
        SongInfo song = services.Songs.Songs[0] with { Length = TimeSpan.FromMinutes(3) };
        void At(int seconds) => services.MusicFake.SetState(new MusicState
        {
            Status = MusicStatus.Playing,
            CurrentSong = song,
            Position = TimeSpan.FromSeconds(seconds),
            CanSeek = true,
        });

        At(10);
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        Assert.Equal(10, page.ProgressSlider.Value);

        // A click on the bar (to seek) leaves the keyboard focus there, as tabbing to it does.
        Assert.True(page.ProgressSlider.Focus());
        Assert.True(page.ProgressSlider.IsKeyboardFocusWithin);
        At(70);
        UiHarness.Pump(TimeSpan.FromMilliseconds(100));
        Assert.Equal(70, page.ProgressSlider.Value);
    });
}
