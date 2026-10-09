using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace HolidayLights.App.Controls;

/// <summary>
/// A NumberBox (PRODUCT-SPEC 3.0.1): a text box with spin buttons and range validation. Up/Down change the value by
/// <see cref="SmallChange"/>, PgUp/PgDn by <see cref="LargeChange"/>; invalid text reverts on Enter or focus loss.
/// Default style in <c>Themes/Generic.xaml</c> (parts <c>PART_TextBox</c>, <c>PART_UpButton</c>, <c>PART_DownButton</c>).
/// </summary>
[TemplatePart(Name = TextBoxPart, Type = typeof(TextBox))]
[TemplatePart(Name = UpButtonPart, Type = typeof(ButtonBase))]
[TemplatePart(Name = DownButtonPart, Type = typeof(ButtonBase))]
public class NumberBox : Control
{
    /// <summary>Identifies <see cref="Value"/>.</summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(int), typeof(NumberBox),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

    /// <summary>Identifies <see cref="Minimum"/>.</summary>
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumberBox), new PropertyMetadata(0, OnRangeChanged));

    /// <summary>Identifies <see cref="Maximum"/>.</summary>
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumberBox), new PropertyMetadata(100, OnRangeChanged));

    /// <summary>Identifies <see cref="SmallChange"/>.</summary>
    public static readonly DependencyProperty SmallChangeProperty =
        DependencyProperty.Register(nameof(SmallChange), typeof(int), typeof(NumberBox), new PropertyMetadata(1));

    /// <summary>Identifies <see cref="LargeChange"/>.</summary>
    public static readonly DependencyProperty LargeChangeProperty =
        DependencyProperty.Register(nameof(LargeChange), typeof(int), typeof(NumberBox), new PropertyMetadata(10));

    /// <summary>Identifies the <see cref="ValueChanged"/> routed event.</summary>
    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<int>), typeof(NumberBox));

    private const string TextBoxPart = "PART_TextBox";
    private const string UpButtonPart = "PART_UpButton";
    private const string DownButtonPart = "PART_DownButton";

    private TextBox? textBox;
    private ButtonBase? upButton;
    private ButtonBase? downButton;

    static NumberBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NumberBox), new FrameworkPropertyMetadata(typeof(NumberBox)));
        FocusableProperty.OverrideMetadata(typeof(NumberBox), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(NumberBox), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Raised when <see cref="Value"/> changed.</summary>
    public event RoutedPropertyChangedEventHandler<int> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    /// <summary>The value (coerced into the range).</summary>
    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The smallest value.</summary>
    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>The largest value.</summary>
    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>The Up/Down step.</summary>
    public int SmallChange
    {
        get => (int)GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>The PgUp/PgDn step.</summary>
    public int LargeChange
    {
        get => (int)GetValue(LargeChangeProperty);
        set => SetValue(LargeChangeProperty, value);
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        Detach();
        base.OnApplyTemplate();
        textBox = GetTemplateChild(TextBoxPart) as TextBox;
        upButton = GetTemplateChild(UpButtonPart) as ButtonBase;
        downButton = GetTemplateChild(DownButtonPart) as ButtonBase;
        if (textBox is not null)
        {
            textBox.PreviewKeyDown += OnTextBoxPreviewKeyDown;
            textBox.LostKeyboardFocus += OnTextBoxLostFocus;
            textBox.Text = Format(Value);
        }

        if (upButton is not null)
        {
            upButton.Click += OnUpClick;
        }

        if (downButton is not null)
        {
            downButton.Click += OnDownClick;
        }
    }

    /// <summary>Adds a step to the value (clamped into the range).</summary>
    /// <param name="delta">The step.</param>
    public void Step(int delta)
    {
        Commit();
        Value = (int)Math.Clamp((long)Value + delta, Minimum, Maximum);
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new NumberBoxAutomationPeer(this);

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var box = (NumberBox)d;
        return Math.Clamp((int)baseValue, box.Minimum, Math.Max(box.Minimum, box.Maximum));
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => d.CoerceValue(ValueProperty);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NumberBox)d;
        if (box.textBox is not null)
        {
            box.textBox.Text = Format((int)e.NewValue);
        }

        box.RaiseEvent(new RoutedPropertyChangedEventArgs<int>((int)e.OldValue, (int)e.NewValue, ValueChangedEvent));
    }

    private static string Format(int value) => value.ToString(CultureInfo.CurrentCulture);

    private void Detach()
    {
        if (textBox is not null)
        {
            textBox.PreviewKeyDown -= OnTextBoxPreviewKeyDown;
            textBox.LostKeyboardFocus -= OnTextBoxLostFocus;
        }

        if (upButton is not null)
        {
            upButton.Click -= OnUpClick;
        }

        if (downButton is not null)
        {
            downButton.Click -= OnDownClick;
        }
    }

    /// <summary>Takes the typed text when it is a number in range; otherwise restores the current value.</summary>
    private void Commit()
    {
        if (textBox is null)
        {
            return;
        }

        if (int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int typed) && typed >= Minimum && typed <= Maximum)
        {
            Value = typed;
        }

        textBox.Text = Format(Value);
    }

    private void OnTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        int? delta = e.Key switch
        {
            Key.Up => SmallChange,
            Key.Down => -SmallChange,
            Key.PageUp => LargeChange,
            Key.PageDown => -LargeChange,
            _ => null,
        };

        if (delta is { } step)
        {
            Step(step);
            textBox?.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Commit();
            textBox?.SelectAll();
            e.Handled = true;
        }
    }

    private void OnTextBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    private void OnUpClick(object sender, RoutedEventArgs e) => Step(SmallChange);

    private void OnDownClick(object sender, RoutedEventArgs e) => Step(-SmallChange);

    /// <summary>Exposes the box as a spinner with a range value (Narrator reads "36, spinner").</summary>
    private sealed class NumberBoxAutomationPeer(NumberBox owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        private NumberBox Box => (NumberBox)Owner;

        public bool IsReadOnly => !Box.IsEnabled;

        public double LargeChange => Box.LargeChange;

        public double Maximum => Box.Maximum;

        public double Minimum => Box.Minimum;

        public double SmallChange => Box.SmallChange;

        public double Value => Box.Value;

        public void SetValue(double value) => Box.Value = (int)Math.Round(value);

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Spinner;

        protected override string GetClassNameCore() => nameof(NumberBox);
    }
}
