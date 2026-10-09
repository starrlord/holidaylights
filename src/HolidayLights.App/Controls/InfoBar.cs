using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace HolidayLights.App.Controls;

/// <summary>Severity of an <see cref="InfoBar"/> (Fluent glyph and colours).</summary>
public enum InfoBarSeverity
{
    /// <summary>Informational.</summary>
    Informational,

    /// <summary>Success.</summary>
    Success,

    /// <summary>Warning.</summary>
    Warning,

    /// <summary>Error.</summary>
    Error,
}

/// <summary>
/// An InfoBar (owner: settings-ui; PRODUCT-SPEC 3.0.1): severity glyph, one sentence, at most one action button or link,
/// optional close button; placed at the top of the page or card it concerns; announced as a polite live region.
/// Default style in <c>Themes/Generic.xaml</c>.
/// </summary>
/// <remarks>
/// A few InfoBars of the spec offer a second action ("[Undo] [Turn Off Automatic Themes]"); <see cref="SecondaryActionText"/>
/// provides it. The bar collapses while <see cref="IsOpen"/> is false.
/// </remarks>
public class InfoBar : Control
{
    /// <summary>Identifies <see cref="Severity"/>.</summary>
    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar), new PropertyMetadata(InfoBarSeverity.Informational));

    /// <summary>Identifies <see cref="Message"/>.</summary>
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(InfoBar), new PropertyMetadata("", OnAnnouncedPropertyChanged));

    /// <summary>Identifies <see cref="ActionText"/>.</summary>
    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(InfoBar), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="ActionCommand"/>.</summary>
    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(InfoBar), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="SecondaryActionText"/>.</summary>
    public static readonly DependencyProperty SecondaryActionTextProperty =
        DependencyProperty.Register(nameof(SecondaryActionText), typeof(string), typeof(InfoBar), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="SecondaryActionCommand"/>.</summary>
    public static readonly DependencyProperty SecondaryActionCommandProperty =
        DependencyProperty.Register(nameof(SecondaryActionCommand), typeof(ICommand), typeof(InfoBar), new PropertyMetadata(null));

    /// <summary>Identifies <see cref="IsClosable"/>.</summary>
    public static readonly DependencyProperty IsClosableProperty =
        DependencyProperty.Register(nameof(IsClosable), typeof(bool), typeof(InfoBar), new PropertyMetadata(true));

    /// <summary>Identifies <see cref="IsOpen"/>.</summary>
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(InfoBar), new PropertyMetadata(true, OnAnnouncedPropertyChanged));

    private static readonly DependencyPropertyKey IsCompactPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsCompact), typeof(bool), typeof(InfoBar), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="IsCompact"/>.</summary>
    public static readonly DependencyProperty IsCompactProperty = IsCompactPropertyKey.DependencyProperty;

    /// <summary>The width below which the action buttons move under the message.</summary>
    public const double CompactWidth = 480;

    /// <summary>Identifies the <see cref="Closed"/> routed event.</summary>
    public static readonly RoutedEvent ClosedEvent =
        EventManager.RegisterRoutedEvent(nameof(Closed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(InfoBar));

    /// <summary>The command of the close button (handled by the InfoBar itself).</summary>
    public static readonly RoutedCommand CloseCommand = new(nameof(CloseCommand), typeof(InfoBar));

    static InfoBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(InfoBar), new FrameworkPropertyMetadata(typeof(InfoBar)));
        FocusableProperty.OverrideMetadata(typeof(InfoBar), new FrameworkPropertyMetadata(false));
        AutomationProperties.LiveSettingProperty.OverrideMetadata(typeof(InfoBar), new FrameworkPropertyMetadata(AutomationLiveSetting.Polite));
    }

    /// <summary>Creates the InfoBar.</summary>
    public InfoBar() => CommandBindings.Add(new CommandBinding(CloseCommand, (_, e) =>
    {
        IsOpen = false;
        RaiseEvent(new RoutedEventArgs(ClosedEvent, this));
        e.Handled = true;
    }));

    /// <summary>True when the bar is narrower than <see cref="CompactWidth"/>: the actions sit under the message.</summary>
    public bool IsCompact => (bool)GetValue(IsCompactProperty);

    /// <summary>Raised when the user closed the bar with its close button.</summary>
    public event RoutedEventHandler Closed
    {
        add => AddHandler(ClosedEvent, value);
        remove => RemoveHandler(ClosedEvent, value);
    }

    /// <summary>The severity.</summary>
    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    /// <summary>The one-sentence message.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Text of the action button ("Undo", "Try Again"), or null for none.</summary>
    public string? ActionText
    {
        get => (string?)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>The action.</summary>
    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    /// <summary>Text of a second action button ("Turn Off Automatic Themes"), or null for none.</summary>
    public string? SecondaryActionText
    {
        get => (string?)GetValue(SecondaryActionTextProperty);
        set => SetValue(SecondaryActionTextProperty, value);
    }

    /// <summary>The second action.</summary>
    public ICommand? SecondaryActionCommand
    {
        get => (ICommand?)GetValue(SecondaryActionCommandProperty);
        set => SetValue(SecondaryActionCommandProperty, value);
    }

    /// <summary>Shows the close button.</summary>
    public bool IsClosable
    {
        get => (bool)GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    /// <summary>Visible; the close button sets it to false.</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new InfoBarAutomationPeer(this);

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.WidthChanged)
        {
            SetValue(IsCompactPropertyKey, sizeInfo.NewSize.Width < CompactWidth);
        }
    }

    private static void OnAnnouncedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InfoBar { IsOpen: true, IsLoaded: true } bar && !string.IsNullOrEmpty(bar.Message))
        {
            Announcer.RaiseLiveRegionChanged(bar);
        }
    }

    private sealed class InfoBarAutomationPeer(InfoBar owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.StatusBar;

        protected override string GetClassNameCore() => nameof(InfoBar);

        protected override string GetNameCore()
        {
            string name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? ((InfoBar)Owner).Message : name;
        }

        protected override string GetLocalizedControlTypeCore() => ((InfoBar)Owner).Severity switch
        {
            InfoBarSeverity.Warning => "warning",
            InfoBarSeverity.Error => "error",
            InfoBarSeverity.Success => "success",
            _ => "information",
        };
    }
}
