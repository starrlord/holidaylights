# tests/HolidayLights.Tests/Bulbs

Owner: core-bulbs. Namespace `HolidayLights.Tests.Bulbs`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.Bulbs"
```

| File | Covers |
|---|---|
| `BuiltInBulbsTests` | the 49 built-ins: table order, record ids, every referenced cell (3,133 slot references) against `builtin-cells.json` (rect and RGBA hash), flavor/phase wrapping, kinds, metadata |
| `BundledBulbGoldenTests` | all 1,501 bundled `.bul` files against `bul-frames.json.gz`: header fields, slot tables, categories, and every frame of all 4,511 GIF entries (18,388 frames) bit for bit, constant-mask flags |
| `GifDecoderTests` | 5.4 quirks versus standard compositing on small synthetic GIFs (holes, persistent colour tables, recolouring, disposal, persistent control blocks, delays, interlace, stray bytes, errors); the standard decoder against Pillow on every bundled GIF, with the five differences that are Pillow's own conventions |
| `BmpDecoderTests` | the 11 shipped pictures (second bitmap, Pillow hashes), masked art with a short mask (Sun), 1/4/8/16/24/32 bpp, top-down, RLE4/RLE8, bit fields, core headers, truncation, rejection |
| `HeritageArtTests` | the decoded `assets/heritage` art against Pillow hashes |
| `BulFileTests` | header fields, Windows-1252 strings, stale tails and run-on strings, categories, slot lookups, every 5.4 loader rule, content identity |
| `AddOnBulbTests` | cells of bundled bulbs against the golden, light-bulb classification, preview windows, WARNING placeholders for damaged animations, editability, thread safety |
| `BulbCatalog*Tests` | indexing (order, cache, damaged files, early resolution), queries (filters, search ranking, sorts, categories), files (import of `.bul` and `.gif`, reload, remove/restore, the My Bulbs watcher, content lookup, fresh ids), preferences (favorites, hidden bulbs, overrides, undo through settings, recently used) |
| `BulbInternalsTests` | Windows-1252, the 5.4 name order, search folding and ranking, category merging, the decoded-art LRU, the light-bulb rule |
| `MalformedInputTests` | corrupted and truncated bulb files, GIFs and BMPs: reported as damaged, never an unexpected exception |
| `BulbPerformanceTests` | indexing all 1,501 bundled bulbs (cold < 1.5 s, from the cache < 1 s) and decoding a typical bulb (< 20 ms); run without parallel tests |

Helpers: `CatalogHarness` (a catalog over a private data root with a chosen set of bundled bulbs, a fixed clock, a fake
GIF writer and recorded events), `TestGif` (writes small GIFs), `TestBul` (writes `.bul` files), `TestBulbs`,
`BulbGoldens`. Shared helpers (owner: contracts, `../Shared`): `TestPaths`, `GoldenData`, `TempDataRoot`,
`InMemorySettingsStore`, `RecordingLog`, `TestHoldingFolder`.
