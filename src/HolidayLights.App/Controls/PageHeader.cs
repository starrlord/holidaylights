using System.Windows;
using System.Windows.Controls;

namespace HolidayLights.App.Controls;

/// <summary>
/// The page header of the Settings window (PRODUCT-SPEC 2.3, 3.0.1): the title (Title 28, announced as a level-1
/// heading), one line of description in secondary text, the page's own actions and, at the right end, the window's
/// Undo split button and Redo. Default style in <c>Themes/Generic.xaml</c>.
/// </summary>
public class PageHeader : Control
{
    /// <summary>Identifies <see cref="Title"/>.</summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(""));

    /// <summary>Identifies <see cref="Description"/>.</summary>
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(PageHeader), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="Actions"/>.</summary>
    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="Commands"/>.</summary>
    public static readonly DependencyProperty CommandsProperty =
        DependencyProperty.Register(nameof(Commands), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    static PageHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PageHeader), new FrameworkPropertyMetadata(typeof(PageHeader)));
        FocusableProperty.OverrideMetadata(typeof(PageHeader), new FrameworkPropertyMetadata(false));
    }

    /// <summary>The page title ("Bulb Factory", or Home's greeting "Happy Halloween!").</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The one-line description under the title.</summary>
    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>The page's own actions ("Peek", "Clear All Bulbs", "Save Settings As Theme...").</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>The window's Undo split button and Redo button.</summary>
    public object? Commands
    {
        get => GetValue(CommandsProperty);
        set => SetValue(CommandsProperty, value);
    }
}
