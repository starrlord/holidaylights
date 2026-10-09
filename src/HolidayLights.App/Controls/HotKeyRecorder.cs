using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace HolidayLights.App.Controls;

/// <summary>
/// Shows a key combination as key caps ("Ctrl" "Alt" "Shift" "B") and, while it has keyboard focus, records the next
/// combination pressed (PRODUCT-SPEC 3.0.1, 3.8.2). Held modifiers show live; a combination ends with an accepted key
/// (<see cref="HotKeyText.ToBindingKey"/>). Tab leaves the field and Esc is left to the dialog (it cancels). Default style
/// in <c>Themes/Generic.xaml</c>; with <see cref="IsReadOnly"/> it only shows caps (General &gt; Hot Keys).
/// </summary>
public class HotKeyRecorder : Control
{
    /// <summary>Identifies <see cref="Binding"/>.</summary>
    public static readonly DependencyProperty BindingProperty =
        DependencyProperty.Register(nameof(Binding), typeof(HotKeyBinding), typeof(HotKeyRecorder), new PropertyMetadata(null, OnBindingChanged));

    /// <summary>Identifies <see cref="IsReadOnly"/>.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(HotKeyRecorder), new PropertyMetadata(false, OnIsReadOnlyChanged));

    private static readonly DependencyPropertyKey CapsPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Caps), typeof(IReadOnlyList<string>), typeof(HotKeyRecorder), new PropertyMetadata(Array.Empty<string>()));

    /// <summary>Identifies <see cref="Caps"/>.</summary>
    public static readonly DependencyProperty CapsProperty = CapsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsRecordingPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsRecording), typeof(bool), typeof(HotKeyRecorder), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="IsRecording"/>.</summary>
    public static readonly DependencyProperty IsRecordingProperty = IsRecordingPropertyKey.DependencyProperty;

    static HotKeyRecorder() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(HotKeyRecorder), new FrameworkPropertyMetadata(typeof(HotKeyRecorder)));

    /// <summary>Creates the recorder.</summary>
    public HotKeyRecorder() => Focusable = true;

    /// <summary>Raised when a complete combination was pressed (<see cref="Binding"/> already holds it).</summary>
    public event EventHandler<HotKeyBinding>? Recorded;

    /// <summary>The combination shown (and the last one recorded).</summary>
    public HotKeyBinding? Binding
    {
        get => (HotKeyBinding?)GetValue(BindingProperty);
        set => SetValue(BindingProperty, value);
    }

    /// <summary>Only shows the caps; never records (and is not a tab stop).</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>The caps to draw (live while modifiers are held).</summary>
    public IReadOnlyList<string> Caps => (IReadOnlyList<string>)GetValue(CapsProperty);

    /// <summary>True while the field has keyboard focus and listens for keys.</summary>
    public bool IsRecording => (bool)GetValue(IsRecordingProperty);

    /// <inheritdoc />
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SetValue(IsRecordingPropertyKey, !IsReadOnly);
    }

    /// <inheritdoc />
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        SetValue(IsRecordingPropertyKey, false);
        ShowBinding();
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!IsRecording)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape)
        {
            return;
        }

        HotKeyModifiers modifiers = HotKeyText.ToBindingModifiers(Keyboard.Modifiers);
        e.Handled = true;
        if (HotKeyText.IsModifierKey(key))
        {
            SetValue(CapsPropertyKey, HotKeyText.Caps(modifiers, null));
            return;
        }

        if (HotKeyText.ToBindingKey(key) is not { } bindingKey)
        {
            return;
        }

        var recorded = new HotKeyBinding { Enabled = Binding?.Enabled ?? true, Modifiers = modifiers, Key = bindingKey };
        Binding = recorded;
        Recorded?.Invoke(this, recorded);
        Announcer.Announce(this, HotKeyText.Spoken(recorded));
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (IsRecording && Keyboard.Modifiers == ModifierKeys.None)
        {
            ShowBinding();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (!IsReadOnly)
        {
            Focus();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new HotKeyRecorderAutomationPeer(this);

    private static void OnBindingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((HotKeyRecorder)d).ShowBinding();

    private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var recorder = (HotKeyRecorder)d;
        bool readOnly = (bool)e.NewValue;
        recorder.Focusable = !readOnly;
        recorder.IsTabStop = !readOnly;
    }

    private void ShowBinding() => SetValue(CapsPropertyKey, Binding is { } binding ? HotKeyText.Caps(binding) : Array.Empty<string>());

    /// <summary>Exposes the combination as the value of an edit field (Narrator: "Ctrl Alt Shift B").</summary>
    private sealed class HotKeyRecorderAutomationPeer(HotKeyRecorder owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        private HotKeyRecorder Recorder => (HotKeyRecorder)Owner;

        public bool IsReadOnly => true;

        public string Value => string.Join(" ", Recorder.Caps);

        public void SetValue(string value) => throw new InvalidOperationException("Press the keys to record a combination.");

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            Recorder.IsReadOnly ? AutomationControlType.Text : AutomationControlType.Edit;

        protected override string GetClassNameCore() => nameof(HotKeyRecorder);

        protected override bool IsKeyboardFocusableCore() => !Recorder.IsReadOnly;
    }
}
