# Holiday Lights 6: Architecture

Audience: the engineers (human or agent) working on the modern edition. Where to find the other specs:

- Product behavior: `docs/PRODUCT-SPEC.md`.
- Module ownership, contracts and build rules: `docs/CONTRACTS.md`.
- The exact behavior of the original Holiday Lights 5.4 is restated in this document, in PRODUCT-SPEC, in the code
  comments and in the golden test data (`tests/HolidayLights.Tests/Golden`, see its README). The analysis notes and
  platform prototypes they were written from are not part of this repository.

This document fixes the technical structure, threading model, rendering pipeline and module contracts.

## 1. Verified platform facts (target machine: Windows 11 26H2 build 26300, two 3840×2160 @150%, second at x = −3840)

| Fact | Evidence |
|---|---|
| The original (DPI-unaware) computes 2560×1392 logical frames, but GDI draws physically: the frame lands at x −1280..1280 across both monitors, additively blended into the layered icon view | Measured with 5.4 on the reference PC |
| **DirectComposition** content in a child window of Progman, z-ordered below `SHELLDLL_DefView`, renders **between the wallpaper and the icons** with exact per-pixel alpha. ULW child windows are invisible there (Progman has no redirection surface). | Desktop-layer prototype on the reference PC, smoke test with real art |
| Top-level windows `WS_EX_NOREDIRECTIONBITMAP\|LAYERED\|TRANSPARENT\|TOOLWINDOW\|NOACTIVATE[\|TOPMOST]` + DComp render exactly and are click-through. When owned by Progman at `HWND_BOTTOM` they sit above the icons and below all apps. | Desktop-layer prototype |
| Premultiplied pixels with rgb > a are composed additively (`out = src + dst·(1−a)`) in every mode, so glow adds light | Desktop-layer prototype (pixel readback) |
| DComp animation by `SetContent` swaps + one `Commit` costs ≈0.014 ms per frame for 40 bulbs | Prototype timing |
| WinMM: GS Wavetable Synth = device 0, `midiOutOpen(MIDI_MAPPER)` works (only `GetDevCaps(MAPPER)` fails); a high-resolution waitable timer gives 0.06 ms mean lateness | Audio prototype |
| WPF `ThemeMode="System"` gives Fluent + Mica + a dark title bar automatically on .NET 10 | WPF prototype |
| MMPX (MIT) magnification, verified bit-exact against the paper | Upscaling prototype (references in `tests/HolidayLights.Tests/Sprites/Golden/upscale`) |

## 2. Solution layout

```
HolidayLights.sln
Directory.Build.props    shared settings; HL_BUILD_ROOT env var redirects obj/bin (parallel builds)
global.json              SDK 10.0.400+ (latestFeature)
src/
  HolidayLights.Core/       net10.0          pure logic: bulb model + decoders (.bul/GIF/BMP), built-in table,
                                             catalog, layout, flash patterns, timing, settings model, themes,
                                             seasons, legacy-settings mapping, CPU sprite pipeline (MMPX, scaling,
                                             glow baking, premultiplied compositor)
  HolidayLights.Platform/   net10.0-windows  Win32 interop + OS services: monitors/DPI, desktop shell hosts
                                             (Progman/WorkerW/DefView), hot keys, single instance + pipe, startup,
                                             file association, pause signals (fullscreen/lock/display/energy
                                             saver), screen saver registration, registry reader (legacy import),
                                             wallpaper info
  HolidayLights.Rendering/  net10.0-windows  DirectComposition presenter (Vortice): layer windows per monitor in
                                             the 3 modes, sprite surfaces, visuals, the "Lights" render thread,
                                             device-lost recovery, topology maintenance
  HolidayLights.Audio/      net10.0-windows  MIDI file parser + sequencer over winmm, audio-file playback (NAudio)
                                             + beat detector, MusicDirector (play modes, shuffle bag), note events
  HolidayLights.App/        net10.0-windows  WPF app HolidayLights.exe: entry point, tray (H.NotifyIcon), settings
                                             window and pages, gallery, live preview, first run, about/help, screen
                                             saver mode, Bulb Factory
tests/HolidayLights.Tests/  xUnit
assets/                     checked-in data embedded in Core: builtin/ (49 built-in bulbs: original BMPs + table
                            JSON), themes/ (shipped themes JSON), heritage/ (5.4 art); icons/ (branding generator)
content/                    bundled content copied next to the exe as Content\: Bulbs/ (1,501 .bul), Music/ (46 .mid),
                            Pictures/ (11 .BMP); provenance and credits in content/README.md
docs/                       ARCHITECTURE.md, CONTRACTS.md, PRODUCT-SPEC.md, USER-GUIDE.md, RELEASING.md
tools/publish/              the release script (publish.ps1)
tools/ci/                   the scripts of the GitHub Actions workflows (tests, version check, release files)
.github/workflows/          CI (build and test every change) and Release (tag v* -> GitHub release); docs/RELEASING.md
```

Project references: `Core ← (none)`, `Platform ← (none)`, `Audio ← (none)`, `Rendering ← Core, Platform`,
`App ← all`, `Tests ← all`. Core and Audio never reference WPF types (Audio uses no WPF at all; NAudio plays files).
Rendering never references WPF.

NuGet packages (versions verified by the platform PoCs):

| Package | Version | Used by |
|---|---|---|
| Vortice.Direct3D11, Vortice.DirectComposition (+ Vortice.DXGI, transitively) | 3.8.3 | Rendering |
| NAudio | 3.1.0 | Audio |
| H.NotifyIcon.Wpf | 2.4.1 | App |
| VirtualizingWrapPanel | 2.5.4 | App |
| xunit, Microsoft.NET.Test.Sdk, xunit.runner.visualstudio, coverlet.collector | (template) | Tests |

All of these are MIT. No GPL/LGPL code anywhere (xBRZ and hqx are excluded).

Parallel builds: `export HL_BUILD_ROOT="$TEMP/hl-build/<name>"` before `dotnet build`.

## 3. Processes and threads

Single instance:

- Mutex `Local\HolidayLights6.Instance`, plus a per-user named pipe `HolidayLights6.<user SID>` for commands from
  later launches: `show-settings [page]`, `toggle-lights`, `toggle-layer`, `open <file>`, `exit`, `saver-started`,
  `saver-stopped`.
- Screen saver launches (`/s`, `/p <hwnd>`, `/c[:hwnd]`) never take the mutex. They render themselves and notify a
  running instance (music "only during screen saver" modes).

| Thread | Owns | Rules |
|---|---|---|
| UI (WPF Dispatcher, STA) | `AppState`, `SettingsStore`, tray icon, windows, theme/season scheduler, pause-signal aggregation | Only thread that mutates settings. Builds immutable `LightsScene` snapshots. |
| Lights (dedicated STA, `MsgWaitForMultipleObjectsEx` + high-resolution waitable timer) | All layer windows, the D3D11/DComp device, sprite surfaces, visuals, a hidden top-level message window (display/DPI/work-area/TaskbarCreated messages) | Never blocks (a cross-process child attaches its input queue to Explorer's). Receives scenes + commands through a lock-free queue. |
| Music (above-normal priority) | MIDI sequencer + winmm handle; NAudio playback | Note events go into a lock-free ring buffer that the Lights thread reads (music sync). |
| ThreadPool | catalog scan, decode, sprite preparation (MMPX/scale/glow), thumbnails | Hands results to the Lights/UI threads. |

## 4. Core model (`HolidayLights.Core`)

### 4.1 Pixels and images
- `Rgba32Image` (width, height, `uint[]` BGRA **straight** alpha). Decoders produce it.
- `PremultipliedImage` (same, premultiplied) is what the renderers consume.
- `BmpDecoder`: 1/4/8/24/32 bpp uncompressed, as used by the RT_BITMAPs and the screen saver pictures.
- `GifDecoder` has two modes:
  - **Classic5_4**: a port of the original 5.4 GIF decoder (holes, LCT persistence, recolouring). It must match the
    golden frames of all 1,501 bundled bulbs (`bul-frames.json.gz`) bit-exactly.
  - **Standard**: a spec-correct decoder, used for importing GIFs in the Bulb Factory and for animation frames whose
    delays matter.

### 4.2 Bulbs
```
Side   { Top, Right, Bottom, Left }                         // original side slots 0..3 (clockwise)
Corner { TopLeft, TopRight, BottomRight, BottomLeft }       // original slots 4..7 (clockwise)
BulbDefinition
  Id                 string   "builtin:<slug>" | "addon:<file stem>" | "user:<file stem>"   (stable key in settings)
  LegacyId           int      original numeric id (built-in 0..48; .bul header 0x0C), used only by the legacy importer
  Name, Description, Copyright, Author            (decoded Windows-1252)
  Categories         string[]  (built-in: table +0x378 or user override; add-on: "categ:" blob)
  Origin             BuiltIn | BundledAddOn | UserAddOn (+ file path)
  FlavorCount(side), PhaseCount(side), Spacing(side)    // built-in table hSpacing/vSpacing; add-on 0
  GetCell(side|corner|preview, flavor, phase) -> BulbCell { Rgba32Image image; ... }   (lazy, cached)
  IsShapeStatic(side)                                   // add-on "slow bulb" information (informational only)
```
- `BuiltInBulbs`:
  - Loads `assets/builtin/table.json` (an exact dump of the 5.4 program's table of 49 × 0x3C8-byte records, strings decoded as
    **Windows-1252**) and the original RT_BITMAP art/mask BMPs (embedded resources).
  - Implements `GetCellRect`/`CellIndexToRect` exactly as 5.4 (golden `builtin-cells.json`). Mask set bit = opaque.
- `BulFile`: header/slot table/GIF entries/categories as 5.4 stores them. It reads leniently; a "damaged"
  bulb is reported, not thrown.
- `BulbCatalog`:
  - Sources: built-ins, then bundled add-ons (`<app>\Content\Bulbs`), then user add-ons
    (`Documents\Holiday Lights\Bulbs`, per PRODUCT-SPEC D24; user songs and pictures go in
    `Documents\Holiday Lights\Music` and `\Pictures`). Settings and themes stay in `%APPDATA%\Holiday Lights`.
    `HOLIDAYLIGHTS_DATA_ROOT` relocates all of these for tests (PRODUCT-SPEC PO-3).
  - Metadata is indexed on a background thread (header only; GIFs decode lazily). It also handles search,
    categories (incl. user overrides for built-ins), favorites and "recently used".
  - Duplicate legacy ids are resolved like the original (increment until free) but only matter for import.

### 4.3 Layout (the exact 5.4 layout; golden `Golden/layout`)
- `ClassicLayout.Build(RectI area, SlotAssignment slots, Func<...> cellSize, scale)` reproduces the 5.4
  layout exactly at scale 1:
  - build order Top, Bottom, Right, Left;
  - corners on Top/Bottom only;
  - the `ids[i % n]` / flavour `i / n` rule;
  - the double gap with truncation;
  - alignment, and the 16-bit quirks where they matter.
  - Output: `BulbPlacement[]` (strip, index, bulb, flavor, x, y, w, h, chaseIndex, isCorner).
- Scaling: layout runs in *art pixels* with all cell sizes and spacings multiplied by the monitor scale `s` (the
  sprite pipeline produces exactly `round(w·s)` sizes). The area is the monitor's work area (or monitor rect, per
  setting) in physical pixels.
- `DesktopFrames`: per-display mode (one area per monitor), and the optional "whole desktop outline" mode if
  PRODUCT-SPEC asks for it (union outline; exposed edge segments only; corners on convex corners only).

### 4.4 Flash patterns and timing
- `FlashClock`: 60 ms tick, interval 1..9 ticks (Don't Flash: 10). A global step counter is shared by all strips;
  each strip shows `step % frames`.
- `ClassicPatterns`:
  - Don't Flash, Flash Together, Alternating, Bulb Chase, Random Flashing, exactly as 5.4 (chase
    counter skipping single-phase bulbs; Random = 8 pre-rolled frames per strip at build time, MSVC LCG
    `seed*214013+2531011`).
  - `IFlashPattern.Evaluate(stepIndex, StripState, Span<BulbVisualState>)` is a pure function.
- New patterns, smooth fading and glow per PRODUCT-SPEC. Smoothing is a renderer-side opacity transition between
  phases (lit ↔ unlit crossfade for 2-phase light bulbs; hard swap for multi-frame animations unless the spec says
  otherwise).
- Music sync: an optional modulation input (note events → per-bulb brightness boosts), defined in PRODUCT-SPEC.

### 4.5 CPU sprite pipeline (shared by previews and the live renderer)
- `Mmpx.Scale2x(Rgba32Image)`: exact port of the upscaling prototype (reference images in
  `tests/HolidayLights.Tests/Sprites/Golden/upscale`).
- `SpriteScaler`:
  - Smooth: MMPX ×2 (×4) to ≥ the target size, then area-average down in premultiplied space.
  - Crisp: nearest-neighbour to the integer factor, then area-average for fractional scales.
  - Optional alpha threshold.
- `GlowBaker`: blurred, colour-weighted halo per lit sprite, stored as additive premultiplied (a = 0, rgb = light).
- `CpuCompositor`: premultiplied source-over and additive blits into a BGRA buffer (SIMD where easy). It renders
  `LightsScene` previews, thumbnails and the `--render-test` PNG.

### 4.6 Settings, themes, seasons
- `AppSettings` (System.Text.Json source-generated): `%APPDATA%\Holiday Lights\settings.json`, atomic save, `Version`
  + migrations. Bulbs are referenced by stable string ids.
- `ThemeDefinition`: the 13 theme values of the original (bulb arrangement, flash pattern + interval, enabled songs +
  play mode, all screen saver values) plus any new fields from PRODUCT-SPEC.
  - Built-in themes ship in `assets/themes/`: the 11 installer themes of 5.4 (golden `default-themes.json`),
    translated to string ids, plus new ones.
  - User themes live in `%APPDATA%\Holiday Lights\Themes\*.json`.
- `SeasonCalendar`: Easter (computus), US Thanksgiving, Hanukkah and other holidays that PRODUCT-SPEC needs.
- `LegacySettingsMapper`: decoded registry values → `AppSettings` + themes. The registry read itself is in Platform;
  golden `legacy-registry.json` is a decoded 5.4 registry.

## 5. Rendering (`HolidayLights.Rendering`, DirectComposition)

- **Device**: `D3D11CreateDevice(Hardware, BgraSupport)` → `IDXGIDevice` → DComp device (v1, or desktop device if
  per-visual opacity animation is used). Check `CheckDeviceState` on failure and on display change; recreate
  everything on loss.
- **Layer windows, one per monitor** (physical monitor rect or work area; DPI from `GetDpiForMonitor`):

  | Mode | Window |
  |---|---|
  | BehindIcons (default "on the desktop") | child of Progman (raised desktop, 24H2+) or the wallpaper WorkerW (classic), `WS_EX_NOREDIRECTIONBITMAP\|WS_EX_NOACTIVATE`, `WS_CHILD\|WS_CLIPSIBLINGS`, placed with `SetWindowPos(h, defView)`. Before locating, send `0x052C` (wParam 0xD, lParam 1, **never lParam 0**). Re-assert z-order and the hosts every ~2 s (`Maintain`). |
  | AboveIcons | top-level `WS_POPUP`, `WS_EX_NOREDIRECTIONBITMAP\|LAYERED\|TRANSPARENT\|TOOLWINDOW\|NOACTIVATE`, owner = Progman, `HWND_BOTTOM`, `DWMWA_EXCLUDED_FROM_PEEK`. |
  | OnTop | same as AboveIcons but `WS_EX_TOPMOST` and no owner. |

  Fallback chain: BehindIcons → AboveIcons → OnTop. The effective mode is reported to the UI.
- **Sprites**:
  - Every unique (bulb, cell, scale, style) frame is uploaded once to an `IDCompositionSurface` (premultiplied BGRA),
    with its optional additive glow surface.
  - Each placed bulb gets a visual (offset = placement), plus a child glow visual and, for crossfades, a second
    sprite visual whose opacity is animated.
  - Flash steps call `SetContent` / opacity and then one `Commit`.
- **Frame loop**:
  - The waitable timer fires on the 60 ms tick grid. While smoothing animations run, it fires at 16 ms, or uses
    DComp animations so no ticks are needed.
  - The loop must cost ~0 CPU when idle ("Don't Flash" + no smoothing).
- **Pause**: hide per monitor (fullscreen app on that monitor) or globally (lock, display off, energy saver,
  presentation mode) per the PRODUCT-SPEC settings. While hidden, stop the clock.
- **Topology**: `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WM_SETTINGCHANGE(SPI_SETWORKAREA)`, `TaskbarCreated` and the 2 s
  poll trigger a debounced (300 ms) diff of `{device, rcMonitor, rcWork, dpi}`. Changes rebuild the layout,
  recreate windows and re-own them to the new Progman.

## 6. Audio (`HolidayLights.Audio`)

- **`MidiFile`**: SMF 0/1, running status, tempo map, SysEx. It must parse all 46 shipped songs (+ reset.mid).
- **`MidiSequencer`**:
  - Paced by a high-resolution waitable timer.
  - Device: the configured device name, else the first "GS Wavetable", else device 0, else MIDI_MAPPER.
  - Sends a GS reset / CC121 on all channels before each song (replacing the 2 s reset.mid) and all-notes-off on stop.
  - Volume = CC7 scaling.
  - Note-on events go to listeners.
- **Audio files**: NAudio `AudioFileReader` → volume → `WaveOutEvent`, with an energy/beat detector feeding the same
  event stream.
- **`MusicDirector`** implements the 5.4 Music Box exactly (golden `shuffle.json`):
  - modes Never / Only when the screen saver is on / Only when it is off / Always (1 s gap) / Intermittently
    (60–180 s gap);
  - the shuffle bag with no immediate repeat.
  It also keeps enabled/disabled state and the song list (bundled + user folder).

## 7. App (`HolidayLights.App`, WPF)

- **Custom `Main`**: parses screen saver arguments first, then handles single instance, then starts WPF.
  `ShutdownMode = OnExplicitShutdown`.
- **Tray**: `H.NotifyIcon.Wpf` TaskbarIcon with a Fluent `ContextMenu`; re-added on `TaskbarCreated`.
- **Settings window**:
  - `ThemeMode="System"`, left navigation, pages per PRODUCT-SPEC.
  - Changes apply live; Cancel/Close semantics per spec.
  - Gallery: `VirtualizingWrapPanel` with one shared animation clock for realized items.
  - The live preview renders via `CpuCompositor` into a `WriteableBitmap`.
- **Screen saver**: full-screen WPF window per monitor (`/s`), child of the preview HWND (`/p`), settings page
  (`/c`). Modules and movement styles as in 5.4, per PRODUCT-SPEC.
- **Bulb Factory**: GIF/PNG import (Standard decoder), editor per spec, and a `.bul` writer compatible with 5.4
  (header + embedded GIFs).
- **Help**: an in-app user guide (rewritten from the 5.4 help file); tooltips from the 57
  "What's This?" popups.

## 8. Testing and verification

- **Golden data** (`tests/HolidayLights.Tests/Golden`, derived from Holiday Lights 5.4 by reference tools that are not
  part of this repository; see its README):
  - per-frame RGBA hashes for every built-in cell and every add-on frame (49 + 1,501 bulbs);
  - layout JSON for a matrix of themes × work areas × patterns;
  - MSVC-LCG random-pattern tables;
  - shuffle-bag sequences;
  - registry-import fixtures (a decoded 5.4 registry).
- **xUnit** (bit-exact equality with the goldens unless stated otherwise):
  - decoders, built-in slicing and layout;
  - patterns: exact for the classic ones;
  - the 46 MIDI files: event counts and durations;
  - MMPX against the paper's reference outputs;
  - settings round-trip and legacy import.
- **Live verification on the real machine**:
  - `HolidayLights.exe --render-test <dir>` writes one PNG per monitor of the current scene (CPU compositor)
    without creating windows;
  - screen captures (BitBlt `SRCCOPY|CAPTUREBLT`) of the real layers in all three modes on both monitors.
