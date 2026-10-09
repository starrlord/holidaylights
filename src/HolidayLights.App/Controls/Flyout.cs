using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace HolidayLights.App.Controls;

/// <summary>
/// The surface of a flyout or teaching tip (PRODUCT-SPEC 3.0.1): Fluent flyout background, border, 8 DIP radius and
/// shadow; an optional title and close button make it a teaching tip. Default style in <c>Themes/Generic.xaml</c>.
/// </summary>
public class FlyoutPresenter : ContentControl
{
    /// <summary>Identifies <see cref="Title"/>.</summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(FlyoutPresenter), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="ShowCloseButton"/>.</summary>
    public static readonly DependencyProperty ShowCloseButtonProperty =
        DependencyProperty.Register(nameof(ShowCloseButton), typeof(bool), typeof(FlyoutPresenter), new PropertyMetadata(false));

    /// <summary>The command of the close button: closes the popup that hosts the presenter.</summary>
    public static readonly RoutedCommand CloseCommand = new(nameof(CloseCommand), typeof(FlyoutPresenter));

    static FlyoutPresenter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FlyoutPresenter), new FrameworkPropertyMetadata(typeof(FlyoutPresenter)));
        FocusableProperty.OverrideMetadata(typeof(FlyoutPresenter), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Creates the presenter.</summary>
    public FlyoutPresenter() => CommandBindings.Add(new CommandBinding(CloseCommand, (_, e) =>
    {
        Flyout.Close(this);
        e.Handled = true;
    }));

    /// <summary>The title of a teaching tip, or null for a plain flyout.</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Shows a close button (teaching tips).</summary>
    public bool ShowCloseButton
    {
        get => (bool)GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }
}

/// <summary>
/// Anchored, light-dismiss flyouts (PRODUCT-SPEC 3.0.1): a popup below (or beside) an anchor that closes when the user
/// clicks elsewhere or presses Esc; focus moves into the flyout when it opens and back to the anchor when it closes.
/// </summary>
public static class Flyout
{
    /// <summary>Opens a flyout.</summary>
    /// <param name="anchor">The element it points at.</param>
    /// <param name="content">The flyout content (the "Add To..." miniature frame, a "..." menu of buttons).</param>
    /// <param name="placement">Where it opens relative to the anchor.</param>
    /// <param name="title">A teaching-tip title, or null.</param>
    /// <returns>The popup (already open); <see cref="Close"/> or light dismiss closes it.</returns>
    public static Popup Show(FrameworkElement anchor, FrameworkElement content, PlacementMode placement = PlacementMode.Bottom, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(content);
        var presenter = new FlyoutPresenter { Content = content, Title = title, ShowCloseButton = title is not null };
        var popup = new Popup
        {
            Child = presenter,
            PlacementTarget = anchor,
            Placement = placement,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = SystemParameters.ClientAreaAnimation ? PopupAnimation.Fade : PopupAnimation.None,
            Focusable = false,
        };
        KeyboardNavigation.SetTabNavigation(presenter, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(presenter, KeyboardNavigationMode.Contained);
        presenter.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                popup.IsOpen = false;
                e.Handled = true;
            }
        };
        popup.Opened += (_, _) => presenter.Dispatcher.BeginInvoke(() => presenter.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
        popup.Closed += (_, _) =>
        {
            if (anchor.IsVisible && presenter.IsKeyboardFocusWithin)
            {
                anchor.Focus();
            }
        };
        popup.IsOpen = true;
        return popup;
    }

    /// <summary>Closes the flyout that hosts an element.</summary>
    /// <param name="element">An element inside a flyout.</param>
    public static void Close(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        for (DependencyObject? node = element; node is not null; node = LogicalTreeHelperParent(node))
        {
            if (node is Popup popup)
            {
                popup.IsOpen = false;
                return;
            }
        }
    }

    private static DependencyObject? LogicalTreeHelperParent(DependencyObject node) =>
        LogicalTreeHelper.GetParent(node) ?? (node as FrameworkElement)?.Parent ?? (node as FrameworkElement)?.TemplatedParent;
}
