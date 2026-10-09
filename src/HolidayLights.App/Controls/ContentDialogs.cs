using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HolidayLights.App.Controls;

/// <summary>A confirmation (PRODUCT-SPEC 3.8.1): the only modal boxes of the program.</summary>
/// <param name="Title">E.g. "Discard Changes?".</param>
/// <param name="Text">E.g. "Your changes to Star will be lost.".</param>
/// <param name="PrimaryButtonText">The left button, e.g. "Discard" (destructive buttons are never the default).</param>
/// <param name="CloseButtonText">The right button, e.g. "Keep Editing"; Esc chooses it.</param>
/// <param name="PrimaryIsDestructive">True when the primary button destroys something (then Enter does not choose it).</param>
public sealed record ContentDialogOptions(string Title, string Text, string PrimaryButtonText, string CloseButtonText, bool PrimaryIsDestructive);

/// <summary>
/// ContentDialogs (owner: settings-ui; PRODUCT-SPEC 3.0.1): an overlay in the owning window that dims it and shows a
/// centred card with title, text and buttons.
/// </summary>
/// <remarks>
/// While a dialog is open the rest of the window is inert (disabled, so neither the mouse, Tab nor access keys reach it);
/// focus starts on the primary button, or on the close button when the primary button is destructive, and returns to
/// where it was afterwards. A window without a visual tree yet gets the same card in a small modal window instead.
/// </remarks>
public static class ContentDialogs
{
    /// <summary>Identifies the attached <c>IsDialogOpen</c> property (true on a window while a dialog covers it).</summary>
    public static readonly DependencyProperty IsDialogOpenProperty =
        DependencyProperty.RegisterAttached("IsDialogOpen", typeof(bool), typeof(ContentDialogs), new PropertyMetadata(false));

    /// <summary>True while a confirmation covers the window (window-level shortcuts must not run then).</summary>
    /// <param name="window">The window.</param>
    /// <returns>True while a dialog is open.</returns>
    public static bool GetIsDialogOpen(DependencyObject window) => (bool)window.GetValue(IsDialogOpenProperty);

    /// <summary>Sets <c>IsDialogOpen</c>.</summary>
    /// <param name="window">The window.</param>
    /// <param name="value">The value.</param>
    public static void SetIsDialogOpen(DependencyObject window, bool value) => window.SetValue(IsDialogOpenProperty, value);

    /// <summary>Shows a confirmation over a window.</summary>
    /// <param name="owner">The window to dim.</param>
    /// <param name="options">Texts and button roles.</param>
    /// <returns>True when the primary button was chosen; false for the close button or Esc.</returns>
    public static Task<bool> ShowAsync(Window owner, ContentDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(options);
        var card = new ContentDialogCard
        {
            Title = options.Title,
            Text = options.Text,
            PrimaryButtonText = options.PrimaryButtonText,
            CloseButtonText = options.CloseButtonText,
            PrimaryIsDestructive = options.PrimaryIsDestructive,
        };

        return owner.Content is UIElement content && owner.IsLoaded && AdornerLayer.GetAdornerLayer(content) is { } layer
            ? ShowAsOverlay(owner, content, layer, card)
            : Task.FromResult(ShowAsWindow(owner, card));
    }

    private static Task<bool> ShowAsOverlay(Window owner, UIElement content, AdornerLayer layer, ContentDialogCard card)
    {
        var completion = new TaskCompletionSource<bool>();
        IInputElement? previousFocus = Keyboard.FocusedElement;
        bool wasEnabled = content.IsEnabled;
        var adorner = new DialogOverlayAdorner(content, card);

        card.Completed += (_, result) =>
        {
            layer.Remove(adorner);
            content.IsEnabled = wasEnabled;
            SetIsDialogOpen(owner, false);
            if (previousFocus is UIElement { IsVisible: true } element)
            {
                element.Focus();
            }

            completion.TrySetResult(result);
        };

        content.IsEnabled = false;
        SetIsDialogOpen(owner, true);
        layer.Add(adorner);
        return completion.Task;
    }

    private static bool ShowAsWindow(Window owner, ContentDialogCard card)
    {
        var window = new Window
        {
            Title = card.Title,
            Content = card,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        if (owner.IsVisible)
        {
            window.Owner = owner;
        }

        bool result = false;
        card.Completed += (_, primary) =>
        {
            result = primary;
            window.Close();
        };
        window.ShowDialog();
        return result;
    }

    /// <summary>Hosts the dimmed overlay and the card over the adorned window content.</summary>
    private sealed class DialogOverlayAdorner : Adorner
    {
        private readonly Grid root;

        public DialogOverlayAdorner(UIElement adorned, ContentDialogCard card)
            : base(adorned)
        {
            var smoke = new Border();
            smoke.SetResourceReference(Border.BackgroundProperty, "ContentDialogSmokeFill");
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.Margin = new Thickness(24);
            root = new Grid { Children = { smoke, card } };
            KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetControlTabNavigation(root, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(root, KeyboardNavigationMode.Cycle);
            AddVisualChild(root);
            AddLogicalChild(root);
            if (SystemParameters.ClientAreaAnimation)
            {
                root.Opacity = 0;
                root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(167)));
            }
        }

        protected override int VisualChildrenCount => 1;

        /// <summary>
        /// The overlay is a logical child too, so WPF re-evaluates its resource references once the adorner joins the window
        /// (the smoke and the card then follow the window's own dictionaries, not only the application's).
        /// </summary>
        protected override System.Collections.IEnumerator LogicalChildren => new object[] { root }.GetEnumerator();

        protected override Visual GetVisualChild(int index) => index == 0 ? root : throw new ArgumentOutOfRangeException(nameof(index));

        protected override Size MeasureOverride(Size constraint)
        {
            Size size = AdornedElement.RenderSize;
            root.Measure(size);
            return size;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            root.Arrange(new Rect(AdornedElement.RenderSize));
            return AdornedElement.RenderSize;
        }
    }
}
