# tests/HolidayLights.Tests/Golden - reference data

Read-only reference data that the tests compare Holiday Lights 6 against (docs/ARCHITECTURE.md section 8). Every
file was derived from Holiday Lights 5.4 (its program, its bundled bulbs and songs, and the registry its installer
writes) by reference tools that are not part of this repository; the files are kept here so the tests run anywhere.
Do not edit them by hand. The folder is copied to the test output; read a file with `TestPaths.Golden(...)` or
`GoldenData.ReadJson(...)` (`.json` and `.json.gz`).

| File | Content |
|---|---|
| `builtin-cells.json` | Rectangle and RGBA SHA-256 of every cell the 49 built-in bulbs use (890 cells, 3,133 slot references) |
| `bul-frames.json.gz` | Header fields, slot tables and per-frame RGBA SHA-256 of all 1,501 bundled add-on bulbs (4,511 GIF entries, 18,388 frames), decoded the 5.4 way |
| `layout/*.json.gz` | The 5.4 layout of 14 themes x 5 work areas x 5 flash patterns: strips, gaps and every placement with its phases |
| `layout/themes/*.json` | The 11 installer themes in the layout model's theme format (per side 6 bulb ids + 2 corner ids, legacy ids) |
| `msvc-rand.json` | The first 2,000 MSVC `rand()` values for the seeds 0, 1, 42, 12345 and 2147483647 |
| `midi.json` | Structure, event counts, tempo map and durations of the 47 `.mid` files of 5.4 (46 bundled songs + `reset.mid`) |
| `legacy-registry.json` | A decoded 5.4 registry (`HKCU\Software\Tiger Technologies\Holiday Lights`, themes, categories, HKLM views, file association, screen saver), with machine-specific paths replaced by neutral ones |
| `default-themes.json` | The 11 themes the 5.4 installer writes, raw and decoded |
| `shuffle.json` | Sequences and scenarios of the 5.4 shuffle bag |
| `screensaver-animations.json` | The 21 picture animations of the 5.4 screen saver with the cells and rectangles of their built-in bulb records |

## Conventions

* **RGBA hashes**: lowercase hex SHA-256 over `width * height * 4` bytes; rows top to bottom, pixels left to right,
  bytes **R, G, B, A**. Alpha is binary: 255 for an opaque pixel with its colour, 0 for a transparent pixel with RGB
  forced to 0, 0, 0. `Rgba32Image` stores BGRA `uint`s with straight alpha: convert it to this byte order (and zero
  the RGB of transparent pixels) before hashing (`GoldenData.RgbaSha256`).
* **Coordinates**: origin top-left, x to the right, y down. Rectangles are `[x0, y0, x1, y1]` with x1/y1 exclusive.
  Cell rectangles are in art-bitmap pixels; layout coordinates are pixels of the work area at scale 1.
* **Strings**: decoded from Windows-1252 and stored as JSON strings (UTF-8 files). "©" is U+00A9.
* **Bulb ids**: legacy numeric ids are the registry "Bulb Settings" values (built-ins 0..48; the table index is not
  the id). `assets/builtin/table.json` maps them to slugs; the stable string id is `builtin:<slug>`.
* **Sides and corners**: sides 0..3 = top, right, bottom, left; corners 4..7 = top-left, top-right, bottom-right,
  bottom-left; slot 8 is the bulb-list preview.
* Every file is deterministic: no timestamps, fixed list order, gzip without file name and with mtime 0.

## Notes per file

* **builtin-cells.json**: alpha comes from the mask (black = opaque); RGB is the art colour where opaque. The "On
  Desktop" fringe and the On-Top clipping are not baked in.
* **bul-frames.json.gz**: one record per `.bul` file, sorted by name: file size and SHA-256, header fields, slots
  (raw entry indices, -1 = unused), loader errors (none), and per GIF entry its size, frame count, the recomputed and
  the stored `constantMask` flag and one hash per frame. The frames follow the 5.4 decoder (holes where a frame uses
  the transparent index, persistent local colour tables, GDI nearest-colour re-mapping, stop at a stray byte).
  `pillowSame` / `pillow` record a standard GIF interpretation for comparison only; it is not normative.
* **layout/*.json.gz** (`holidaylights.golden.layout/1`): one file per theme (`standard`, `default`, the 11
  `installer-*` themes and `mixed-example`), 25 cases each: the work areas `0,0,1920,1032`, `0,0,1280,984`,
  `0,0,2560,1392`, `0,0,3840,2088` and `0,0,1024,738` with patterns 0..4 (pattern 4 with seed 1). Strips are in build
  order top, bottom, right, left; `placements` are rows of `slot, index, bulbId, flavor, x, y, w, h, chaseIndex,
  phases`, corners first; `gap` is `null` for +infinity. Pattern 0 shows phase 0, pattern 1 `f % N`, pattern 2
  `(f + (k odd ? W/2 : 0)) % N`, pattern 3 `(f + k) % N`; pattern 4 uses MSVC `rand()` after `srand(1)` in strip order,
  bulb by bulb, frames 0..7; corners always show `f % N`.
* **msvc-rand.json**: `srand(s)`: state = s; `rand()`: state = state * 214013 + 2531011 (mod 2^32), return
  (state >> 16) & 0x7FFF.
* **midi.json**: one record per file, sorted by name, including `reset.mid` (`tests/HolidayLights.Tests/Fixtures/Music`):
  format, track counts, division, tempo changes, `durationMicroseconds` (500,000 us per quarter note until the first
  Set Tempo, up to the last End of Track, rounded down), the same up to the last event, and per-track event counts
  (note-on with velocity 0 counts as note-off; F0 and F7 are SysEx).
* **legacy-registry.json**: values with their type, raw value and decoded value. The 5.4 program folder is shown as
  `C:\PROGRA~1\HOLIDA~1` and the Startup folder as `%APPDATA%\...`; the tests never read the folder from the file.
* **shuffle.json**: the bag starts with every song unplayed and a private MSVC `rand()` after `srand(seed)`; every
  pick consumes exactly one `rand()`; a new round excludes the previous song unless it is the only eligible one.
  `sequences[]` holds the first 100 picks for 1, 2, 3, 15 and 46 songs and the seeds 1 and 42; `scenarios[]` covers
  rescans, disabled songs, a single eligible song and no eligible song.
* **screensaver-animations.json**: per animation its kind (`gif`, `includedBulbs`, ...) and, for built-in bulb
  animations, the records with bulb id, cell numbers and the cell rectangles verified by rendering every record.
