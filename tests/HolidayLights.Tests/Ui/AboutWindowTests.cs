using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using HolidayLights.App.About;
using HolidayLights.App.Help;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui;

/// <summary>
/// About Holiday Lights shown off-screen: the creator line with its link to the project's home page (mouse and keyboard),
/// the licence line, and the window in the light, dark and High Contrast themes at 150 %. With <c>HL_UI_SNAPSHOTS</c> it
/// writes PNG files; without it, it renders the light theme as a smoke test.
/// </summary>
[Collection(nameof(WpfCollection))]
public sealed class AboutWindowTests(UiThread ui)
{
    [Fact]
    public void TheCreatorLineLinksToTheProjectHomePage() => ui.Run(() =>
    {
        using var services = new UiTestServices();
        AboutWindow window = Show(services, dark: false, highContrast: false);
        try
        {
            Assert.Equal("Holiday Lights 6 — the modern edition — was created by StarrLord.", window.CreatorLine.Text);
            string project = new TextRange(window.ProjectLine.ContentStart, window.ProjectLine.ContentEnd).Text;
            Assert.Equal("Visit the project at github.com/starrlord/holidaylights.", project);
            Assert.Equal("https://github.com/starrlord/holidaylights", window.ProjectLink.ToolTip);
            Assert.Equal("github.com/starrlord/holidaylights (opens in your web browser)", AutomationProperties.GetName(window.ProjectLink));
            Assert.Equal("Open source under the MIT License.", window.LicenseLine.Text);

            window.ProjectLink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, window.ProjectLink));
            Assert.Equal(["OpenProjectHomePage"], services.ShellFake.Calls);

            // Keyboard: the link takes focus with Tab and follows with Enter.
            Assert.True(window.ProjectLink.Focusable);
            Assert.True(window.ProjectLink.Focus());
            var source = PresentationSource.FromVisual(window)!;
            window.ProjectLink.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            UiHarness.Pump(TimeSpan.FromMilliseconds(30));
            Assert.Equal(["OpenProjectHomePage", "OpenProjectHomePage"], services.ShellFake.Calls);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void AboutRendersInLightDarkAndHighContrast()
    {
        string? folder = UiHarness.SnapshotFolder;
        ui.Run(() =>
        {
            using var services = new UiTestServices();
            (string Name, bool Dark, bool HighContrast)[] looks = folder is null
                ? [("light", false, false)]
                : [("light", false, false), ("dark", true, false), ("highcontrast", true, true)];
            foreach ((string name, bool dark, bool highContrast) in looks)
            {
                AboutWindow window = Show(services, dark, highContrast);
                try
                {
                    BitmapSource shot = UiHarness.Render((FrameworkElement)window.Content, dark);
                    Assert.True(shot.PixelWidth > 600, name);
                    if (folder is not null)
                    {
                        UiHarness.SavePng(shot, Path.Combine(folder, $"About-{name}.png"));
                    }
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    private static AboutWindow Show(UiTestServices services, bool dark, bool highContrast)
    {
        var window = new AboutWindow(services, HelpContentStore.LoadCredits());
        UiHarness.ApplyAppResources(window, dark, highContrast);
        UiHarness.PlaceOffScreen(window, window.Width, window.Height);
        window.Show();
        UiHarness.Pump(TimeSpan.FromMilliseconds(UiHarness.SnapshotFolder is null ? 100 : 700));
        return window;
    }
}
