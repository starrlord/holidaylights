# tests/HolidayLights.Tests/Ui

Owner: settings-ui. Namespace `HolidayLights.Tests.Ui`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.Ui"
```

Covers the Settings window (use `StaThread` for WPF objects):

| File | What it checks |
|---|---|
| `UndoHistoryTests`, `SettingsMergeTests` | Undo/Redo, coalescing, groups, Undo All, the Cancel scope. |
| `ArrangementEditsTests` | The frame editor's rules (use, add, insert, replace, transfer, reorder, remove, clear). |
| `UiTextTests` | Sentences and small rules: result lines, empty states, import summaries, status lines, greetings, calendar texts, hot key messages, colours, window placement, High Contrast overrides. |
| `BulbFactoryPageTests` | Acceptance scenarios 5, 6 and 9 in a real window; the frame editor's keyboard model and screen-reader names; the Bulb List's filter, search and Restore All; opening a bulb file that is already present; refused drops. |
| `SettingsPagesTests` | Home's theme cards, loading themes, the music switch, Cancel (scenario 8), the screen saver status card and styles, General's displays and Look presets, and the status lines that follow the lights, the music, the displays and the hot keys. |
| `LightStripTests` | Every bulb shows in its Bulb List tile, however wide its art. |
| `SettingsSnapshotTests`, `DialogSnapshotTests` | The visual harness: every page, dialog and overlay rendered off-screen at 150 % in the light and the dark theme. |

Set `HL_UI_SNAPSHOTS=<folder>` to make the harness write its PNG files; without it the harness renders the light theme
as a smoke test. Windows are placed off-screen, never activated, and closed at once.

The classes that build WPF windows, pages or resources belong to `WpfCollection` and run one at a time, never beside
other tests: WPF's XAML loader shares a process-wide schema context that is not safe for several UI threads at once.

Fakes (`Fakes/`): `UiTestServices` (an `IAppServices` with the real catalog, layout, flash, sprites, themes and seasons,
and fakes for the platform, shell and media), `UiHarness` (Fluent resources, pumping, rendering).

Helpers (owner: contracts, `../Shared`): `TestPaths`, `GoldenData`, `TempDataRoot`, `StaThread`, `InMemorySettingsStore`,
`RecordingLog`, `TestHoldingFolder`.
