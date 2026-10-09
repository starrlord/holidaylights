using System.Windows;
using System.Windows.Input;

namespace HolidayLights.App.Controls;

/// <summary>The keyboard model of the frame editor (PRODUCT-SPEC 3.2.12): roving focus where focus is selection.</summary>
public partial class ArrangementEditor
{
    /// <summary>Arrows move spatially, Ctrl+arrows reorder, Delete removes, Enter picks, Shift+F10 opens a menu, Ctrl+V pastes.</summary>
    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is not ArrangementChip chip || TargetOf(chip) is not { } target)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys modifiers = Keyboard.Modifiers;
        switch (key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down when modifiers == ModifierKeys.Control:
                e.Handled = MoveChip(target, key);
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End when modifiers == ModifierKeys.None:
                if (Neighbor(target, key) is { } next)
                {
                    next.Focus();
                    if (TargetOf(next) is { } moved)
                    {
                        Target = moved;
                    }
                }

                e.Handled = true;
                break;
            case Key.Delete or Key.Back when modifiers == ModifierKeys.None:
                if (target.Kind is ArrangementTargetKind.Chip or ArrangementTargetKind.Corner)
                {
                    OnChipRemove(chip);
                    FocusTarget();
                }

                e.Handled = true;
                break;
            case Key.Enter when modifiers == ModifierKeys.None:
                PickBulbRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.F10 when modifiers == ModifierKeys.Shift:
            case Key.Apps:
                OpenMenuFor(chip, target, fromKeyboard: true);
                e.Handled = true;
                break;
            case Key.V when modifiers == ModifierKeys.Control:
                PasteRequested?.Invoke(this, target.Kind == ArrangementTargetKind.Corner
                    ? BoxPosition.InCorner(target.Corner)
                    : BoxPosition.OnEdge(target.Side, Arrangement.GetEdge(target.Side).Count));
                e.Handled = true;
                break;
        }
    }

    /// <summary>Ctrl+arrows: moves the focused chip one place along its edge (Left/Right on top and bottom, Up/Down on the sides).</summary>
    private bool MoveChip(ArrangementTarget target, Key key)
    {
        if (target.Kind != ArrangementTargetKind.Chip)
        {
            return false;
        }

        bool vertical = target.Side is Side.Left or Side.Right;
        int delta = (key, vertical) switch
        {
            (Key.Left, false) or (Key.Up, true) => -1,
            (Key.Right, false) or (Key.Down, true) => 1,
            _ => 0,
        };
        int to = target.Index + delta;
        if (delta != 0 && to >= 0 && to < Arrangement.GetEdge(target.Side).Count)
        {
            Raise(ArrangementEdits.Reorder(Arrangement, target.Side, target.Index, to, NameOf));
        }

        return true;
    }

    /// <summary>The item an arrow key moves to: along an edge, into the neighboring corner, or across the stage.</summary>
    private ArrangementChip? Neighbor(ArrangementTarget target, Key key)
    {
        if (target.Kind == ArrangementTargetKind.Corner)
        {
            return (target.Corner, key) switch
            {
                (Corner.TopLeft, Key.Right) => First(Side.Top),
                (Corner.TopLeft, Key.Down) => First(Side.Left),
                (Corner.TopRight, Key.Left) => Last(Side.Top),
                (Corner.TopRight, Key.Down) => First(Side.Right),
                (Corner.BottomLeft, Key.Right) => First(Side.Bottom),
                (Corner.BottomLeft, Key.Up) => Last(Side.Left),
                (Corner.BottomRight, Key.Left) => Last(Side.Bottom),
                (Corner.BottomRight, Key.Up) => Last(Side.Right),
                _ => null,
            };
        }

        Side side = target.Side;
        IReadOnlyList<ArrangementChip> items = edges[side].Items;
        int index = target.Kind == ArrangementTargetKind.Chip ? target.Index : items.Count - 1;
        bool vertical = side is Side.Left or Side.Right;
        Key back = vertical ? Key.Up : Key.Left;
        Key forward = vertical ? Key.Down : Key.Right;
        if (key == Key.Home)
        {
            return items[0];
        }

        if (key == Key.End)
        {
            return items[^1];
        }

        if (key == back)
        {
            return index > 0 ? items[index - 1] : corners[StartCorner(side)].Chip;
        }

        if (key == forward)
        {
            return index < items.Count - 1 ? items[index + 1] : corners[EndCorner(side)].Chip;
        }

        Side? across = (side, key) switch
        {
            (Side.Top, Key.Down) => Side.Bottom,
            (Side.Bottom, Key.Up) => Side.Top,
            (Side.Left, Key.Right) => Side.Right,
            (Side.Right, Key.Left) => Side.Left,
            _ => null,
        };
        return across is { } other ? edges[other].Items[Math.Min(index, edges[other].Items.Count - 1)] : null;
    }

    private ArrangementChip First(Side side) => edges[side].Items[0];

    private ArrangementChip Last(Side side) => edges[side].Items[^1];

    /// <summary>The corner before an edge's first item (left of the top and bottom edges, above the side edges).</summary>
    private static Corner StartCorner(Side side) => side switch
    {
        Side.Top or Side.Left => Corner.TopLeft,
        Side.Right => Corner.TopRight,
        _ => Corner.BottomLeft,
    };

    /// <summary>The corner after an edge's last item.</summary>
    private static Corner EndCorner(Side side) => side switch
    {
        Side.Top or Side.Right => Corner.TopRight,
        Side.Left => Corner.BottomLeft,
        _ => Corner.BottomRight,
    };

    /// <summary>Shift+F10 or the Menu key: the chip menu on a bulb, the box menu on a "+" or an empty corner.</summary>
    private void OpenMenuFor(ArrangementChip chip, ArrangementTarget target, bool fromKeyboard)
    {
        if (target.Kind == ArrangementTargetKind.Plus)
        {
            OpenBoxMenu(BoxPosition.OnEdge(target.Side, 0), chip, fromKeyboard);
        }
        else if (target.Kind == ArrangementTargetKind.Corner && chip.BulbId is null)
        {
            OpenBoxMenu(BoxPosition.InCorner(target.Corner), chip, fromKeyboard);
        }
        else
        {
            OpenChipMenu(chip, target, fromKeyboard);
        }
    }
}
