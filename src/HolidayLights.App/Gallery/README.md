# Gallery

Owner: settings-ui. The Bulb List of Bulb Factory and of the Choose a Bulb dialog (PRODUCT-SPEC 3.2.5, 3.2.6, 3.8.4).

| File | What it is |
|---|---|
| `BulbList.xaml(.cs)`, `BulbList.Menus.cs` | Toolbar (search, Add Bulb..., Show, Sort, Tiles/Details, "..."), result line, `vwp:GridView` tiles (VirtualizingWrapPanel 2.5.4) and Details rows, empty states, try-on, drag source, context menu. |
| `BulbItem.cs` | One bulb as listed: badges, facts, credits, accessible name. |
| `BulbShowOptions.cs` | The "Show:" items with live counts, the result line and the empty-state sentences. |
| `SelectedBulbBar.xaml(.cs)` | The selected-bulb bar under the list. |
| `AddToFlyout.cs` | The "Add To..." flyout: a miniature of the frame. |
| `BulbAction.cs` | The commands the list and the bar ask the page to carry out. |

Tile art animates on the shared UI clock (`Preview/BulbAnimationClock`) for realized tiles only (3.0.3).
