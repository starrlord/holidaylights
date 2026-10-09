# tests/HolidayLights.Tests/Layout

Owner: core-layout. Namespace `HolidayLights.Tests.Layout`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.Layout"
```

Covers the classic layout and "All Displays Together" (golden `layout/*.json.gz`; the reference PC cases of PRODUCT-SPEC 5.3.2 and 7.5 #7).

| File | What it checks |
|---|---|
| `GoldenLayoutTests` | Every golden layout case (14 themes x 5 work areas x 5 patterns): strips, thicknesses, counts, gaps and every placement, bit for bit |
| `ClassicLayoutTests` | Reference PC (48 px bulbs, 78 on top, 41 per side with gap 24/41), acceptance 7.5 #7 numbers, scaling of cells and spacings, type/flavor cycling, alignment, chase counter, ring order, unknown ids, empty inputs, argument checks |
| `AllDisplaysTogetherTests` | PO-1 wreaths: the reference PC (pieces split at x = 0, four corners, index and chase counter continuing across the seam, one clockwise ring), a lone display equal to Each Display, gaps up to 2 px, corner contact, L shapes and steps (concave corners, inset vertical pieces), mixed DPI, stacked displays, displays around a hole, and the invariants over random desktops in both modes |
| `LayoutAssertions` | The invariants: dense ordinals, strips, bulbs inside their strip and display, no overlaps, rings covering every bulb once, indices and chase counters continuous along each outline edge |
| `TableBulbs`, `GoldenLayouts` | Test doubles with the exact geometry of the 49 built-in bulbs (`assets/builtin/table.json`), synthetic bulbs, and the golden reader; also used by the flash tests |

Helpers (owner: contracts, `../Shared`): `TestPaths` (repository, golden and content paths), `GoldenData` (JSON and
`.json.gz` readers, golden RGBA hashes), `TempDataRoot` (a private data root, deleted afterwards), `StaThread`,
`InMemorySettingsStore`, `RecordingLog`, `TestHoldingFolder`.
