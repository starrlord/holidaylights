using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace HolidayLights.App.Controls;

/// <summary>
/// A Fluent card (owner: settings-ui; PRODUCT-SPEC 3.0.1): <c>CardBackgroundFillColorDefault</c>, 8 DIP radius, 16 DIP
/// padding, a Title Case header (BodyStrong 14) and an optional one-line description. The 5.4 group boxes become cards.
/// Default style in <c>Themes/Generic.xaml</c>.
/// </summary>
/// <remarks>Screen readers see a group named after the header, with the description as its help text.</remarks>
public class Card : HeaderedContentControl
{
    /// <summary>Identifies <see cref="Description"/>.</summary>
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(Card), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="Actions"/>.</summary>
    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(Card), new PropertyMetadata(null));

    static Card() => DefaultStyleKeyProperty.OverrideMetadata(typeof(Card), new FrameworkPropertyMetadata(typeof(Card)));

    /// <summary>The description under the header (secondary text), or null.</summary>
    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Commands shown at the right end of the header row ("Edit Holidays...", "Music Box..."), or null.</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new CardAutomationPeer(this);

    private sealed class CardAutomationPeer(Card owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(Card);

        protected override string GetNameCore()
        {
            string name = base.GetNameCore();
            return string.IsNullOrEmpty(name) && Owner is Card { Header: string header } ? header : name;
        }

        protected override string GetHelpTextCore()
        {
            string help = base.GetHelpTextCore();
            return string.IsNullOrEmpty(help) && Owner is Card { Description: { } description } ? description : help;
        }
    }
}
