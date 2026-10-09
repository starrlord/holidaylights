using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HolidayLights.App.Tray;
using HolidayLights.Tests.App.Fakes;
using HolidayLights.Tests.Shared;
using HolidayLights.Tests.Ui.Fakes;

namespace HolidayLights.Tests.App;

/// <summary>
/// The tray menu as WPF builds it with the Fluent theme (PRODUCT-SPEC 2.2), laid out without opening its popup: radio
/// items show a radio mark (r1 review: they showed check marks), check items keep the check box, and every label,
/// the Themes submenu's included, starts in the same column.
/// </summary>
public sealed class TrayMenuViewTests
{
    private static ContextMenu Menu(bool dark)
    {
        var state = new TrayMenuState(new AppSettings(), "Halloween", "Halloween", ["Christmas 1", "Halloween"], false, null, "Ctrl+Alt+Shift+B");
        var menu = new ContextMenu();
        var window = new Window();
        UiHarness.ApplyAppResources(window, dark);
        foreach (ResourceDictionary dictionary in window.Resources.MergedDictionaries)
        {
            menu.Resources.MergedDictionaries.Add(dictionary);
        }

        // The header strip needs the lights; it is not part of the label column.
        TrayMenuView.Fill(menu, [.. TrayMenuModel.Build(state).Where(i => i.Kind != TrayMenuItemKind.Header)], TrayMenuModel.Header(state),
            new FakeServices(), animated: false, (_, _) => { });
        menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();
        return menu;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject descendant in Visuals(child))
            {
                yield return descendant;
            }
        }
    }

    private static double LabelLeft(MenuItem item) =>
        Visuals(item).OfType<ContentPresenter>().First(p => ReferenceEquals(p.Content, item.Header)).TranslatePoint(default, item).X;

    private static FrameworkElement? Part(MenuItem item, string name) => item.Template?.FindName(name, item) as FrameworkElement;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RadioItemsShowARadioMarkAndEveryLabelLinesUp(bool dark) => StaThread.Run(() =>
    {
        ContextMenu menu = Menu(dark);
        MenuItem[] items = [.. menu.Items.OfType<MenuItem>()];
        MenuItem Item(string header) => items.Single(i => i.Header as string == header);

        MenuItem desktop = Item("Bulbs On _Desktop");
        MenuItem onTop = Item("Bulbs On _Top of All Windows");
        Assert.Equal(Visibility.Visible, Part(desktop, "RadioDot")?.Visibility);
        Assert.Equal(Visibility.Collapsed, Part(onTop, "RadioDot")?.Visibility);
        Assert.Null(Part(desktop, "CheckBoxIcon"));
        Assert.True(desktop.IsCheckable, "Screen readers still hear the choice.");

        MenuItem showLights = Item("Show _Lights");
        Assert.Null(Part(showLights, "RadioDot"));
        Assert.Equal(Visibility.Visible, Part(showLights, "CheckBoxIconBorder")?.Visibility);

        double column = LabelLeft(showLights);
        Assert.All(items, item => Assert.Equal(column, LabelLeft(item), 1));

        MenuItem themes = Item("Th_emes");
        Assert.Equal(MenuItemRole.SubmenuHeader, themes.Role);
        foreach (MenuItem theme in themes.Items.OfType<MenuItem>().Where(t => t.Header is "Christmas 1" or "Halloween"))
        {
            theme.ApplyTemplate();
            Assert.NotNull(Part(theme, "RadioDot"));
        }
    });
}
