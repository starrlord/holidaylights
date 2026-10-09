using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace HolidayLights.App.Controls;

/// <summary>
/// A SplitButton (PRODUCT-SPEC 3.0.1): a primary button and a chevron that opens a menu (the page header's Undo with
/// "Undo All Changes Since This Window Opened"). Both halves are separate tab stops with their own accessible names;
/// Alt+Down on the primary half also opens the menu. Default style in <c>Themes/Generic.xaml</c>
/// (parts <c>PART_PrimaryButton</c>, <c>PART_MenuButton</c>).
/// </summary>
[TemplatePart(Name = PrimaryButtonPart, Type = typeof(ButtonBase))]
[TemplatePart(Name = MenuButtonPart, Type = typeof(ButtonBase))]
public class SplitButton : ContentControl
{
    /// <summary>Identifies <see cref="Command"/>.</summary>
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SplitButton), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="Menu"/>.</summary>
    public static readonly DependencyProperty MenuProperty =
        DependencyProperty.Register(nameof(Menu), typeof(ContextMenu), typeof(SplitButton), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="MenuButtonName"/>.</summary>
    public static readonly DependencyProperty MenuButtonNameProperty =
        DependencyProperty.Register(nameof(MenuButtonName), typeof(string), typeof(SplitButton), new PropertyMetadata("More options"));

    private const string PrimaryButtonPart = "PART_PrimaryButton";
    private const string MenuButtonPart = "PART_MenuButton";

    private ButtonBase? menuButton;
    private ButtonBase? primaryButton;

    static SplitButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SplitButton), new FrameworkPropertyMetadata(typeof(SplitButton)));
        FocusableProperty.OverrideMetadata(typeof(SplitButton), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(SplitButton), new FrameworkPropertyMetadata(false));
    }

    /// <summary>The command of the primary half.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>The menu of the chevron half.</summary>
    public ContextMenu? Menu
    {
        get => (ContextMenu?)GetValue(MenuProperty);
        set => SetValue(MenuProperty, value);
    }

    /// <summary>The accessible name of the chevron half.</summary>
    public string MenuButtonName
    {
        get => (string)GetValue(MenuButtonNameProperty);
        set => SetValue(MenuButtonNameProperty, value);
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        if (menuButton is not null)
        {
            menuButton.Click -= OnMenuButtonClick;
        }

        if (primaryButton is not null)
        {
            primaryButton.PreviewKeyDown -= OnPrimaryKeyDown;
        }

        base.OnApplyTemplate();
        menuButton = GetTemplateChild(MenuButtonPart) as ButtonBase;
        primaryButton = GetTemplateChild(PrimaryButtonPart) as ButtonBase;
        if (menuButton is not null)
        {
            menuButton.Click += OnMenuButtonClick;
        }

        if (primaryButton is not null)
        {
            primaryButton.PreviewKeyDown += OnPrimaryKeyDown;
        }
    }

    /// <summary>Opens the menu under the control.</summary>
    public void OpenMenu()
    {
        if (Menu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnMenuButtonClick(object sender, RoutedEventArgs e) => OpenMenu();

    private void OnPrimaryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.SystemKey == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            OpenMenu();
            e.Handled = true;
        }
    }
}
