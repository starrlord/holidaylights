using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using HolidayLights.App.Controls;

namespace HolidayLights.App.Gallery;

/// <summary>
/// The "Add To..." flyout (PRODUCT-SPEC 3.2.6): a miniature of the frame, so the button teaches the box model. An edge
/// adds the bulb(s) at its end (a full edge is disabled), a corner is replaced, and the bottom row adds to every edge,
/// fills the corners or uses the bulb everywhere. Arrow keys move between the regions; Enter applies; Esc closes.
/// </summary>
public static class AddToFlyout
{
    /// <summary>Opens the flyout below an anchor.</summary>
    /// <param name="anchor">The "Add To..." button.</param>
    /// <param name="bulbIds">The bulbs, in selection order.</param>
    /// <param name="title">The flyout title: "Add "Standard Bulbs" to" or "Add 3 bulbs to".</param>
    /// <param name="arrangement">The arrangement (for the "2/6" counts and full edges).</param>
    /// <param name="apply">Carries out the chosen command.</param>
    /// <returns>The popup.</returns>
    public static Popup Show(FrameworkElement anchor, IReadOnlyList<string> bulbIds, string title, SlotAssignment arrangement, Action<BulbActionRequest> apply)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(bulbIds);
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(apply);
        bool one = bulbIds.Count == 1;
        var grid = new Grid();
        KeyboardNavigation.SetDirectionalNavigation(grid, KeyboardNavigationMode.Contained);
        KeyboardNavigation.SetTabNavigation(grid, KeyboardNavigationMode.Once);
        AutomationProperties.SetName(grid, title);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        for (int i = 0; i < 4; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Popup? popup = null;
        void Choose(BulbActionRequest request)
        {
            if (popup is not null)
            {
                popup.IsOpen = false;
            }

            apply(request);
        }

        Button Region(string text, int row, int column, bool enabled, BulbActionRequest request, string? tip = null, int rowSpan = 1)
        {
            var button = new Button
            {
                Content = text,
                IsEnabled = enabled,
                Margin = new Thickness(3),
                MinHeight = rowSpan > 1 ? 72 : 36,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(button, text.Replace("\n", " ", StringComparison.Ordinal));
            if (tip is not null)
            {
                button.ToolTip = tip;
                ToolTipService.SetShowOnDisabled(button, true);
            }

            button.Click += (_, _) => Choose(request);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
            Grid.SetRowSpan(button, rowSpan);
            grid.Children.Add(button);
            return button;
        }

        string fullTip = anchor.TryFindResource("HL.Tip.BulbFactory.EdgeFull") as string ?? "This edge already has 6 bulb types. Remove one first.";
        string Count(Side side) => string.Create(CultureInfo.CurrentCulture, $"{arrangement.GetEdge(side).Count}/{SlotAssignment.MaxTypesPerEdge}");
        bool Room(Side side) => arrangement.GetEdge(side).Count < SlotAssignment.MaxTypesPerEdge;
        BulbActionRequest Edge(Side side) => new(BulbAction.AddToEdge, bulbIds, side);
        BulbActionRequest CornerRequest(Corner corner) => new(BulbAction.UseInCorner, bulbIds, Corner: corner);

        Button first = Region("Top-Left", 0, 0, true, CornerRequest(Corner.TopLeft));
        Region($"Top Edge  {Count(Side.Top)}", 0, 1, Room(Side.Top), Edge(Side.Top), Room(Side.Top) ? null : fullTip);
        Region("Top-Right", 0, 2, true, CornerRequest(Corner.TopRight));
        Region($"Left\n{Count(Side.Left)}", 1, 0, Room(Side.Left), Edge(Side.Left), Room(Side.Left) ? null : fullTip);
        Region($"Right\n{Count(Side.Right)}", 1, 2, Room(Side.Right), Edge(Side.Right), Room(Side.Right) ? null : fullTip);
        Region("Bottom-Left", 2, 0, true, CornerRequest(Corner.BottomLeft));
        Region($"Bottom Edge  {Count(Side.Bottom)}", 2, 1, Room(Side.Bottom), Edge(Side.Bottom), Room(Side.Bottom) ? null : fullTip);
        Region("Bottom-Right", 2, 2, true, CornerRequest(Corner.BottomRight));
        foreach (UIElement child in grid.Children)
        {
            if (child is Button { Content: string text } button && text.Contains('\n', StringComparison.Ordinal))
            {
                button.Content = new TextBlock { Text = text, TextAlignment = TextAlignment.Center };
                button.MinHeight = 64;
            }
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(actions, 3);
        Grid.SetColumnSpan(actions, 3);
        grid.Children.Add(actions);
        void Action(string text, bool enabled, BulbActionRequest request, string? tip = null)
        {
            var button = new Button { Content = text, IsEnabled = enabled, Margin = new Thickness(3) };
            if (tip is not null)
            {
                button.ToolTip = tip;
            }

            button.Click += (_, _) => Choose(request);
            actions.Children.Add(button);
        }

        Action("Add to Every Edge", CellSlots.Sides.Any(Room), new BulbActionRequest(BulbAction.AddToEveryEdge, bulbIds));
        Action("Use in All Corners", one, new BulbActionRequest(BulbAction.UseInAllCorners, bulbIds));
        Action("Use Everywhere", one, new BulbActionRequest(BulbAction.UseEverywhere, bulbIds), anchor.TryFindResource("HL.Tip.BulbFactory.UseEverywhere") as string);
        popup = Flyout.Show(anchor, grid, PlacementMode.Bottom, title);
        first.Dispatcher.BeginInvoke(() => first.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        return popup;
    }
}
