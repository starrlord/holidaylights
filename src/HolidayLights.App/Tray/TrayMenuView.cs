using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HolidayLights.App.Preview;
using HolidayLights.App.Shell;

namespace HolidayLights.App.Tray;

/// <summary>
/// Turns the <see cref="TrayMenuModel"/> into a Fluent WPF <see cref="ContextMenu"/> (PRODUCT-SPEC 2.2): the header strip
/// (the live top edge, 120 x 20 DIP, with the theme beside it; not focusable), check and radio items with their hot keys,
/// the Themes submenu and the bold default item. UI thread.
/// </summary>
internal static class TrayMenuView
{
    /// <summary>The key of the radio items' template in <see cref="StylesUri"/>.</summary>
    internal const string RadioTemplateKey = "HL.Tray.RadioMenuItemTemplate";

    /// <summary>
    /// The width of the Fluent submenu item's check column (a 20 DIP box and its 6 DIP margin). The Fluent submenu header
    /// has no such column, so the Themes item moves its label by this much to line up with the other labels.
    /// </summary>
    internal const double CheckColumnWidth = 26;

    /// <summary>The tray menu's own looks (the radio items' template).</summary>
    private static readonly Uri StylesUri = AppAssetUris.Resolve("pack://application:,,,/Tray/TrayMenuStyles.xaml");

    /// <summary>Fills a menu with the items of a model.</summary>
    /// <param name="menu">The menu (its items are replaced).</param>
    /// <param name="items">The model items.</param>
    /// <param name="header">The header lines.</param>
    /// <param name="services">The services of the header strip.</param>
    /// <param name="animated">Animate the strip (frame 0 under reduced motion).</param>
    /// <param name="execute">Performs a command (with the theme of a theme item).</param>
    public static void Fill(ContextMenu menu, IReadOnlyList<TrayMenuItem> items, TrayHeader header, IAppServices services, bool animated, Action<TrayCommand, string?> execute)
    {
        if (menu.TryFindResource(RadioTemplateKey) is null)
        {
            menu.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = StylesUri });
        }

        menu.Items.Clear();
        foreach (TrayMenuItem item in items)
        {
            menu.Items.Add(Create(item, header, services, animated, execute));
        }
    }

    private static object Create(TrayMenuItem item, TrayHeader header, IAppServices services, bool animated, Action<TrayCommand, string?> execute)
    {
        switch (item.Kind)
        {
            case TrayMenuItemKind.Separator:
                return new Separator();
            case TrayMenuItemKind.Header:
                return CreateHeader(header, services, animated);
            case TrayMenuItemKind.Submenu:
                // The Fluent submenu header has no check column: its label would start left of the others.
                var submenu = new MenuItem { Header = item.Text, Padding = new Thickness(CheckColumnWidth, 0, 0, 0) };
                foreach (TrayMenuItem child in item.Children ?? [])
                {
                    submenu.Items.Add(Create(child, header, services, animated, execute));
                }

                return submenu;
            default:
                var menuItem = new MenuItem
                {
                    Header = item.Text,
                    IsCheckable = item.Kind is TrayMenuItemKind.Check or TrayMenuItemKind.Radio,
                    IsChecked = item.IsChecked,
                    IsEnabled = item.IsEnabled,
                    InputGestureText = item.Gesture ?? "",
                };
                if (item.Kind == TrayMenuItemKind.Radio)
                {
                    // A radio mark instead of the Fluent check box (still checkable, so screen readers hear the state).
                    menuItem.SetResourceReference(Control.TemplateProperty, RadioTemplateKey);
                }

                if (item.IsDefault)
                {
                    menuItem.FontWeight = FontWeights.Bold;
                }

                TrayCommand command = item.Command!.Value;
                string? theme = item.ThemeName;
                menuItem.Click += (_, _) => execute(command, theme);
                return menuItem;
        }
    }

    /// <summary>The header: the live strip of the active top edge and the theme lines; read by screen readers, never focused.</summary>
    private static MenuItem CreateHeader(TrayHeader header, IAppServices services, bool animated)
    {
        var strip = new LightStrip
        {
            Services = services,
            Side = Side.Top,
            IncludeCorners = true,
            BulbHeight = 20,
            Width = 120,
            Height = 20,
            IsAnimated = animated,
            Glow = StripGlow.Auto,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var lines = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(new TextBlock { Text = header.ThemeLine, FontWeight = FontWeights.SemiBold });
        if (header.SourceLine.Length > 0)
        {
            var source = new TextBlock { Text = header.SourceLine, FontSize = 12 };
            source.SetResourceReference(TextBlock.StyleProperty, AppResourceKeys.SecondaryTextStyle);
            lines.Children.Add(source);
        }

        var content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        content.Children.Add(strip);
        content.Children.Add(lines);

        var item = new MenuItem
        {
            Header = content,
            Focusable = false,
            IsHitTestVisible = false,
            StaysOpenOnClick = true,
        };
        AutomationProperties.SetName(item, header.SourceLine.Length > 0 ? $"{header.ThemeLine}, {header.SourceLine}" : header.ThemeLine);
        return item;
    }
}
