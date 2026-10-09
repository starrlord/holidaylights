# tests/HolidayLights.Tests/Shared - test helpers

Owner: contracts (read-only for builders). Namespace `HolidayLights.Tests.Shared`.

| Helper | Use |
|---|---|
| `TestPaths` | repository root, golden files (`Golden(...)`) and fixtures (`Fixture(...)`), both copied to the test output, the bundled `Content` folder |
| `GoldenData` | `ReadJson` for `.json` and `.json.gz` goldens; `RgbaSha256` = the golden RGBA hash convention of `Golden/README.md` |
| `TempDataRoot` | a private, empty data root (`DataPaths.ForDataRoot`) deleted on dispose |
| `StaThread.Run` | runs WPF test code on the shared STA thread (a running dispatcher, one call at a time, no synchronization context) |
| `InMemorySettingsStore` | `ISettingsStore` without a file (records every `SettingsChange`) |
| `RecordingLog` | `IAppLog` that keeps entries for assertions |
| `TestHoldingFolder` | `IHoldingFolder` without the Recycle Bin |

Tests that set process environment variables use `[Collection(nameof(EnvironmentCollection))]` (no parallelism).
