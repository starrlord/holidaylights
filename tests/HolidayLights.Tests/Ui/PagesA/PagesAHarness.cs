using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using HolidayLights.App.Settings;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.Ui.PagesA;

/// <summary>
/// Helpers of the Music Box, Screen Saver and General page tests: a real Settings window off-screen, the ways a control
/// can be used (UI Automation's Toggle and Select, a mouse click, a double-click on a row) and the visible access keys.
/// </summary>
internal static class PagesAHarness
{
    private static readonly MethodInfo OnClickMethod =
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>Runs a test on a real Settings window showing a page (off-screen, closed at once).</summary>
    /// <param name="ui">The UI thread.</param>
    /// <param name="page">The page to show.</param>
    /// <param name="test">The test.</param>
    /// <param name="initial">The initial settings, or null for the defaults.</param>
    public static void WithWindow(UiThread ui, SettingsPageId page, Action<UiTestServices, SettingsWindow> test, AppSettings? initial = null) => ui.Run(() =>
    {
        using var services = new UiTestServices(initial);
        services.IndexTask.Wait(TimeSpan.FromSeconds(120));
        var window = new SettingsWindow(services);
        UiHarness.ApplyAppResources(window, dark: false);
        UiHarness.PlaceOffScreen(window, 1240, 800);
        window.Show();
        try
        {
            window.ShowPage(page);
            UiHarness.Pump(TimeSpan.FromMilliseconds(250));
            test(services, window);
        }
        finally
        {
            window.Close();
        }
    }, TimeSpan.FromMinutes(3));

    /// <summary>UI Automation's Toggle, as Narrator's primary action and Voice Access use it (no Click is raised).</summary>
    /// <param name="element">A check box or toggle button.</param>
    public static void Toggle(UIElement element)
    {
        ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(element).GetPattern(PatternInterface.Toggle)).Toggle();
        UiHarness.Pump(TimeSpan.FromMilliseconds(30));
    }

    /// <summary>UI Automation's Select of a radio button (no Click is raised).</summary>
    /// <param name="element">The radio button.</param>
    public static void Select(UIElement element)
    {
        ((ISelectionItemProvider)UIElementAutomationPeer.CreatePeerForElement(element).GetPattern(PatternInterface.SelectionItem)).Select();
        UiHarness.Pump(TimeSpan.FromMilliseconds(30));
    }

    /// <summary>A mouse click or Space: the control's own OnClick (a toggle control changes, then Click is raised).</summary>
    /// <param name="button">The control.</param>
    public static void Click(ButtonBase button)
    {
        OnClickMethod.Invoke(button, null);
        UiHarness.Pump(TimeSpan.FromMilliseconds(30));
    }

    /// <summary>
    /// The double-click a list row raises for the second press of a double-click, as WPF builds it: the row's
    /// MouseDoubleClick with the pressed element as its original source (also when that element handled the press).
    /// </summary>
    /// <param name="row">The row (a ListBoxItem).</param>
    /// <param name="pressed">The element under the mouse.</param>
    public static void DoubleClick(Control row, DependencyObject pressed)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = pressed };
        row.RaiseEvent(args);
        UiHarness.Pump(TimeSpan.FromMilliseconds(30));
    }

    /// <summary>The access keys of the visible labels of the window (page, header, navigation and bottom bar).</summary>
    /// <param name="window">The window.</param>
    /// <returns>The keys, upper case, with their label.</returns>
    public static IReadOnlyList<(char Key, string Text)> VisibleAccessKeys(SettingsWindow window) =>
    [
        .. UiHarness.Descendants<AccessText>((DependencyObject)window.Content)
            .Where(text => text.IsVisible && text.AccessKey != default)
            .Select(text => (char.ToUpperInvariant(text.AccessKey), text.Text)),
    ];

    /// <summary>The access keys used by more than one visible label, as "R: P_revious | _Resume".</summary>
    /// <param name="window">The window.</param>
    /// <returns>The duplicates.</returns>
    public static string[] DuplicateAccessKeys(SettingsWindow window) =>
    [
        .. VisibleAccessKeys(window).GroupBy(k => k.Key).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join(" | ", g.Select(k => k.Text))}"),
    ];

    /// <summary>Runs the dispatcher until a condition holds (at most a few seconds).</summary>
    /// <param name="condition">The condition.</param>
    /// <returns>True when it held in time.</returns>
    public static bool PumpUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            UiHarness.Pump(TimeSpan.FromMilliseconds(20));
        }

        return true;
    }
}
