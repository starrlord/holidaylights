using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HolidayLights.App.Controls;

/// <summary>The box and chip menus of the frame editor (PRODUCT-SPEC 3.2.2, 3.2.11).</summary>
public partial class ArrangementEditor
{
    /// <summary>The box menu: "Clear", "Copy This Edge to All Edges" (edges) and "Pick a Bulb...".</summary>
    /// <param name="box">The edge (any index) or the corner.</param>
    /// <param name="anchor">The element the menu opens at.</param>
    /// <param name="fromKeyboard">True to open below the anchor instead of at the pointer.</param>
    private void OpenBoxMenu(BoxPosition box, UIElement anchor, bool fromKeyboard)
    {
        SlotAssignment arrangement = Arrangement;
        var menu = new ContextMenu();
        if (box.Side is { } side)
        {
            bool empty = arrangement.GetEdge(side).Count == 0;
            menu.Items.Add(Item("_Clear", !empty, () => Raise(ArrangementEdits.ClearEdge(Arrangement, side))));
            menu.Items.Add(Item("Copy This Edge to _All Edges", !empty, () => Raise(ArrangementEdits.CopyEdgeToAll(Arrangement, side))));
            menu.Items.Add(Item("_Pick a Bulb…", true, () => PickFor(ArrangementTarget.ForPlus(side).Normalize(Arrangement))));
        }
        else
        {
            Corner corner = box.Corner;
            bool empty = arrangement.GetCorner(corner) is null;
            menu.Items.Add(Item("_Clear", !empty, () => Raise(ArrangementEdits.ClearCorner(Arrangement, corner, NameOf))));
            menu.Items.Add(Item("_Pick a Bulb…", true, () => PickFor(ArrangementTarget.ForCorner(corner))));
        }

        Open(menu, anchor, fromKeyboard);
    }

    /// <summary>
    /// The chip or corner menu: "Move Left" / "Move Right" ("Move Up" / "Move Down" on the side edges), "Copy To" and
    /// "Move To" the other 7 boxes, "Remove", "Edit Bulb..." or "Edit Categories...", "Bulb Credits", "Find in Bulb List".
    /// </summary>
    private void OpenChipMenu(ArrangementChip chip, ArrangementTarget target, bool fromKeyboard)
    {
        if (chip.BulbId is not { } bulbId)
        {
            return;
        }

        SlotAssignment arrangement = Arrangement;
        BoxPosition source;
        var menu = new ContextMenu();
        if (target.Kind == ArrangementTargetKind.Chip)
        {
            Side side = target.Side;
            int index = target.Index;
            int count = arrangement.GetEdge(side).Count;
            bool vertical = side is Side.Left or Side.Right;
            menu.Items.Add(Item(vertical ? "Move _Up" : "Move _Left", index > 0,
                () => Raise(ArrangementEdits.Reorder(Arrangement, side, index, index - 1, NameOf))));
            menu.Items.Add(Item(vertical ? "Move _Down" : "Move _Right", index < count - 1,
                () => Raise(ArrangementEdits.Reorder(Arrangement, side, index, index + 1, NameOf))));
            source = BoxPosition.OnEdge(side, index);
        }
        else
        {
            source = BoxPosition.InCorner(target.Corner);
        }

        menu.Items.Add(TransferMenu("_Copy To", source, move: false));
        menu.Items.Add(TransferMenu("Mo_ve To", source, move: true));
        menu.Items.Add(Item("Re_move", true, () => OnChipRemove(chip)));
        menu.Items.Add(new Separator());
        BulbInfo? info = null;
        bool known = services is not null && services.Bulbs.TryGetInfo(bulbId, out info);
        bool editable = info?.IsEditable == true;
        menu.Items.Add(Item(editable ? "_Edit Bulb…" : "_Edit Categories…", known, () => RequestBulbCommand(BulbCommand.Edit, bulbId)));
        menu.Items.Add(Item("Bulb Cre_dits", known, () => RequestBulbCommand(BulbCommand.Credits, bulbId)));
        menu.Items.Add(Item("_Find in Bulb List", known, () => RequestBulbCommand(BulbCommand.FindInList, bulbId)));
        Open(menu, chip, fromKeyboard);
    }

    /// <summary>"Copy To" or "Move To" with the other 7 boxes; a full edge is disabled.</summary>
    private MenuItem TransferMenu(string header, BoxPosition source, bool move)
    {
        SlotAssignment arrangement = Arrangement;
        var menu = new MenuItem { Header = header };
        foreach (Side side in CellSlots.Sides)
        {
            if (source.Side == side)
            {
                continue;
            }

            int count = arrangement.GetEdge(side).Count;
            MenuItem item = Item(ArrangementTexts.EdgeTitle(side), count < SlotAssignment.MaxTypesPerEdge,
                () => Raise(ArrangementEdits.Transfer(Arrangement, source, BoxPosition.OnEdge(side, Arrangement.GetEdge(side).Count), replace: false, move, NameOf)));
            if (!item.IsEnabled)
            {
                item.ToolTip = TryFindResource("HL.Tip.BulbFactory.EdgeFull");
                ToolTipService.SetShowOnDisabled(item, true);
            }

            menu.Items.Add(item);
        }

        foreach (Corner corner in CellSlots.Corners)
        {
            if (source.Side is null && source.Corner == corner)
            {
                continue;
            }

            menu.Items.Add(Item(ArrangementTexts.CornerTitle(corner), true,
                () => Raise(ArrangementEdits.Transfer(Arrangement, source, BoxPosition.InCorner(corner), replace: true, move, NameOf))));
        }

        return menu;
    }

    private void PickFor(ArrangementTarget target)
    {
        Target = target;
        PickBulbRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RequestBulbCommand(BulbCommand command, string bulbId) =>
        BulbCommandRequested?.Invoke(this, new BulbCommandRequest(command, bulbId));

    private static MenuItem Item(string header, bool enabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private static void Open(ContextMenu menu, UIElement anchor, bool fromKeyboard)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = fromKeyboard ? PlacementMode.Bottom : PlacementMode.MousePoint;
        menu.IsOpen = true;
    }
}
