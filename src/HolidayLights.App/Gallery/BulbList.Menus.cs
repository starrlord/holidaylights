using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HolidayLights.App.Gallery;

/// <summary>The context menu of tiles and rows (PRODUCT-SPEC 3.2.11).</summary>
public partial class BulbList
{
    /// <summary>Builds the menu for the bulb under the pointer (selecting it first) or, on empty space, only "Show".</summary>
    private void OnListContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var list = (ListBox)sender;
        BulbItem? item = e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(list, source) is ListBoxItem { DataContext: BulbItem found }
            ? found
            : null;
        if (item is not null && !list.SelectedItems.Contains(item))
        {
            SetSelection(list, [item]);
        }

        ContextMenu menu = list.ContextMenu;
        menu.Items.Clear();
        IReadOnlyList<BulbItem> selection = item is null ? [] : SelectedItems;
        if (selection.Count > 0)
        {
            AddBulbItems(menu, selection);
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(ShowMenu());
    }

    /// <summary>Opens the bulb menu of the selection below an element (the "..." of the selected-bulb bar).</summary>
    /// <param name="anchor">The element.</param>
    public void OpenMenu(FrameworkElement anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        IReadOnlyList<BulbItem> selection = SelectedItems;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        if (selection.Count > 0)
        {
            AddBulbItems(menu, selection);
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(ShowMenu());
        menu.IsOpen = true;
    }

    /// <summary>Adds the bulb commands for the selection.</summary>
    private void AddBulbItems(ContextMenu menu, IReadOnlyList<BulbItem> selection)
    {
        string[] ids = [.. selection.Select(i => i.Id)];
        bool one = selection.Count == 1;
        BulbItem first = selection[0];
        bool addOn = first.Info.Origin != BulbOrigin.BuiltIn;
        if (IsChooser)
        {
            if (one)
            {
                MenuItem choose = MenuItem("_Choose", true, () => Request(BulbAction.Use, ids));
                choose.FontWeight = FontWeights.SemiBold;
                menu.Items.Add(choose);
            }

            menu.Items.Add(FavoriteItem(selection));
            if (one)
            {
                menu.Items.Add(MenuItem("Bulb _Credits", true, () => Request(BulbAction.Credits, ids)));
            }

            return;
        }

        SlotAssignment arrangement = attached?.Settings.Current.Current.Arrangement ?? SlotAssignment.Empty;
        if (one)
        {
            MenuItem use = MenuItem("_" + Target.ButtonText(), true, () => Request(BulbAction.Use, ids));
            use.FontWeight = FontWeights.SemiBold;
            menu.Items.Add(use);
        }

        foreach ((Side side, string header) in new[]
        {
            (Side.Top, "Add to _Top Edge"),
            (Side.Right, "Add to Ri_ght Edge"),
            (Side.Bottom, "Add to _Bottom Edge"),
            (Side.Left, "Add to _Left Edge"),
        })
        {
            bool room = arrangement.GetEdge(side).Count < SlotAssignment.MaxTypesPerEdge;
            MenuItem add = MenuItem(header, room, () => Request(BulbAction.AddToEdge, ids, side));
            if (!room)
            {
                add.ToolTip = TryFindResource("HL.Tip.BulbFactory.EdgeFull");
                ToolTipService.SetShowOnDisabled(add, true);
            }

            menu.Items.Add(add);
        }

        menu.Items.Add(MenuItem("Add to E_very Edge", true, () => Request(BulbAction.AddToEveryEdge, ids)));
        if (one)
        {
            menu.Items.Add(MenuItem("Use in All C_orners", true, () => Request(BulbAction.UseInAllCorners, ids)));
            MenuItem everywhere = MenuItem("Use Every_where", true, () => Request(BulbAction.UseEverywhere, ids));
            everywhere.ToolTip = TryFindResource("HL.Tip.BulbFactory.UseEverywhere");
            menu.Items.Add(everywhere);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(FavoriteItem(selection));
        menu.Items.Add(new Separator());
        if (one)
        {
            menu.Items.Add(MenuItem(first.Info.IsEditable ? "_Edit Bulb…" : "Edit Categor_ies…", true, () => Request(BulbAction.Edit, ids)));
            menu.Items.Add(MenuItem("Bulb _Credits", true, () => Request(BulbAction.Credits, ids)));
            if (addOn)
            {
                menu.Items.Add(MenuItem("E_xport Bulb File…", true, () => Request(BulbAction.Export, ids)));
                menu.Items.Add(MenuItem("Show in Fol_der", first.Info.FilePath is not null, () => Request(BulbAction.ShowInFolder, ids)));
                menu.Items.Add(MenuItem("Use as Screen Saver _Animation", true, () => Request(BulbAction.UseAsSaverAnimation, ids)));
            }
        }

        if (selection.All(i => i.Info.Origin != BulbOrigin.BuiltIn))
        {
            if (selection.All(i => i.IsRemoved))
            {
                menu.Items.Add(MenuItem("Re_store Bulb", true, () =>
                {
                    foreach (string id in ids)
                    {
                        attached?.Bulbs.Unhide(id);
                    }
                }));
            }
            else
            {
                menu.Items.Add(MenuItem("_Remove Bulb", true, () => Request(BulbAction.Remove, ids)));
            }
        }
    }

    /// <summary>"Add to Favorites" or "Remove from Favorites" (Ctrl+D).</summary>
    private MenuItem FavoriteItem(IReadOnlyList<BulbItem> selection)
    {
        bool add = selection.Any(i => !i.IsFavorite);
        MenuItem item = MenuItem(add ? "Add to _Favorites" : "Remove from _Favorites", true, () => ToggleFavorites(selection));
        item.InputGestureText = "Ctrl+D";
        return item;
    }

    /// <summary>"Show" with every Show filter item; the current one checked.</summary>
    private MenuItem ShowMenu()
    {
        var show = new MenuItem { Header = "S_how" };
        foreach (ShowOption? option in options)
        {
            if (option is null)
            {
                show.Items.Add(new Separator());
                continue;
            }

            string key = option.Key;
            MenuItem item = MenuItem(option.Text.Replace("_", "__", StringComparison.Ordinal), true, () => SelectOption(key));
            item.IsCheckable = false;
            item.IsChecked = key == currentOption.Key;
            show.Items.Add(item);
        }

        return show;
    }
}
