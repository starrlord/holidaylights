# tests/HolidayLights.Tests/App

Owner: app-shell. Namespace `HolidayLights.Tests.App`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.App"
```

Covers command-line parsing, start-up flows (newcomer, 5.4 factory defaults, customized import), scene building, pause aggregation, notifications throttling, installer (temporary folders and data roots only).

Helpers (owner: contracts, `../Shared`): `TestPaths` (repository, golden and content paths), `GoldenData` (JSON and
`.json.gz` readers, golden RGBA hashes), `TempDataRoot` (a private data root, deleted afterwards), `StaThread`,
`InMemorySettingsStore`, `RecordingLog`, `TestHoldingFolder`.
