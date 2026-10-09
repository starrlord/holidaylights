using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HolidayLights.App.Settings.Dialogs;

/// <summary>
/// A modal dialog over the Settings window (PRODUCT-SPEC 2.4, 3.6.3, 3.8): Fluent surfaces, centred on its owner, the
/// content on top and a button bar at the bottom (the primary button is the default unless destructive; the close
/// button is the cancel button, so Esc closes).
/// </summary>
public class DialogWindow : Window
{
    private readonly ContentControl body;

    /// <summary>Creates a dialog.</summary>
    /// <param name="owner">The owner window, or null.</param>
    /// <param name="title">The caption.</param>
    public DialogWindow(Window? owner, string title)
    {
        Title = title;
        if (owner is not null && owner.IsLoaded)
        {
            Owner = owner;
        }

        WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        body = new ContentControl { Margin = new Thickness(24, 20, 24, 20), Focusable = false, IsTabStop = false };
        ButtonBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        LeftButtonBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var barGrid = new Grid();
        barGrid.Children.Add(LeftButtonBar);
        barGrid.Children.Add(ButtonBar);
        var bar = new Border { Padding = new Thickness(24, 16, 24, 16), BorderThickness = new Thickness(0, 1, 0, 0), Child = barGrid };
        bar.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorSecondaryBrush");
        bar.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(bar, 1);
        root.Children.Add(body);
        root.Children.Add(bar);
        Content = root;
    }

    /// <summary>The dialog's content (above the button bar).</summary>
    public object? Body
    {
        get => body.Content;
        set => body.Content = value;
    }

    /// <summary>The right-aligned buttons ([Save] [Cancel]).</summary>
    protected StackPanel ButtonBar { get; }

    /// <summary>The left-aligned buttons ("Use Default", "Reset to Defaults").</summary>
    protected StackPanel LeftButtonBar { get; }

    /// <summary>Adds a button to the right of the bar.</summary>
    /// <param name="text">The label.</param>
    /// <param name="isPrimary">True for the accent (default) button.</param>
    /// <param name="isCancel">True for the button Esc chooses.</param>
    /// <param name="click">What it does.</param>
    /// <returns>The button.</returns>
    protected Button AddButton(string text, bool isPrimary, bool isCancel, Action click)
    {
        ArgumentNullException.ThrowIfNull(click);
        var button = new Button { Content = text, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsDefault = isPrimary, IsCancel = isCancel };
        if (isPrimary)
        {
            button.SetResourceReference(StyleProperty, "AccentButtonStyle");
            button.SetResourceReference(ContentTemplateProperty, "HL.Template.GlyphLabel");
        }

        button.Click += (_, _) => click();
        ButtonBar.Children.Add(button);
        return button;
    }

    /// <summary>Adds a button to the left of the bar.</summary>
    /// <param name="text">The label.</param>
    /// <param name="click">What it does.</param>
    /// <returns>The button.</returns>
    protected Button AddLeftButton(string text, Action click)
    {
        ArgumentNullException.ThrowIfNull(click);
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => click();
        LeftButtonBar.Children.Add(button);
        return button;
    }

    /// <summary>A secondary text block (descriptions and notes).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The block.</returns>
    protected static TextBlock Secondary(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return block;
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (Owner?.Icon is ImageSource icon)
        {
            Icon = icon;
        }
    }
}
