# tests/HolidayLights.Tests/Flash

Owner: core-layout. Namespace `HolidayLights.Tests.Flash`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.Flash"
```

Covers the classic patterns (golden phases in `layout/*.json.gz`, `msvc-rand.json`), the new patterns, fades, flash limit, Dance.

| File | What it checks |
|---|---|
| `GoldenPatternTests` | The phases of every placement of every golden case in world frames 0..7 (Random Flashing with `srand(1)`), the strip frame counts W, and the MSVC `rand()` sequences |
| `ClassicPatternTests` | The kept 5.4 quirks (Alternating next to 4-phase bulbs, Bulb Chase direction, corners ignoring the pattern, Random repeating every 8 steps), light-bulb states, Don't Flash on frame 0 like 5.4 (also add-ons lit on their second frame) and Stop Flashing lit, Alternating and Bulb Chase running on across the All Displays Together seam, classic patterns ignoring music |
| `FlashClockTests` | 60 ms tick, interval and Don't Flash (10 ticks), the flash limit interval, strip frames |
| `FadeAndSampleTests` | Fade durations per pattern, forced fading under the limit, linear ramps in `Sample`, speed changes, jumping equals stepping for every pattern |
| `TwinkleTests` | Determinism, the 0.8 start / 0.806 steady lit share, transition rates, animation advances, no repeats, direct evaluation across restarts, cheap far jumps, the limit, per-display seeds (`DisplaySeeds`: identical displays never twinkle in lockstep, also in Combination; display 1 and the one wreath keep their exact chain) |
| `NewPatternTests` | Slow Glow (wave, 4.8 s breath, stepped without fading), Chase Around (one in three, clockwise), Waves (offsets, clockwise travel), Combination (schedule, restarts) |
| `DanceTests` | Groups, idle Slow Glow, song start, melody and drum rules, decay, raises, coalescing, the 333 ms limit, audio beats, beat frames and the 2 s rule, stop/pause/resume, no-fading lit/dark |
| `SequencerTests` | Per-bulb facts, ring indices per display, add-on lit frames, unresolvable bulbs, argument checks |
| `PerformanceTests` | 2,000 bulbs per step under 0.2 ms for every pattern, `Sample` speed, no allocations in `MoveTo`/`Sample` |
| `FlashTestKit` | Shared layouts and helpers |

Helpers (owner: contracts, `../Shared`): `TestPaths` (repository, golden and content paths), `GoldenData` (JSON and
`.json.gz` readers, golden RGBA hashes), `TempDataRoot` (a private data root, deleted afterwards), `StaThread`,
`InMemorySettingsStore`, `RecordingLog`, `TestHoldingFolder`.
