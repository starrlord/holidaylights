using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace HolidayLights.App.Controls;

/// <summary>
/// A search box (Bulb List, Songs): a Fluent text box with a placeholder ("Search 1,550 bulbs"), a search glyph and a
/// clear button. <see cref="SearchChanged"/> follows typing after a 150 ms pause (PRODUCT-SPEC 3.2.5); Esc clears. Default
/// style in <c>Themes/Generic.xaml</c>.
/// </summary>
[TemplatePart(Name = "PART_TextBox", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_ClearButton", Type = typeof(ButtonBase))]
public class SearchBox : Control
{
    /// <summary>Identifies <see cref="Text"/>.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SearchBox),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SearchBox)d).OnTextChanged()));

    /// <summary>Identifies <see cref="Placeholder"/>.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchBox), new PropertyMetadata(""));

    private static readonly DependencyPropertyKey HasTextPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasText), typeof(bool), typeof(SearchBox), new PropertyMetadata(false));

    /// <summary>Identifies <see cref="HasText"/>.</summary>
    public static readonly DependencyProperty HasTextProperty = HasTextPropertyKey.DependencyProperty;

    /// <summary>The pause after typing before <see cref="SearchChanged"/> (PRODUCT-SPEC 3.2.5).</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private readonly DispatcherTimer debounce;
    private TextBox? textBox;
    private ButtonBase? clearButton;

    static SearchBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SearchBox), new FrameworkPropertyMetadata(typeof(SearchBox)));
        FocusableProperty.OverrideMetadata(typeof(SearchBox), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(SearchBox), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Creates the box.</summary>
    public SearchBox()
    {
        debounce = new DispatcherTimer(DispatcherPriority.Input) { Interval = Debounce };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            SearchChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised 150 ms after the text stopped changing, and at once when it is cleared.</summary>
    public event EventHandler? SearchChanged;

    /// <summary>The search text.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The placeholder ("Search 1,550 bulbs"); also the help text for screen readers.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>True when the box has text (the placeholder hides and the clear button shows).</summary>
    public bool HasText => (bool)GetValue(HasTextProperty);

    /// <summary>Moves keyboard focus into the box and selects its text (Ctrl+F).</summary>
    public void FocusAndSelect()
    {
        ApplyTemplate();
        if (textBox is not null)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    /// <summary>Appends typed characters and focuses the box ("printable characters start a search").</summary>
    /// <param name="text">The characters typed in the list.</param>
    public void StartWith(string text)
    {
        ApplyTemplate();
        if (textBox is null)
        {
            return;
        }

        textBox.Focus();
        textBox.Text += text;
        textBox.CaretIndex = textBox.Text.Length;
    }

    /// <summary>Clears the text and raises <see cref="SearchChanged"/> at once.</summary>
    public void Clear()
    {
        Text = "";
        debounce.Stop();
        SearchChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        if (clearButton is not null)
        {
            clearButton.Click -= OnClearClick;
        }

        if (textBox is not null)
        {
            textBox.PreviewKeyDown -= OnTextBoxKeyDown;
        }

        base.OnApplyTemplate();
        textBox = GetTemplateChild("PART_TextBox") as TextBox;
        clearButton = GetTemplateChild("PART_ClearButton") as ButtonBase;
        if (textBox is not null)
        {
            textBox.PreviewKeyDown += OnTextBoxKeyDown;
            AutomationProperties.SetName(textBox, AutomationProperties.GetName(this));
            AutomationProperties.SetHelpText(textBox, Placeholder);
        }

        if (clearButton is not null)
        {
            clearButton.Click += OnClearClick;
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (textBox is null)
        {
            return;
        }

        if (e.Property == AutomationProperties.NameProperty)
        {
            AutomationProperties.SetName(textBox, (string)e.NewValue);
        }
        else if (e.Property == PlaceholderProperty)
        {
            AutomationProperties.SetHelpText(textBox, (string)e.NewValue);
        }
    }

    private void OnTextChanged()
    {
        SetValue(HasTextPropertyKey, !string.IsNullOrEmpty(Text));
        debounce.Stop();
        debounce.Start();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Clear();
        textBox?.Focus();
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && HasText)
        {
            Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && debounce.IsEnabled)
        {
            debounce.Stop();
            SearchChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
