using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HolidayLights.App.Settings.Dialogs;

/// <summary>
/// The Color dialog (PRODUCT-SPEC 3.8.3): the 48 basic colors of the classic ChooseColor, 16 custom colors (part of the
/// settings and of the Cancel snapshot), a hue/saturation field with a luminance slider, a "#RRGGBB" box, the old and the
/// new color, "Add to Custom Colors", [OK] [Cancel]. Fully keyboard operable.
/// </summary>
public sealed class ColorDialog : DialogWindow
{
    /// <summary>The 48 basic colors of the classic ChooseColor dialog, row by row.</summary>
    public static readonly IReadOnlyList<RgbColor> BasicColors =
    [
        .. new[]
        {
            "#FF8080", "#FFFF80", "#80FF80", "#00FF80", "#80FFFF", "#0080FF", "#FF80C0", "#FF80FF",
            "#FF0000", "#FFFF00", "#80FF00", "#00FF40", "#00FFFF", "#0080C0", "#8080C0", "#FF00FF",
            "#804040", "#FF8040", "#00FF00", "#008080", "#004080", "#8080FF", "#800040", "#FF0080",
            "#800000", "#FF8000", "#008000", "#008040", "#0000FF", "#0000A0", "#800080", "#8000FF",
            "#400000", "#804000", "#004000", "#004040", "#000080", "#000040", "#400040", "#400080",
            "#000000", "#808000", "#808040", "#808080", "#408080", "#C0C0C0", "#400040", "#FFFFFF",
        }.Select(RgbColor.Parse),
    ];

    private const double FieldWidth = 240;
    private const double FieldHeight = 180;

    /// <summary>The ring that marks the chosen hue and saturation (kept inside the field at its edges).</summary>
    private const double MarkerSize = 14;

    private readonly IAppServices services;
    private readonly RgbColor original;
    private readonly Canvas field = new() { Width = FieldWidth, Height = FieldHeight, Focusable = true, ClipToBounds = true };
    private readonly Grid marker = new()
    {
        Width = MarkerSize,
        Height = MarkerSize,
        IsHitTestVisible = false,
        Children =
        {
            new Ellipse { Stroke = Brushes.Black, StrokeThickness = 1, Opacity = 0.6 },
            new Ellipse { Stroke = Brushes.White, StrokeThickness = 2, Margin = new Thickness(1) },
        },
    };
    private readonly Slider luminance = new() { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 240, Height = FieldHeight, SmallChange = 4, LargeChange = 24, IsMoveToPointEnabled = true };
    private readonly Rectangle luminanceTrack = new() { Width = 14, Height = FieldHeight, RadiusX = 3, RadiusY = 3 };
    private readonly TextBox hexBox = new() { Width = 96, MaxLength = 7 };
    private readonly Border newSwatch = new() { Width = 56, Height = 32, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
    private readonly UniformGrid customGrid = new() { Columns = 8, Rows = 2 };
    private double hue;
    private double saturation;
    private bool updating;

    /// <summary>Creates the dialog.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="services">The services (custom colors).</param>
    /// <param name="color">The current color.</param>
    public ColorDialog(Window? owner, IAppServices services, RgbColor color)
        : base(owner, "Color")
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services;
        original = color;
        Color = color;
        var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        left.Children.Add(Heading("_Basic colors:", out Label basicLabel));
        UniformGrid basic = SwatchGrid(BasicColors, 6, "Basic colors");
        basicLabel.Target = basic.Children[0];
        left.Children.Add(basic);
        left.Children.Add(Heading("_Custom colors:", out Label customLabel));
        left.Children.Add(customGrid);
        var add = new Button { Content = "_Add to Custom Colors", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => AddToCustom();
        left.Children.Add(add);
        BuildCustom();
        customLabel.Target = customGrid.Children[0];

        BuildField();
        var right = new StackPanel();
        var picker = new StackPanel { Orientation = Orientation.Horizontal };
        picker.Children.Add(new Border { Child = field, CornerRadius = new CornerRadius(4), ClipToBounds = true, BorderThickness = new Thickness(1) });
        var lumPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        luminanceTrack.Margin = new Thickness(0, 0, 4, 0);
        lumPanel.Children.Add(luminanceTrack);
        lumPanel.Children.Add(luminance);
        AutomationProperties.SetName(luminance, "Luminance");
        picker.Children.Add(lumPanel);
        right.Children.Add(picker);
        var previews = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        previews.Children.Add(new TextBlock { Text = "Old", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var oldSwatch = new Border { Width = 56, Height = 32, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Background = new SolidColorBrush(ToMedia(original)) };
        oldSwatch.SetResourceReference(Border.BorderBrushProperty, "ControlStrongStrokeColorDefaultBrush");
        AutomationProperties.SetName(oldSwatch, "Old color " + original);
        previews.Children.Add(oldSwatch);
        previews.Children.Add(new TextBlock { Text = "New", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
        newSwatch.SetResourceReference(Border.BorderBrushProperty, "ControlStrongStrokeColorDefaultBrush");
        previews.Children.Add(newSwatch);
        var hexLabel = new Label { Content = "_Hex:", Target = hexBox, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(14, 0, 6, 0) };
        hexLabel.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        previews.Children.Add(hexLabel);
        AutomationProperties.SetName(hexBox, "Color as #RRGGBB");
        previews.Children.Add(hexBox);
        right.Children.Add(previews);
        Body = new StackPanel { Orientation = Orientation.Horizontal, Children = { left, right } };
        AddButton("OK", isPrimary: true, isCancel: false, () =>
        {
            DialogResult = true;
            Close();
        });
        AddButton("Cancel", isPrimary: false, isCancel: true, Close);
        luminance.ValueChanged += (_, _) =>
        {
            if (!updating)
            {
                SetColor(FromHsl(hue, saturation, luminance.Value / 240.0));
            }
        };
        hexBox.TextChanged += (_, _) =>
        {
            if (!updating && hexBox.Text.Length == 7 && RgbColor.TryParse(hexBox.Text, out RgbColor parsed))
            {
                SetColor(parsed, updateHex: false);
            }
        };
        SetColor(color);
        Loaded += (_, _) => basic.Children.OfType<Button>().FirstOrDefault(b => b.Tag is RgbColor c && c == color)?.Focus();
    }

    /// <summary>The chosen color.</summary>
    public RgbColor Color { get; private set; }

    /// <summary>Shows the dialog over the Settings window.</summary>
    /// <param name="host">The Settings window.</param>
    /// <param name="services">The services.</param>
    /// <param name="color">The current color.</param>
    /// <returns>The new color, or null when cancelled.</returns>
    public static RgbColor? Show(ISettingsHost host, IAppServices services, RgbColor color)
    {
        ArgumentNullException.ThrowIfNull(host);
        var dialog = new ColorDialog(host.Window, services, color);
        return dialog.ShowDialog() == true ? dialog.Color : null;
    }

    /// <summary>An RGB color from hue (0-1), saturation (0-1) and luminance (0-1).</summary>
    /// <param name="h">The hue.</param>
    /// <param name="s">The saturation.</param>
    /// <param name="l">The luminance.</param>
    /// <returns>The color.</returns>
    public static RgbColor FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h * 6 % 2) - 1));
        double m = l - c / 2;
        (double r, double g, double b) = (int)(h * 6 % 6) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        return new RgbColor(Byte(r + m), Byte(g + m), Byte(b + m));
    }

    /// <summary>Hue, saturation and luminance (each 0-1) of a color.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The components.</returns>
    public static (double H, double S, double L) ToHsl(RgbColor color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        double d = max - min;
        if (d < 1e-9)
        {
            return (0, 0, l);
        }

        double s = d / (1 - Math.Abs(2 * l - 1));
        double h = max == r ? ((g - b) / d % 6 + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, Math.Clamp(s, 0, 1), l);
    }

    private static Color ToMedia(RgbColor color) => System.Windows.Media.Color.FromRgb(color.R, color.G, color.B);

    private static StackPanel Heading(string text, out Label label)
    {
        label = new Label { Content = text, Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 6) };
        label.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        return new StackPanel { Children = { label } };
    }

    private UniformGrid SwatchGrid(IReadOnlyList<RgbColor> colors, int rows, string name)
    {
        var grid = new UniformGrid { Columns = 8, Rows = rows };
        KeyboardNavigation.SetDirectionalNavigation(grid, KeyboardNavigationMode.Contained);
        KeyboardNavigation.SetTabNavigation(grid, KeyboardNavigationMode.Once);
        AutomationProperties.SetName(grid, name);
        foreach (RgbColor color in colors)
        {
            grid.Children.Add(Swatch(color));
        }

        return grid;
    }

    private Button Swatch(RgbColor color)
    {
        var swatch = new Button
        {
            Width = 26,
            Height = 22,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            Tag = color,
            Background = new SolidColorBrush(ToMedia(color)),
            ToolTip = color.ToString(),
            Template = SwatchTemplate(),
        };
        AutomationProperties.SetName(swatch, color.ToString());
        swatch.Click += (_, _) => SetColor(color);
        return swatch;
    }

    private static ControlTemplate SwatchTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetResourceReference(Border.BorderBrushProperty, "ControlStrongStrokeColorDefaultBrush");
        template.VisualTree = border;
        return template;
    }

    private void BuildCustom()
    {
        customGrid.Children.Clear();
        KeyboardNavigation.SetDirectionalNavigation(customGrid, KeyboardNavigationMode.Contained);
        KeyboardNavigation.SetTabNavigation(customGrid, KeyboardNavigationMode.Once);
        AutomationProperties.SetName(customGrid, "Custom colors");
        foreach (RgbColor color in services.Settings.Current.Colors.Custom.Take(ColorSettings.CustomColorCount))
        {
            customGrid.Children.Add(Swatch(color));
        }
    }

    /// <summary>"Add to Custom Colors": the new color goes into the first slot; the others move along (one undo step).</summary>
    private void AddToCustom()
    {
        RgbColor color = Color;
        services.Settings.Update(
            s => s with { Colors = s.Colors with { Custom = [color, .. s.Colors.Custom.Take(ColorSettings.CustomColorCount - 1)] } },
            SettingsChange.Edit("Add a custom color"));
        BuildCustom();
    }

    private void BuildField()
    {
        var hues = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++)
        {
            hues.GradientStops.Add(new GradientStop(ToMedia(FromHsl(i % 6 / 6.0, 1, 0.5)), i / 6.0));
        }

        field.Background = hues;
        field.Children.Add(new Rectangle
        {
            Width = FieldWidth,
            Height = FieldHeight,
            IsHitTestVisible = false,
            Fill = new LinearGradientBrush(System.Windows.Media.Color.FromArgb(0, 128, 128, 128), System.Windows.Media.Color.FromArgb(255, 128, 128, 128), 90),
        });
        field.Children.Add(marker);
        AutomationProperties.SetName(field, "Hue and saturation");
        field.SetResourceReference(FocusVisualStyleProperty, "DefaultControlFocusVisualStyle");
        field.MouseLeftButtonDown += (_, e) =>
        {
            field.Focus();
            field.CaptureMouse();
            PickAt(e.GetPosition(field));
        };
        field.MouseMove += (_, e) =>
        {
            if (field.IsMouseCaptured)
            {
                PickAt(e.GetPosition(field));
            }
        };
        field.MouseLeftButtonUp += (_, _) => field.ReleaseMouseCapture();
        field.KeyDown += (_, e) =>
        {
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 0.1 : 0.02;
            (double h, double s) = e.Key switch
            {
                Key.Left => (hue - step, saturation),
                Key.Right => (hue + step, saturation),
                Key.Up => (hue, saturation + step),
                Key.Down => (hue, saturation - step),
                _ => (double.NaN, double.NaN),
            };
            if (!double.IsNaN(h))
            {
                hue = (h % 1 + 1) % 1;
                saturation = Math.Clamp(s, 0, 1);
                SetColor(FromHsl(hue, saturation, luminance.Value / 240.0), keepHueSaturation: true);
                e.Handled = true;
            }
        };
    }

    private void PickAt(Point point)
    {
        hue = Math.Clamp(point.X / FieldWidth, 0, 0.9999);
        saturation = Math.Clamp(1 - point.Y / FieldHeight, 0, 1);
        SetColor(FromHsl(hue, saturation, luminance.Value / 240.0), keepHueSaturation: true);
    }

    private void SetColor(RgbColor color, bool updateHex = true, bool keepHueSaturation = false)
    {
        Color = color;
        updating = true;
        try
        {
            (double h, double s, double l) = ToHsl(color);
            if (!keepHueSaturation && s > 0)
            {
                hue = h;
                saturation = s;
            }
            else if (!keepHueSaturation)
            {
                saturation = 0;
            }

            luminance.Value = l * 240;
            Canvas.SetLeft(marker, Math.Clamp(hue * FieldWidth - MarkerSize / 2, 0, FieldWidth - MarkerSize));
            Canvas.SetTop(marker, Math.Clamp((1 - saturation) * FieldHeight - MarkerSize / 2, 0, FieldHeight - MarkerSize));
            luminanceTrack.Fill = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Colors.White, 0),
                    new(ToMedia(FromHsl(hue, saturation, 0.5)), 0.5),
                    new(Colors.Black, 1),
                },
                90);
            newSwatch.Background = new SolidColorBrush(ToMedia(color));
            AutomationProperties.SetName(newSwatch, "New color " + color);
            if (updateHex)
            {
                hexBox.Text = color.ToString();
            }
        }
        finally
        {
            updating = false;
        }
    }

}
