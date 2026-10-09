using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HolidayLights.App.Settings;
using HolidayLights.App.Settings.Undo;

namespace HolidayLights.App.Controls;

/// <summary>
/// A snackbar (PRODUCT-SPEC 3.0.1, 3.12): bottom centre of the page content, 48 DIP high, at most 560 DIP wide, on an
/// inverted surface, with one sentence, at most one action ("Undo", "Show") and a close button. It stays 6 s, paused while
/// hovered or focused; a new one replaces the old one; it is a polite live region. Motion: slide 12 DIP and fade over
/// 200 ms (a 150 ms fade when Windows animation effects are off). Default style in <c>Themes/Generic.xaml</c>.
/// </summary>
[TemplatePart(Name = ActionButtonPart, Type = typeof(ButtonBase))]
[TemplatePart(Name = CloseButtonPart, Type = typeof(ButtonBase))]
public class Snackbar : Control
{
    /// <summary>How long a snackbar stays when nobody points at it.</summary>
    public static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(6);

    /// <summary>Identifies <see cref="Message"/>.</summary>
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(Snackbar), new PropertyMetadata(""));

    /// <summary>Identifies <see cref="ActionText"/>.</summary>
    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(Snackbar), new PropertyMetadata(null));

    private const string ActionButtonPart = "PART_ActionButton";
    private const string CloseButtonPart = "PART_CloseButton";
    private const double SlideDistance = 12;

    private readonly DispatcherTimer hideTimer;
    private readonly TranslateTransform slide = new();
    private ButtonBase? actionButton;
    private ButtonBase? closeButton;
    private Action? action;
    private UndoHistory? watchedHistory;
    private bool historyArmed;
    private int watchSession;

    static Snackbar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Snackbar), new FrameworkPropertyMetadata(typeof(Snackbar)));
        AutomationProperties.LiveSettingProperty.OverrideMetadata(typeof(Snackbar), new FrameworkPropertyMetadata(AutomationLiveSetting.Polite));
        VisibilityProperty.OverrideMetadata(typeof(Snackbar), new FrameworkPropertyMetadata(Visibility.Collapsed));
    }

    /// <summary>Creates the snackbar (hidden).</summary>
    public Snackbar()
    {
        RenderTransform = slide;
        hideTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = VisibleDuration };
        hideTimer.Tick += (_, _) => Dismiss();
        Unloaded += (_, _) => hideTimer.Stop();
    }

    /// <summary>The sentence.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>The action ("Undo", "Show"), or null for none.</summary>
    public string? ActionText
    {
        get => (string?)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>True while a snackbar shows.</summary>
    public bool IsShown { get; private set; }

    /// <summary>
    /// Shows a snackbar, replacing the one that shows. An "Undo" action reverts the step the sentence describes: once
    /// another step is recorded (or a step is undone) in the window's history while the snackbar shows, its Undo would
    /// revert something else, so the action goes away and only the sentence stays.
    /// </summary>
    /// <param name="message">The sentence, e.g. "Using Candy Canes on the whole frame.".</param>
    /// <param name="actionText">"Undo" or "Show", or null.</param>
    /// <param name="onAction">What the action does.</param>
    public void Show(string message, string? actionText = null, Action? onAction = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        Message = message;
        ActionText = onAction is null ? null : actionText;
        action = onAction;
        IsShown = true;
        Visibility = Visibility.Visible;
        AnimateIn();
        RestartTimer();
        WatchHistory(onAction is not null && IsUndo(actionText));
        Announcer.RaiseLiveRegionChanged(this);
    }

    /// <summary>Hides the snackbar (close button, timeout, the action).</summary>
    public void Dismiss()
    {
        hideTimer.Stop();
        StopWatchingHistory();
        if (!IsShown)
        {
            return;
        }

        IsShown = false;
        action = null;
        bool keyboardWasInside = IsKeyboardFocusWithin;
        AnimateOut();
        if (keyboardWasInside)
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
        }
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        if (actionButton is not null)
        {
            actionButton.Click -= OnActionClick;
        }

        if (closeButton is not null)
        {
            closeButton.Click -= OnCloseClick;
        }

        base.OnApplyTemplate();
        actionButton = GetTemplateChild(ActionButtonPart) as ButtonBase;
        closeButton = GetTemplateChild(CloseButtonPart) as ButtonBase;
        if (actionButton is not null)
        {
            actionButton.Click += OnActionClick;
        }

        if (closeButton is not null)
        {
            closeButton.Click += OnCloseClick;
        }
    }

    /// <inheritdoc />
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        hideTimer.Stop();
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        RestartTimer();
    }

    /// <inheritdoc />
    protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsKeyboardFocusWithinChanged(e);
        if ((bool)e.NewValue)
        {
            hideTimer.Stop();
        }
        else
        {
            RestartTimer();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsShown)
        {
            Dismiss();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new SnackbarAutomationPeer(this);

    private static bool IsUndo(string? actionText) =>
        string.Equals(actionText?.Replace("_", "", StringComparison.Ordinal), "Undo", StringComparison.OrdinalIgnoreCase);

    private void RestartTimer()
    {
        hideTimer.Stop();
        if (IsShown && !IsMouseOver && !IsKeyboardFocusWithin)
        {
            hideTimer.Start();
        }
    }

    /// <summary>Follows the window's undo history while an "Undo" snackbar shows (from the end of the current operation, which records the step).</summary>
    private void WatchHistory(bool watch)
    {
        StopWatchingHistory();
        if (!watch || Window.GetWindow(this) is not ISettingsHost host)
        {
            return;
        }

        watchedHistory = host.History;
        watchedHistory.Changed += OnHistoryChanged;
        int session = watchSession;
        Dispatcher.InvokeAsync(
            () =>
            {
                if (session == watchSession)
                {
                    historyArmed = true;
                }
            },
            DispatcherPriority.Background);
    }

    private void StopWatchingHistory()
    {
        if (watchedHistory is not null)
        {
            watchedHistory.Changed -= OnHistoryChanged;
            watchedHistory = null;
        }

        historyArmed = false;
        watchSession++;
    }

    /// <summary>A newer step, or an undo: "Undo" would no longer revert what the sentence says, so the action goes away.</summary>
    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (!historyArmed || !IsShown)
        {
            return;
        }

        StopWatchingHistory();
        action = null;
        ActionText = null;
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        Action? chosen = action;
        Dismiss();
        chosen?.Invoke();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Dismiss();

    private void AnimateIn()
    {
        BeginAnimation(OpacityProperty, null);
        slide.BeginAnimation(TranslateTransform.YProperty, null);
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
            return;
        }

        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(SlideDistance, 0, duration) { EasingFunction = ease });
    }

    private void AnimateOut()
    {
        TimeSpan duration = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 200 : 150);
        var fade = new DoubleAnimation(0, duration);
        fade.Completed += (_, _) =>
        {
            if (!IsShown)
            {
                Visibility = Visibility.Collapsed;
            }
        };
        BeginAnimation(OpacityProperty, fade);
        if (SystemParameters.ClientAreaAnimation)
        {
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(SlideDistance, duration));
        }
    }

    private sealed class SnackbarAutomationPeer(Snackbar owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.StatusBar;

        protected override string GetClassNameCore() => nameof(Snackbar);

        protected override string GetNameCore() => ((Snackbar)Owner).Message;
    }
}
