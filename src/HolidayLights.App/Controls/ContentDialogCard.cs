using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace HolidayLights.App.Controls;

/// <summary>
/// The card of a <see cref="ContentDialogs"/> confirmation: title, text and two buttons, the primary (possibly
/// destructive) one on the left. Enter chooses the primary button unless it is destructive; Esc chooses the close button.
/// Default style in <c>Themes/Generic.xaml</c> (parts <c>PART_PrimaryButton</c>, <c>PART_CloseButton</c>).
/// </summary>
[TemplatePart(Name = PrimaryButtonPart, Type = typeof(Button))]
[TemplatePart(Name = CloseButtonPart, Type = typeof(Button))]
public class ContentDialogCard : Control
{
    /// <summary>Identifies <see cref="Title"/>.</summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ContentDialogCard), new PropertyMetadata(""));

    /// <summary>Identifies <see cref="Text"/>.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ContentDialogCard), new PropertyMetadata(""));

    /// <summary>Identifies <see cref="PrimaryButtonText"/>.</summary>
    public static readonly DependencyProperty PrimaryButtonTextProperty =
        DependencyProperty.Register(nameof(PrimaryButtonText), typeof(string), typeof(ContentDialogCard), new PropertyMetadata("OK"));

    /// <summary>Identifies <see cref="CloseButtonText"/>.</summary>
    public static readonly DependencyProperty CloseButtonTextProperty =
        DependencyProperty.Register(nameof(CloseButtonText), typeof(string), typeof(ContentDialogCard), new PropertyMetadata("Cancel"));

    /// <summary>Identifies <see cref="PrimaryIsDestructive"/>.</summary>
    public static readonly DependencyProperty PrimaryIsDestructiveProperty =
        DependencyProperty.Register(nameof(PrimaryIsDestructive), typeof(bool), typeof(ContentDialogCard), new PropertyMetadata(false));

    private const string PrimaryButtonPart = "PART_PrimaryButton";
    private const string CloseButtonPart = "PART_CloseButton";

    private Button? primaryButton;
    private Button? closeButton;
    private bool completed;

    static ContentDialogCard() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ContentDialogCard), new FrameworkPropertyMetadata(typeof(ContentDialogCard)));

    /// <summary>Creates the card.</summary>
    public ContentDialogCard() => Loaded += (_, _) => (PrimaryIsDestructive ? closeButton : primaryButton)?.Focus();

    /// <summary>Raised once with true (primary button) or false (close button, Esc).</summary>
    public event EventHandler<bool>? Completed;

    /// <summary>The title, e.g. "Reset Holiday Lights?".</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The primary (left) button.</summary>
    public string PrimaryButtonText
    {
        get => (string)GetValue(PrimaryButtonTextProperty);
        set => SetValue(PrimaryButtonTextProperty, value);
    }

    /// <summary>The close (right) button.</summary>
    public string CloseButtonText
    {
        get => (string)GetValue(CloseButtonTextProperty);
        set => SetValue(CloseButtonTextProperty, value);
    }

    /// <summary>True when the primary button destroys something: it is never the default.</summary>
    public bool PrimaryIsDestructive
    {
        get => (bool)GetValue(PrimaryIsDestructiveProperty);
        set => SetValue(PrimaryIsDestructiveProperty, value);
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        if (primaryButton is not null)
        {
            primaryButton.Click -= OnPrimaryClick;
        }

        if (closeButton is not null)
        {
            closeButton.Click -= OnCloseClick;
        }

        base.OnApplyTemplate();
        primaryButton = GetTemplateChild(PrimaryButtonPart) as Button;
        closeButton = GetTemplateChild(CloseButtonPart) as Button;
        if (primaryButton is not null)
        {
            primaryButton.Click += OnPrimaryClick;
        }

        if (closeButton is not null)
        {
            closeButton.Click += OnCloseClick;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Complete(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !PrimaryIsDestructive && Keyboard.FocusedElement is not Button)
        {
            Complete(true);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new ContentDialogCardAutomationPeer(this);

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => Complete(true);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Complete(false);

    private void Complete(bool result)
    {
        if (completed)
        {
            return;
        }

        completed = true;
        Completed?.Invoke(this, result);
    }

    private sealed class ContentDialogCardAutomationPeer(ContentDialogCard owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Window;

        protected override string GetClassNameCore() => "ContentDialog";

        protected override string GetNameCore() => ((ContentDialogCard)Owner).Title;

        protected override string GetHelpTextCore() => ((ContentDialogCard)Owner).Text;

        protected override bool IsDialogCore() => true;
    }
}
