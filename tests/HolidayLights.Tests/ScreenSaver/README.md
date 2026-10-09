# tests/HolidayLights.Tests/ScreenSaver

Owner: screensaver. Namespace `HolidayLights.Tests.ScreenSaver`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.ScreenSaver"
```

Covers saver argument handling, the exit rules, simulation modules and physics (deterministic seeds), text and picture
layout, the saver art, the picture library, the music hand-off and CPU-rendered frames of whole scenes. Also: each
display's own seeds for the random patterns, the picture drawn as one layer of its visible part (panoramas, tiny tiles),
the Settings preview's enlarged corner (viewport drawing), "Preview Screen Saver" when the saver cannot start, and `/p`
ending when the owner of Windows' preview window goes away.

Two groups are skipped unless asked for; both close everything they open within seconds:

- `SaverVisualChecks` writes PNG frames of every animation, look and picture placement for looking at. Set
  `HOLIDAYLIGHTS_SAVER_FRAMES` to a folder.
- `SaverLiveChecks` shows the real saver on the real displays for a few seconds each (full screen, `/s` with its own
  music, "Preview Screen Saver", `/p` in a stand-in window, the Settings preview), checks the captures and the frame
  rate and reports CPU use. Set `HOLIDAYLIGHTS_LIVE_TESTS=1`, keep the displays on and don't touch the mouse or keyboard
  meanwhile.
  `HOLIDAYLIGHTS_SAVER_ANIMATION` (an animation value such as `Leaves`, `Balloons` or `bulb:<id>`) and
  `HOLIDAYLIGHTS_SAVER_PATTERN` (a `FlashPatternId` name) choose what the full-screen check shows; with
  `HOLIDAYLIGHTS_SAVER_FRAMES` set too, the captures are saved as PNGs.

Helpers (owner: contracts, `../Shared`): `TestPaths` (repository, golden and content paths), `GoldenData` (JSON and
`.json.gz` readers, golden RGBA hashes), `TempDataRoot` (a private data root, deleted afterwards), `StaThread`,
`InMemorySettingsStore`, `RecordingLog`, `TestHoldingFolder`.
