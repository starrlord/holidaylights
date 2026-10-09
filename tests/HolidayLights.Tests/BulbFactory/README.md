# tests/HolidayLights.Tests/BulbFactory

Owner: bulb-factory. Namespace `HolidayLights.Tests.BulbFactory`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.BulbFactory"
```

| File | Covers |
|---|---|
| `BulFileWriterRoundTripTests` | All 1,501 bundled bulbs: read with the core-bulbs `BulFile` reader, opened as a `BulbDocument`, written by `BulFileWriter`, read again. Content identity, categories, every slot's GIF bytes, flags (locked 0, unsent 0, status 0), dense entries with correct CRC-32/BZIP2, use counts and `constantMask` (against the golden `bul-frames.json.gz`), no region caches or sender, GIFs verbatim in entry order, `categ:` last; writing twice gives the same bytes. |
| `BulFileWriterTests` | The 0x57C header layout byte by byte, text cutting and "?" replacement, categories 5.4 can load, required name and slots, atomic writes. |
| `BulTextAndCrcTests` | CRC-32/BZIP2 check value and a bitwise reference, Windows-1252 encoding, New Category name rules, the 255-character `categ:` limit. |
| `BulbDocumentTests` | GIF de-duplication, 37 slots and 37 entries, Remove Flavor gaps packed on save, Copy to All Sides, the list-preview window (centring, clamping), clones and content equality, loading files (internal categories, flavors after -1, empty entries, damaged files). |
| `GifBulbFactoryTests` | The 5.4 defaults of a bulb made from a GIF, 5.4 file naming (`Star.bul`, `Star 1.bul` ... `Star 999.bul`), and the catalog's GIF import through the factory. |
| `GifEncoderTests` | GIFs written from PNG frames decode identically with the faithful decoder, the standard decoder and Windows Imaging (including LZW table resets); colour reduction above 255 colours; 1-bit alpha. |
| `PictureImportTests` | "Change..." file rules: Explorer order of PNG frames, size limits, damaged pictures, oversized GIFs. |
| `AnimationFramesTests` | The Animation Frames group: 5.4 slot names and texts, flavor marks, Change / Remove Flavor / Copy to All Sides with the dialog's Undo and Redo, the white square, frame stepping, zoom fitting, the GIF tip, the slot map and the edge sample. |
| `BulbEditingSaveTests` | Save (file replaced under the same name, previous file held, one Undo step that swaps the versions), replaced characters, the required name, "Discard Changes?", categories and their stored order, save failures. |
| `BulbEditingRoundTripTests` | Acceptance scenario 18: a GIF becomes a bulb, gets a second flavor and a category, is saved, loads under the 5.4 rules and is exported. |
| `BulbDialogsTests` | Edit Categories (settings override), New Category, Bulb Credits (sources, the two East Asian artists), GIF-to-bulb results, export copies, that every window builds from its XAML, and the 5.4 access keys of the Bulb Editing group headers (Alt+I, Alt+F: targets, no clash, drawn where the Card draws its header). |

Helpers in this folder: `WritingTestData` (pictures, GIFs, slot-table helpers), `TestPictures` (PNG files, an "optimized"
GIF), `EditorTestBed` (a real catalog and platform's real holding folder over a private data root, a recording Undo
history and scripted prompts), `FakeAppServices`. Shared helpers (owner: contracts, `../Shared`): `TestPaths`,
`GoldenData`, `TempDataRoot`, `StaThread`, `InMemorySettingsStore`, `RecordingLog`, `TestHoldingFolder`. Some tests build
raw `.bul` and GIF files with core-bulbs' `TestBul` and `TestGif` (`../Bulbs`).
