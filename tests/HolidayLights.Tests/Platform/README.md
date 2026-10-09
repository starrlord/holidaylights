# tests/HolidayLights.Tests/Platform

Owner: platform. Namespace `HolidayLights.Tests.Platform`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.Platform"
```

Covers OS services with fakes or a temporary registry key that the test deletes; never the user's real settings.

Helpers (owner: contracts, `../Shared`): `TestPaths` (repository, golden and content paths), `GoldenData` (JSON and
`.json.gz` readers, golden RGBA hashes), `TempDataRoot` (a private data root, deleted afterwards), `StaThread`,
`InMemorySettingsStore`, `RecordingLog`, `TestHoldingFolder`.

Helpers of this folder:

| Helper | Use |
|---|---|
| `DispatcherThread` | an STA thread pumping messages with a WPF dispatcher, like the app's UI thread (services with hidden message windows live there) |
| `TestRegistryRoot` | `HKCU\Software\HolidayLightsTests\<guid>` standing in for HKCU (Run key, file types, screen saver); deleted afterwards |
| `TestFolders.Delete` | removes a test data root, retrying while a virus scanner still holds a freshly written program file |

What the tests touch on this machine (all undone or read-only):

* Registry writes only below `HKCU\Software\HolidayLightsTests`; `SystemParametersInfo` and `SHChangeNotify` are
  replaced by fakes. The real Run key, `.bul` association and screen saver are only read.
* Hot keys: Ctrl+Alt+Shift+F22 and F23 are registered for a moment on a test thread and released.
* Single instance: private mutex and pipe names (`HolidayLights6.Test.<guid>`), so a running Holiday Lights is never
  disturbed.
* Recycle Bin: one uniquely named temporary file is recycled, then exactly that entry (`$I`/`$R` pair) is removed from
  the user's Recycle Bin again.
* Read-only probes: displays, wallpaper, keyboard layouts, system settings,
  pause signals, the string resources of a 5.4 screen saver stand-in (`StringResourceImage`).
