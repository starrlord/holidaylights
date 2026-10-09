# tests/HolidayLights.Tests/Rendering

Owner: rendering. Namespace `HolidayLights.Tests.Rendering`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.Rendering"
```

## Unit tests (always run)

* The presenter's pure parts with fakes (`Fakes/`): scene preparation and diffing, the animator (repeating periods, fades,
  waves, Dance envelopes, power-up), opacity curves, the clock, the fallback chain, z-order rules (including the repair of an
  on-top layer inserted after a taskbar that lost its topmost style, on a simulated z-order), the `NonRudeHWND` mark of
  top-level layer windows (one hidden window), host discovery, the pill and Identify pictures, the display-change diff on
  simulated snapshots (`TopologyChangeTests`), the half glow on displays in HDR (`AdvancedColorTests`).
* With the real Core (`RealCore/`, fixture `RealLights`: catalog with every bundled bulb, classic layout, flash engine,
  sprite provider, shipped themes): `RealEngineSceneTests` prepares the reference PC's scenes; `PatternEquivalenceTests`
  proves that the curves and frames the animator gives the visuals equal what the CPU previews sample
  (`IFlashSequencer.Sample`) for every pattern, with and without Smooth Fading, across speed changes and with music.

## Live probes (opt-in, `[Trait("Category", "Live")]`)

They show real lights on the real desktop for a few seconds and close themselves; nothing in Windows is changed.

| Variable | Effect |
|---|---|
| `HOLIDAYLIGHTS_LIVE_TESTS=1` | runs the live probes |
| `HOLIDAYLIGHTS_LIVE_WAKE_DISPLAYS=1` | probes that need the displays on (animations) first wake sleeping displays with a zero-pixel mouse nudge |
| `HOLIDAYLIGHTS_PROBE_OUT=<dir>` | where captures go (default `%TEMP%\HolidayLightsProbe`) |

| Probe | What it proves |
|---|---|
| `LiveThemeProbe` | Halloween, Christmas 1, Winter Wonderland and Holiday Party in the three layer modes on every display, plus the "All Displays Together" wreath: windows, z-order, click-through, every bulb pixel-exact at its Core layout position, the additive glow, the theme transition filmed along the top strips |
| `LivePatternProbe` (displays on) | every flash pattern and Dance with published notes plays on screen as an independent sequencer says, bulb by bulb |
| `LiveOverlayProbe` | the pill and Identify, pixel-exact where the spec puts them |
| `LiveRecoveryProbe` | a destroyed layer, `TaskbarCreated`, device loss (and WARP after two), a crash of the Lights thread, a vanished display, lights off and pauses (no commits) |
| `LiveTransitionsProbe` | power-up, theme transition, layer move, lights off/on, Slow Glow speed change, Dance subscribing only while it plays |
| `LiveLayerMoveProbe` | with invisible bulbs: two or three Bulb Drawing changes inside the 150 ms move fade end with every display shown in the last mode; on-top layers are not taken for a full-screen app (no `QUNS_BUSY`, the taskbars stay topmost) |
| `LivePerformanceProbe` | Lights-thread CPU per step and per second for the default theme, still, resting and Dance; first frame cold and warm |
| `LiveAnimationTiming` (displays on) | the DirectComposition animation contract the fades rely on |

While the displays are off DWM composes only when content changes and evaluates animations at their end, so the
animation probes need the displays on; the others work either way.

Helpers (owner: contracts, `../Shared`): `TestPaths`, `GoldenData`, `TempDataRoot`, `StaThread`, `InMemorySettingsStore`,
`RecordingLog`, `TestHoldingFolder`.
