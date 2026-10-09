# Holiday Lights 6 - Contracts and ownership

| | |
|---|---|
| File | `docs/CONTRACTS.md` (owner: contracts) |
| Status | Binding for the build stage. Written after `docs/PRODUCT-SPEC.md` and `docs/ARCHITECTURE.md`; where this document refines an implementation detail of ARCHITECTURE it says so (section 11), and PRODUCT-SPEC still wins over both (PO-2). Updated at the wave-1 merge (section 13, "Changes after wave 1") and at the wave-2 integration (section 14, "Changes after wave 2"): read both before changing merged code. |
| Purpose | Twelve builders implement Holiday Lights 6 in parallel, each in an isolated copy, without coordinating. This document fixes who owns which files, the shared API every owner codes against, and the threading and naming rules. |
| Code | The shared contract types live in `src/HolidayLights.Core/Abstractions/` (namespace `HolidayLights.Core.Abstractions`, imported everywhere by a global using) and `src/HolidayLights.App/Contracts/` (namespace `HolidayLights.App.Contracts`). Every owner folder holds compiling skeletons (`throw new NotImplementedException()`) of the public classes its owner must implement. |

---------------------------------------------------------------------------------------------------------------------

## 1. Ground rules

1. **Builders add, change and delete files only inside their own paths (section 2).** A change anywhere else (a shared
   contract, another owner's skeleton, a project file, `Directory.Build.props`) goes into the builder's report as a
   change request with the exact edit. Nothing outside your paths is edited, not even "obvious" fixes.
2. **Project files are frozen.** All `*.csproj`, `Directory.Build.props`, `global.json` and `HolidayLights.sln` belong
   to contracts. They already provide what every owner needs: packages, embedded resources by wildcard, content
   items, global usings (section 3). New files in your folders are picked up automatically (C#, XAML pages, `.resx`).
3. **Public signatures of skeletons are contracts.** Keep every public type and member of your skeletons with its
   signature and documented behaviour; other owners call them. You may add public members, internal types and files
   freely inside your paths. If a skeleton signature cannot work, implement the closest working variant only after
   requesting the change in your report, and explain it.
4. **Contract types are read-only.** `Core/Abstractions` and `App/Contracts` are owned by contracts. Implement the
   interfaces; never copy-and-modify them.
5. **Quality bar.** Production code: small cohesive classes, XML doc comments on every public API (the `src` projects
   generate documentation files, so a missing comment is warning CS1591), nullable enabled, no warnings in your files,
   no dead code, no TODO stubs left in your area at the end. Skeleton `NotImplementedException`s in your area must all
   be gone when you finish.
6. **Builds.** Always set `HL_BUILD_ROOT` to your private folder before `dotnet build`/`dotnet test`
   (for example `set HL_BUILD_ROOT=%TEMP%\hl-build\<your-name>`). The whole solution must build.
7. **Tests.** xUnit tests in `tests/HolidayLights.Tests/<YourArea>/`, namespace `HolidayLights.Tests.<YourArea>`, run with
   `dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.<YourArea>"`. All of
   your tests pass. Unit tests must not depend on another owner's unimplemented code: use the shared test doubles
   (`tests/HolidayLights.Tests/Shared`) or fakes inside your own test folder.
8. **Isolation.** Tests and probes never modify the user's real settings, registry, startup, screen saver or file
   associations: use `TempDataRoot` / `HOLIDAYLIGHTS_DATA_ROOT`, temporary folders, and registry writes only under a
   test key the test deletes. GUI probes close themselves within seconds; leave no windows or processes running.
9. **Reference material is read-only.** `content/`, `assets/builtin/`, `assets/heritage/` and
   `tests/HolidayLights.Tests/Golden/` are never modified by hand. The golden data is authoritative for exactness.
10. **No network access** anywhere in the product (PRODUCT-SPEC 6.11).

---------------------------------------------------------------------------------------------------------------------

## 2. Ownership map

Paths are relative to the repository root. Owners are disjoint: every path has exactly one owner; a more specific path
(for example `src/HolidayLights.Core/Bulbs/Writing/`) is carved out of the general one.

| Owner | Paths (everything below each path) |
|---|---|
| **contracts** | `src/HolidayLights.Core/Abstractions/`, `src/HolidayLights.App/Contracts/`, every `*.csproj`, `Directory.Build.props`, `global.json`, `HolidayLights.sln`, `src/HolidayLights.App/app.manifest`, `src/HolidayLights.App/AssemblyInfo.cs`, `assets/heritage/`, `tests/HolidayLights.Tests/Shared/`, `tests/HolidayLights.Tests/Abstractions/`, `tests/HolidayLights.Tests/Contracts/`, `docs/CONTRACTS.md` |
| **core-bulbs** | `src/HolidayLights.Core/Imaging/`, `src/HolidayLights.Core/Bulbs/` (except `Bulbs/Writing/`), `tests/HolidayLights.Tests/Bulbs/` |
| **core-layout** | `src/HolidayLights.Core/Layout/`, `src/HolidayLights.Core/Flash/`, `tests/HolidayLights.Tests/Layout/`, `tests/HolidayLights.Tests/Flash/` |
| **core-sprites** | `src/HolidayLights.Core/Sprites/`, `tests/HolidayLights.Tests/Sprites/` |
| **core-settings** | `src/HolidayLights.Core/Settings/`, `src/HolidayLights.Core/Themes/`, `src/HolidayLights.Core/Seasons/`, `src/HolidayLights.Core/Legacy/`, `src/HolidayLights.Platform/Legacy/`, `assets/themes/`, `tests/HolidayLights.Tests/Settings/` |
| **platform** | `src/HolidayLights.Platform/` (except `Legacy/`), `tests/HolidayLights.Tests/Platform/` |
| **rendering** | `src/HolidayLights.Rendering/`, `tests/HolidayLights.Tests/Rendering/` |
| **audio** | `src/HolidayLights.Audio/`, `tests/HolidayLights.Tests/Audio/` |
| **app-shell** | `src/HolidayLights.App/Shell/`, `src/HolidayLights.App/App.xaml`, `src/HolidayLights.App/App.xaml.cs`, `src/HolidayLights.App/Program.cs`, `src/HolidayLights.App/Tray/`, `src/HolidayLights.App/FirstRun/`, `src/HolidayLights.App/About/`, `src/HolidayLights.App/Help/`, `src/HolidayLights.App/Install/`, `tools/publish/`, `tests/HolidayLights.Tests/App/` |
| **settings-ui** | `src/HolidayLights.App/Settings/`, `src/HolidayLights.App/Controls/`, `src/HolidayLights.App/Gallery/`, `src/HolidayLights.App/Preview/`, `src/HolidayLights.App/Styles/`, `src/HolidayLights.App/Themes/`, `tests/HolidayLights.Tests/Ui/` |
| **screensaver** | `src/HolidayLights.App/ScreenSaver/`, `tests/HolidayLights.Tests/ScreenSaver/` |
| **bulb-factory** | `src/HolidayLights.App/BulbFactory/`, `src/HolidayLights.Core/Bulbs/Writing/`, `tests/HolidayLights.Tests/BulbFactory/` |
| **branding-docs** | `src/HolidayLights.App/Assets/`, `src/HolidayLights.App/HelpContent/`, `assets/icons/`, `docs/USER-GUIDE.md`, `tests/HolidayLights.Tests/Branding/` |

Additions to the brief's map: `src/HolidayLights.App/Contracts/`, `assets/heritage/`, `tests/.../Shared|Abstractions|Contracts`
(contracts), `src/HolidayLights.App/Themes/` (settings-ui: `Generic.xaml` for the control kit's default styles),
`tests/HolidayLights.Tests/Ui/` (settings-ui's test area; named `Ui` because a filter on `HolidayLights.Tests.Settings`
would also match `HolidayLights.Tests.SettingsUi`) and `tests/HolidayLights.Tests/Branding/` (branding-docs).

Produced earlier, read-only for everybody: `content/`, `assets/builtin/`, `assets/heritage/`,
`tests/HolidayLights.Tests/Golden/`, `docs/PRODUCT-SPEC.md`, `docs/ARCHITECTURE.md`.

Exception to "every `*.csproj` belongs to contracts" (wave-1 merge): `assets/icons/generator/BrandingGenerator.csproj` is
branding-docs' developer tool, outside `HolidayLights.sln`, and belongs to branding-docs with the rest of `assets/icons/`.

### 2.1 Skeletons per owner (public entry points to implement)

| Owner | Public skeletons (namespace) |
|---|---|
| core-bulbs | `BmpDecoder`, `GifDecoder` + `GifAnimation`, `HeritageArt` (`HolidayLights.Core.Imaging`); `BulFile` + `BulGifEntry`, `BuiltInBulbs`, `AddOnBulbs`, `BulbCatalog : IBulbCatalog` (`HolidayLights.Core.Bulbs`) |
| core-layout | `ClassicLayoutEngine : ILayoutEngine` (`HolidayLights.Core.Layout`); `FlashEngine : IFlashEngine` with its `IFlashSequencer` (`HolidayLights.Core.Flash`) |
| core-sprites | `SpriteProvider : ISpriteProvider`, `CpuCompositor : ICpuCompositor`, `PixelArtScaler`, `GlowBaker` (`HolidayLights.Core.Sprites`) |
| core-settings | `JsonSettingsStore : ISettingsStore`, `NewcomerSettings` + `FirstRunContext` (`.Settings`); `ThemeLibrary : IThemeLibrary`, `ThemeService : IThemeService` (`.Themes`); `SeasonCalendar : ISeasonCalendar` (`.Seasons`); `LegacyImporter : ILegacyImporter`, `ILegacyRegistrySource`, `LegacyRegistrySnapshot` (`HolidayLights.Core.Legacy`); `LegacyRegistryReader`, `LegacyLeftovers : ILegacyLeftovers` (`HolidayLights.Platform.Legacy`); the 19 files of `assets/themes/` |
| platform | `DisplayService`, `WallpaperProvider` (`.Displays`); `HotKeyService` (`.Input`); `SingleInstance` (`.Instance`); `StartupRegistration`, `FileAssociation`, `ScreenSaverRegistration` (`.Integration`); `HoldingFolder` (`.Files`); `ShellOperations`, `SystemInfo` (`.Shell`); `PauseSignalSource` (`.Power`) - all `HolidayLights.Platform.*`, each implementing its Abstractions interface |
| rendering | `LightsPresenter` (incl. the additive `Diagnostics` property), `LightsPresenterOptions`, `LightsDiagnostics` + `LayerDiagnostics` (`HolidayLights.Rendering`) |
| audio | `IMusicDirector`, `MusicDirector`, `MusicPolicy`, `MusicState` + `MusicStatus` (`HolidayLights.Audio`); `MusicLibrary : ISongLibrary` (`.Library`); `MidiFile` + `MidiEvent` + `MidiTempoChange`, `MidiSequencer` (`.Midi`); `MusicEventHub : IMusicEventSource` (`.Events`) |
| app-shell | `Program`, `App` (`HolidayLights.App`); `AppHost : IAppServices`, `CommandLine` + `LaunchRequest` + `LaunchKind`, `LightsController : ILightsController`, `HotKeyController`, `ScreenSaverSessions`, `AppShellCommands : IAppShell`, `RollingFileLog : IAppLog` (`.Shell`); `TrayIconController`, `NotificationService` (`.Tray`); `WelcomeCard` + `WelcomeVariant` (`.FirstRun`); `AboutWindowHost` (`.About`); `HelpWindowHost` (`.Help`); `PerUserSetup` (`.Install`); `tools/publish/` |
| settings-ui | `SettingsWindow` + the six pages `HomePage`, `BulbFactoryPage`, `MusicBoxPage`, `ScreenSaverPage`, `ThemesPage`, `GeneralPage` (`.Settings`, `.Settings.Pages`); `SettingsWindowService : ISettingsWindowService`, `UndoHistory : IUndoHistory` (`.Settings`, `.Settings.Undo`); control kit pieces used by other owners: `InfoBar`, `Card`, `NightWell`, `ContentDialogs` (`.Controls`); previews `LightStage`, `LightStrip` (`.Preview`); `Styles/Theme.xaml`, `Themes/Generic.xaml` |
| screensaver | `ScreenSaverEntry`, `ScreenSaverService : IScreenSaverService`, `PictureLibrary : IPictureLibrary` (`HolidayLights.App.ScreenSaver`) |
| bulb-factory | `BulbFactoryDialogs : IBulbFactoryDialogs` (`HolidayLights.App.BulbFactory`); `BulbDocument`, `BulFileWriter` + `BulFileContent`, `GifBulbFactory` (`HolidayLights.Core.Bulbs.Writing`) |
| branding-docs | placeholder files to replace: `Assets/HolidayLights.ico`, `Assets/BulbDocument.ico`, `Assets/Tray/Tray{Lit,Unlit}{Light,Dark}.ico` (copies of the 5.4 icons), `Assets/Illustrations.xaml` (empty drawings under the contract keys); `HelpContent/contents.json` (complete book and topic structure); to create: `HelpContent/topics/<id>.md`, `HelpContent/credits.json`, `docs/USER-GUIDE.md`. Delivered in wave 1 besides: `Assets/Tooltips.xaml` (`HL.Tip.*` descriptions and tooltips, merged by `Illustrations.xaml`), `Assets/Banner/{AboutBanner,AboutFlash1,AboutFlash2,HelpBanner}.png` (MMPX 4x), `HelpContent/images/*.png` (192 DPI), `assets/icons/generator/` (BrandingGenerator; it compiles `tests/HolidayLights.Tests/Branding/AddOnArtistCredits.cs` and `UserGuideComposer.cs`) |

---------------------------------------------------------------------------------------------------------------------

## 3. Solution, projects and build

| Project | Target | References | Packages and items |
|---|---|---|---|
| `HolidayLights.Core` | `net10.0` | - | Embedded resources `holidaylights/builtin/**`, `holidaylights/themes/**`, `holidaylights/heritage/**` from `assets/` (read with `EmbeddedAssets`) |
| `HolidayLights.Platform` | `net10.0-windows10.0.26100.0` | Core | `Microsoft.Win32.SystemEvents` 10.0.11; WinRT projection (FocusSessionManager) |
| `HolidayLights.Rendering` | `net10.0-windows10.0.26100.0` | Core, Platform | `Vortice.Direct3D11` 3.8.3, `Vortice.DirectComposition` 3.8.3 |
| `HolidayLights.Audio` | `net10.0-windows10.0.26100.0` | Core | `NAudio` 3.1.0 |
| `HolidayLights.App` (`HolidayLights.exe`) | `net10.0-windows10.0.26100.0`, WPF | all | `H.NotifyIcon.Wpf` 2.4.1, `VirtualizingWrapPanel` 2.5.4; `StartupObject = HolidayLights.App.Program` (App.xaml is a Page); `ApplicationIcon = Assets/HolidayLights.ico`; `app.manifest` (Win8/8.1/10 supportedOS + PerMonitorV2); Content `Content/Bulbs/*.bul` (1,501), `Content/Music/*.mid` (46, no `reset.mid`), `Content/Pictures/*.BMP` (11) linked from `content/`; `Resource` for `Assets/**/*.png\|ico\|jpg\|bmp\|gif`; `Assets/BulbDocument.ico` also copied to the output; `HelpContent/**` embedded as `help/<path>` (except README.md) |
| `HolidayLights.Tests` | `net10.0-windows10.0.26100.0`, WPF | all five | xunit 2.9.3; `Golden/**` and `Fixtures/**` copied to the output; the App's `Content/` flows into the output through the project reference |

* `SupportedOSPlatformVersion` is `10.0.22621.0` (Windows 11 22H2) for the Windows projects: guard any newer WinRT API
  (CA1416 tells you).
* Global usings: `HolidayLights.Core.Abstractions` in every project (`Directory.Build.props`);
  `HolidayLights.App.Contracts` in App and Tests; `System.IO` and `Xunit` in Tests. WPF projects do **not** import
  `System.IO` implicitly (it clashes with `System.Windows.Shapes.Path`): add `using System.IO;` in App files that need it.
* `src` projects generate XML documentation (`GenerateDocumentationFile`), so undocumented public members warn.
* App suppresses `WPF0001` (Application.ThemeMode is still marked experimental).
* `HL_BUILD_ROOT` redirects `obj/` and `bin/` (`Directory.Build.props`). The test output folder is
  `<HL_BUILD_ROOT>/bin/HolidayLights.Tests/Debug/net10.0-windows10.0.26100.0/`.

---------------------------------------------------------------------------------------------------------------------

## 4. Naming and coding conventions

* **Namespaces follow folders**: `HolidayLights.<Project>.<Folder>` (e.g. `HolidayLights.Core.Bulbs.Writing`,
  `HolidayLights.App.Settings.Pages`). Exception: all contract types share one namespace,
  `HolidayLights.Core.Abstractions` (respectively `HolidayLights.App.Contracts`), whatever their subfolder.
* **Never name a type or namespace** like a common framework type: `Path`, `Timer`, `Monitor`, `Image`, `Color`,
  `Point`, `Size`, `Rect`, `Visual`, `Style`, `Key`, `ModifierKeys`, `PlacementMode`, `Orientation`, `Page`, `GridView`,
  `Range`, `Index`; and no namespace segment named `System` or `Windows` (it shadows `System.*` / WinRT `Windows.*`).
* **Ids are strings**: bulbs `builtin:<slug>` / `addon:<file stem>` / `user:<file stem>` (`BulbIds`), songs and pictures
  `bundled:<file name>` / `user:<file name>` (`MediaIds`), compared ordinal-ignore-case. Never persist 5.4 numeric ids.
* **Settings are immutable snapshots.** Change settings only through `ISettingsStore.Update(transform, change)` on the UI
  thread, with a `SettingsChange` whose description is the Undo text ("Use Candy Canes for the whole frame"). Persisted
  contract types have setters only because System.Text.Json source generation cannot keep the initializer defaults of
  `init` properties (missing JSON values would become `default`); never mutate a published instance - use `with`.
* **JSON**: only `HolidayLightsJsonContext` / `HolidayLightsJson` for settings, themes and the pipe (camel case, enum
  names declared on the enums, comments and trailing commas tolerated, unknown members ignored). Owners may define their
  own source-generated contexts for their own file formats (for example the bulb index cache).
* **Strings** shown to users are the exact PRODUCT-SPEC strings (house style: Title Case labels, sentence case
  descriptions; `&` access keys become `_`; "..." is U+2026). UI strings may live in XAML or `.resx` in the owner's folder.
* **Threads**: the UI thread owns settings, windows and services whose remarks say "UI thread"; the Lights thread is
  rendering's; the music thread is audio's; events documented as "raised on an arbitrary thread" must be marshalled by
  the subscriber. No blocking calls on the UI thread or the Lights thread (I/O and decoding go to the thread pool).
* **Logging**: every component receives an `IAppLog` (no static loggers); never log personal data or file contents.
* **Errors**: no modal error boxes; recoverable problems become states that the UI shows as InfoBars (PRODUCT-SPEC 6.6.5).
* **Async**: `Task`-returning APIs never block; UI continuations resume on the dispatcher; libraries use
  `ConfigureAwait(false)`.
* **Files**: atomic writes (temporary file + replace) for settings, themes and bulbs; user files removed through
  `IHoldingFolder`, never deleted directly.

---------------------------------------------------------------------------------------------------------------------

## 5. Shared contracts (`src/HolidayLights.Core/Abstractions`)

Every type is documented in code; this section gives the map and the rules that matter across owners.

| Folder | Types | Rules |
|---|---|---|
| `Imaging` | `RectI`, `PointI`, `SizeI`, `Bgra32`, `RgbColor`, `BgraPixelBuffer`, `Rgba32Image` (straight BGRA), `PremultipliedImage` | Pixels are `0xAARRGGBB` `uint`s, row-major top-down. Bulb art: alpha 0/255, transparent colour 0. Premultiplied pixels may have rgb &gt; a (additive light). `RectI` is Win32-style (right/bottom exclusive), virtual-screen physical pixels or art pixels. |
| `Bulbs` | `Side`, `Corner`, `CellSlot` (+ `CellSlots`), `BulbOrigin`, `BulbIds`, `BulbAnimationKind`, `BulbAnimationInfo`, `BulbCell`, `IBulb`, `IBulbResolver`, `BulbInfo`, `BulbQuery`, `BulbFilter`, `BulbSortOrder`, `IBulbCatalog` (+ events, results) | `CellSlot` values are the 5.4 slots 0-8. 5.4 `GetCellRect` rules: side flavor modulo flavor count, frame = phase modulo frame count. `IBulb` is thread-safe and immutable; `ContentKey` keys every cache. Hidden bundled bulbs still resolve. |
| `Arrangement` | `SlotAssignment` | 0-6 ids per edge, one per corner; value equality; `Classic54Default` = Christmas 1. |
| `Displays` | `DisplayInfo`, `IDisplayService`, `IWallpaperProvider` | `DisplayInfo.DeviceId` is the stable key; `Number` is what Identify shows. |
| `Layout` | `BulbSize`, `ArtScale`, `FrameMode`, `LayoutTarget`, `BulbPlacement`, `StripLayout`, `DisplayLayout`, `LightsRing`, `LightsLayout`, `ILayoutEngine` | **`ArtScale.Scale` is the only scaling rule** (round half away from zero, at least 1 for positive lengths); layout and sprites both use it so sprites fit to the pixel. Placements are absolute physical pixels with dense ordinals; strips are in build order Top, Bottom, Right, Left. |
| `Flash` | `FlashPatternId` (+ `FlashPatterns`), `FlashSettings`, `StepClock`, `IStepClockSource`, `BulbVisualState`, `FlashOptions`, `FlashBulbInfo`, `FadeProfile`, `BrightnessWave`, `DanceEnvelope`, `DanceGroupUpdate`, `MusicResponse`, `IFlashSequencer`, `IFlashEngine` | `FlashPatternId` 0-4 are the 5.4 numbers. **`StepClock` is the one step clock** (Stopwatch timestamps, consistent across processes): speed changes at the next boundary without resetting the step; patterns restart at step 0. Discrete states via `MoveTo`/`Current`, analytic descriptions for DirectComposition via `GetFadeProfile`/`GetWave`/`ApplyMusicEvents`, CPU previews via `Sample`. |
| `Sprites` | `SpriteStyle`, `GlowLevel` (+ `GlowLevels`), `SpriteKey`, `SpriteRequest`, `GlowSprite`, `ISpriteProvider`, `CompositeMode`, `LightsRenderRequest`, `ICpuCompositor` | Sprites are premultiplied and sized with `ArtScale`. Glow sprites are premultiplied with alpha 0 (additive). Returned images are shared and read-only. |
| `Scene` | `LayerMode` (+ `LayerModes`), `SceneEffects`, `DisplayScene`, `LightsScene`, `SceneTransition`, `PauseReasons`, `LightsPauseState`, `LightsHealth`, `DisplayLayerStatus`, `LightsStatus` (+ event args), `PillGlyph`, `PillRequest` | The immutable scene is built on the UI thread and is the single description of what the desktop shows; previews read the same scene. |
| `Settings` | `AppSettings` and every sub-record (`LightsSettings`, `LookSettings`, `ThemeableSettings`, `SaverLook`, `SaverFont`, `CurrentMusic`, `MusicSettings`, `SaverDeviceSettings`, `ScreenSaverPrevious`, `ColorSettings`, `CalendarSettings`/`CalendarEntry`/`CalendarRule`, `HotKeySettings`/`HotKeyBinding`/`HotKeyModifiers`, `StartupSettings`, `RestSettings`, `AccessibilitySettings`, `UiSettings`, `BulbPreferences`, `HiddenItems`, `FileSettings`, `ThemePreferences`, `RecentSettingsEntry`, `Import54Record`, `OnboardingState`), enums (`BulbDrawing`, `SaverMovementStyle`, `PicturePlacement`, `SaverDisplays`, `EnergySaverChoice`, `SettingsPageId`, `BulbListView`, `LookPreset`, ...), helpers (`LookPresets`, `SaverAnimations`, `SaverPictures`, `SaverFontSubstitutes`), `ISettingsStore`, `SettingsChange`/`SettingsChangeKind` | Covers every setting of PRODUCT-SPEC 7.1 and Appendix C with its default (JSON names as Appendix C, plus `lights.patternBeforeDance`, `music.muted`, `calendar.activeEntryId`, `onboarding.*`). Context-dependent first-run values are applied by core-settings (`NewcomerSettings`). |
| `Themes` | `ThemeDefinition` (+ `ThemeFlash`, `ThemeMusic`, `ThemeSaver`), `ShippedThemeKind`, `ShippedThemeNames`, `IThemeLibrary`, `IThemeService`, `ThemeApplyOptions`, `ThemeNameCheck`, `ISeasonCalendar`, `CalendarResolution`, `HolidayOccurrence` | A theme value missing from a file is null and takes its default when loaded. Settings store **unchecked** songs, themes store **checked** songs (5.4). |
| `Media` | `MediaIds`, `MediaOrigin`, `MediaImportResult`, `PlayMode`, `MusicEventKind`, `MusicEvent`, `IMusicEventSource`/`IMusicEventReader`, `SongInfo`, `SongKind`, `SongCategory`, `ISongLibrary`, `PictureInfo`, `IPictureLibrary` | `MusicEvent` is the spec's "NoteEvent"; its timestamp is when the sound is heard. Each consumer subscribes its own lock-free reader. `Beat` is a MIDI tempo-map quarter note (animation bulbs only); `AudioBeat` is a beat detected in an audio file (every Dance group lights). |
| `Legacy` | `ILegacyImporter`, `LegacyImportPreview`, `LegacyImportMode`, `LegacyImportResult`, `ILegacyLeftovers`, `LegacyLeftoverState` | Read-only towards everything 5.4 owns. |
| `Platform` | `IHotKeyService` (+ `HotKeyAction`, `HotKeyRegistration`, `HotKeyValidity`, `HotKeyCheck`), `IHoldingFolder` + `HeldItem`, `IShellOperations`, `ISystemInfo`, `IStartupRegistration`, `IFileAssociation`, `IScreenSaverRegistration` (+ status types), `IPauseSignalSource` + `PauseSignals` | Implemented by platform. Writes are skipped (and logged) when `AppRuntimeOptions.AllowSystemChanges` is false. |
| `Instance` | `InstanceCommandKind`, `InstanceCommand`, `InstanceMessage`, `InstanceNames`, `IInstanceConnection`, `ISingleInstance` | The pipe protocol (section 6.4). |
| `Undo` | `UndoStep`, `IUndoHistory` | Section 6.5. |
| `Diagnostics` | `IAppLog`, `AppLogLevel`, `NullAppLog`, `AppLogExtensions` | |
| `Runtime` | `DataPaths`, `AppRuntimeOptions`, `AppSessionKind`, `EmbeddedAssets`, `HeritageAssets` | Section 8. |
| `Serialization` | `HolidayLightsJsonContext`, `HolidayLightsJson` | Section 4. |

### 5.1 App contracts (`src/HolidayLights.App/Contracts`)

| Type | Implemented by | Used by |
|---|---|---|
| `IAppServices` (+ `AppServicesHost`) | app-shell (`AppHost`) | every App owner; XAML-created controls fall back to `AppServicesHost.Current` |
| `ILightsController` | app-shell | settings-ui (Home and Bulb Factory status, previews read `Scene` and `Clock`), app-shell (tray) |
| `IHotKeyController` + `HotKeyStatus` | app-shell | settings-ui (General > Hot Keys, Change Hot Key dialog) |
| `INotificationService` + `NotificationKind` | app-shell | settings-ui (`FirstClose`), app-shell |
| `IAppShell` | app-shell | settings-ui (footer Help/About, Uninstall), bulb-factory (F1) |
| `IScreenSaverSessions` | app-shell | screensaver (in-process "Preview Screen Saver"), app-shell (pipe) |
| `ISettingsWindowService` + `SettingsRequest` records | settings-ui | app-shell (tray, second launch, `--open`, `/c`, notifications) |
| `IBulbFactoryDialogs` + results | bulb-factory | settings-ui (context menus, selected-bulb bar, file import), app-shell (Exit asks first) |
| `IScreenSaverService` | screensaver | settings-ui (Screen Saver page) |
| `HelpTopics`, `HelpContentStore`, `HelpContents`, `CreditsContent` | contracts (content: branding-docs) | app-shell (Help, About), settings-ui and bulb-factory (F1) |
| `AppAssets`, `AppResourceKeys` | contracts (art: branding-docs; tokens: settings-ui) | app-shell (tray icons), settings-ui (General illustrations), app-shell (Welcome picture) |

---------------------------------------------------------------------------------------------------------------------

## 6. How the modules work together

### 6.1 Start-up and composition (app-shell)

`Program.Main` (STA) parses the command line (`CommandLine.Parse`): `--data-root <dir>` (sets
`HOLIDAYLIGHTS_DATA_ROOT` for the process and builds `DataPaths.ForDataRoot`), `--no-system-changes`
(`AppRuntimeOptions.AllowSystemChanges = false`), then:

1. Screen saver arguments first: `/s` and `/p` run `ScreenSaverEntry` with a screen-saver `AppHost` (no mutex, no tray,
   no hot keys); `/c[:hwnd]` (or a `.scr` launch without arguments) forwards `show-settings saver` to a running
   instance, else runs a settings-only session (`AppSessionKind.SettingsOnly`) that exits when Settings closes; `/a` exits.
2. `--render-test <dir>`, `--diagnostics [file]`, `--install` and `--uninstall` run without the mutex. `--diagnostics`
   writes a plain-text report (version, paths, bundled content, displays, settings, scene, Windows integration, hot keys,
   MIDI output) to the file or to the parent console; it runs as a windowless `RenderTest` session and never writes
   settings. `--install --quiet` installs without a window (the setup's `/SILENT`). A copy of the program named
   `Setup.exe` started without arguments also means `--install`. The screen saver switches are parsed once, by
   `HolidayLights.App.ScreenSaver.ScreenSaverArguments.TryParse`, which `CommandLine.Parse` calls first.
3. `ISingleInstance.TryClaim()`; a later launch connects, sends its `InstanceCommand`, waits for `Accepted`, exits.
4. WPF: `new App()`, `AppHost` constructs services in this order: log, `JsonSettingsStore.Load`, platform services,
   `IHoldingFolder.RecycleLeftovers`, `BulbCatalog` (+ `StartAsync`: built-ins at once; construct it with
   `new BulbCatalog(paths, settingsStore, holdingFolder, log, () => systemInfo.UserDisplayName)` so a bulb made from a GIF
   is credited to the Windows account display name, PRODUCT-SPEC 3.2.10; `IBulbCatalog.FindByContent`, `ImportFile`
   and `AllocateLegacyId` wait for the add-on index themselves, so the 5.4 import may run before indexing has finished),
   `MusicLibrary`, `PictureLibrary` (both `Start()`ed before `NewcomerSettings.Create`, `ThemeService` use and
   `ILegacyImporter.Analyze`: theme loading and matching convert checked/unchecked songs using the songs the library
   knows, and the 5.4 import uses the libraries to tell which songs and pictures are bundled), `ThemeLibrary.Load` (seeds
   shipped themes on first run), `ThemeService`, `SeasonCalendar`, `LegacyImporter`, engines (`ClassicLayoutEngine`,
   `FlashEngine`, `SpriteProvider`, `CpuCompositor`), `MusicDirector`, `LightsPresenter`, `LightsController`, window
   services (`SettingsWindowService`, `BulbFactoryDialogs`, `ScreenSaverService`). `Program` then registers the host
   with `AppServicesHost.Initialize` for the session it runs; an `AppHost` never registers itself, so hosts built by
   tests leave the services of XAML-created controls alone.
5. First run (`LoadOutcome == Created`): `newcomer = NewcomerSettings.Create(store.Current, context, calendar, themes,
   themeService)`; 5.4 present (`ILegacyImporter.IsLegacyInstallPresent`) -> `Import(Analyze()!, newcomer,
   LegacyImportMode.FirstRun).Settings` (factory defaults keep the newcomer look and add the 5.4 values to Recent Settings
   as `LegacyImporter.RecentSettingsLabel` = "Holiday Lights 5.4 Settings"; customized: the 5.4 values, Automatic themes
   off, music on unless Never); otherwise `newcomer`. Applied with `ISettingsStore.Update` (kind `Import`/`Internal`),
   before the first frame. "Start Fresh Instead" and "Reset All Settings" also use `NewcomerSettings.Create` (with a
   Recent Settings label).
6. Lights first: the first scene is applied with `FirstRunPowerUp` / `AutostartPowerUp` / `ShortPowerUp`; then the tray
   icon, hot keys, the pipe listener, music (`ApplyPolicy`, held until the Welcome card closes for 5.4 imports), the
   Automatic theme scheduler, the Welcome card about 2.5 s after launch on first run (never at sign-in).

### 6.2 Lights pipeline

* **Scene builder** (app-shell, UI thread): settings + `IDisplayService.Displays` + pause signals + `ISystemInfo`
  -> `LightsScene`: enabled displays (`lights.displays.disabled`, at least one on), `LayoutTarget(area = WorkArea,
  scale = ArtScale.Effective(display.Scale, lights.size))`, `ILayoutEngine.Layout(targets, arrangement, catalog,
  frameMode)`, effective `FlashOptions` and `Interval` (Limit Flashing: interval at least 3, fading forced on; "Use
  Less Power": interval at least 5, fading and glow off; "Stop Flashing": `StopFlashing`, no glow; "Turn Off the
  Lights": `LightsOn` false; the rule in effect in `EnergySaverInEffect`), `SceneEffects` (Look, reduced motion), new
  seeds when the pattern or layout changes (Random Flashing re-rolls on rebuild).
  Rebuild triggers: settings changes, `DisplaysChanged` (already debounced), catalog changes of bulbs in use.
* **Presenter** (rendering, Lights thread): `Apply(scene, transition)` diffs against the previous scene and rebuilds
  only affected displays; creates `IFlashSequencer`s with `IFlashEngine`; owns the `StepClock` (exposed through
  `IStepClockSource`, restarted at step 0 when a pattern starts); paces step boundaries with a high-resolution waitable
  timer; turns `Current` changes into `SetContent` and opacity animations (`GetFadeProfile`), continuous patterns into
  repeating animations (`GetWave`), music into envelopes (`ApplyMusicEvents`, events from `IMusicEventSource`); finds
  the desktop hosts itself (Progman, `SHELLDLL_DefView`, WorkerW, `0x052C` with lParam 1, maintenance every 2 s,
  `TaskbarCreated`, virtual desktops, Win+D) and reports `LightsStatus` (`EffectiveModeChanged`).
* **Pause aggregation** (app-shell): `IPauseSignalSource` + rest settings -> `LightsPauseState` for the presenter and
  `MusicPolicy.PausedByRules`/`Stopped` for the music; energy saver rules change the scene.
* **Previews** (settings-ui `LightStage`/`LightStrip`, app-shell tray header and Welcome cards, bulb-factory edge
  sample): the same `ILayoutEngine` with the scene's targets, `IFlashEngine` with the scene's `FlashOptions`
  (or a theme's own), `ILightsController.Clock` for the step, `IFlashSequencer.Sample` for states,
  `ICpuCompositor.DrawLights` into a `WriteableBitmap` (sprites requested at `S x zoom`).
* `--render-test <dir>` (app-shell): the current scene, one PNG per display, `ICpuCompositor` at zoom 1, no windows.

### 6.3 Music pipeline

app-shell builds `MusicPolicy` from settings (`music.*`, `current.music`), the pause rules, `IScreenSaverSessions`,
the Remote Desktop state and the held first song, and calls `IMusicDirector.ApplyPolicy` on every change. The director
(audio) owns the shuffle bag, gaps, engines (MIDI sequencer over WinMM, NAudio for audio files), volume, and publishes
`MusicEvent`s through `IMusicEventSource` (presenter, and the pipe stream to a running screen saver). settings-ui's
Music Box drives transport commands and shows `MusicState`. In a screen-saver process, screensaver builds and applies
the policy itself when no Holiday Lights instance answers on the pipe (music per "Play the Chosen Songs" with
`SaverRunning = true`); when one answers, the app plays and streams events back.

### 6.4 Instance pipe protocol

Transport (platform): mutex `Local\HolidayLights6.Instance`; pipe `HolidayLights6.<user SID>` with
`PipeOptions.CurrentUserOnly`; duplex; one UTF-8 JSON `InstanceMessage` per line (`ToLine` / `TryParseLine`).

| Client sends | Server (app-shell) does | Server replies |
|---|---|---|
| `show-settings [page]`, `open <files>`, `toggle-lights`, `lights on\|off`, `toggle-layer`, `theme <name>`, `exit`, `reset` | performs the command on the UI thread (`reset`: Settings on General with the "Reset Holiday Lights?" confirmation) | `{ "accepted": true }`, then closes |
| `saver-started` (screen saver process) | `IScreenSaverSessions.SaverStarted()`; subscribes to `IMusicEventSource` and streams `{ "music": ... }` lines on the same connection | music events until the connection closes |
| `saver-stopped`, or the connection closes | `IScreenSaverSessions.SaverStopped()` | - |
| `subscribe-music-events` | streams music events without a saver session | music events until the connection closes |

### 6.5 Settings window, Undo and the holding folder

* settings-ui owns the window and its apply model: on open, a Cancel snapshot of `ISettingsStore.Current` and a new
  `UndoHistory`; every `Changed` event of kind `Edit`, `ThemeLoaded`, `Import` or `Reset` becomes an Undo step
  (`UndoRedo`, `CancelRestore` and `Internal` never do); file operations record `UndoStep`s inside
  `IUndoHistory.BeginGroup` together with their settings change (Remove Bulb = `IBulbCatalog.RemoveUserBulb` + arrangement
  update). Cancel restores the snapshot's Cancel scope (everything except Show Lights and the music transport) and
  undoes removals, never additions. On close (OK, X, Alt+F4, tray Exit) the window calls `IHoldingFolder.CommitSession()`.
* Other owners record steps through `IAppServices.Undo` (bulb-factory: "Save" in Bulb Editing restores the previous
  file on undo); tray and hot-key changes are recorded automatically because they go through the settings store.
* Removed files always go through `IHoldingFolder` (platform), from the catalog (core-bulbs), song library (audio),
  picture library (screensaver), theme library (core-settings) and Bulb Editing (bulb-factory).

### 6.6 Adding bulbs and GIFs (PRODUCT-SPEC 3.2.10)

settings-ui orchestrates every entry point (Add Bulb..., drop, Import from a Folder, `OpenFilesRequest` from
`--open`/double-click): `.bul` -> `IBulbCatalog.ImportFile` (content identity, unique name, copy into My Bulbs);
`.gif` -> `IBulbFactoryDialogs.CreateBulbFromGif`, which can be implemented as `IBulbCatalog.ImportFile(gifPath)` (it
writes with `GifBulbFactory`/`BulFileWriter` and loads the bulb; outcome `Damaged` = "Cannot Import GIF File"), one Undo
step ("Added <name>"), then `EditBulbAsync` (a single GIF only); messages, snackbars and selection on Bulb Factory. New
`.bul` files written by Bulb Editing take their header id from `IBulbCatalog.AllocateLegacyId()`.

### 6.7 Threading summary

| Component | Thread rules |
|---|---|
| `ISettingsStore` | mutate on the UI thread; `Current` readable anywhere; `Changed` on the UI thread |
| `IBulbCatalog`, `ISongLibrary`, `IPictureLibrary`, `IThemeLibrary` | queries thread-safe; mutations on the UI thread; `Changed` may come from any thread (catalog, libraries). Exceptions: `IBulbCatalog.ImportFile`, `FindByContent` and `AllocateLegacyId` may be called from any thread and block until the add-on index is complete, so call them on the thread pool |
| `IBulb`, `ISpriteProvider`, `ICpuCompositor`, `ILayoutEngine`, `IFlashEngine`, `ISeasonCalendar`, `IThemeService` | thread-safe / pure |
| `IFlashSequencer` | one thread at a time (each consumer creates its own) |
| `LightsPresenter` | callable from any thread; events on the Lights thread |
| `IMusicDirector` | callable from any thread; `StateChanged` on any thread |
| `IDisplayService`, `IHotKeyService`, `ISystemInfo` | create and use on the UI thread; events on the UI thread (`IDisplayService` reads - `Displays`, `Primary`, `FromPoint` - from any thread) |
| `IPauseSignalSource` | `Changed` on any thread |
| `IHoldingFolder`, `IShellOperations` | thread-safe |
| App contracts (`ILightsController`, `ISettingsWindowService`, dialogs, notifications) | UI thread; events on the UI thread. Exception: `IBulbFactoryDialogs.CreateBulbFromGif` is thread-safe and meant for the thread pool (it waits for the catalog's index) |

---------------------------------------------------------------------------------------------------------------------

## 7. Cross-cutting services: one owner each

| Service | Owner | Notes |
|---|---|---|
| Undo/Redo and the Cancel snapshot model | settings-ui | `IUndoHistory`; others record steps through `IAppServices.Undo` |
| Removal holding folder (move, restore, Recycle Bin commit, crash leftovers) | platform | `IHoldingFolder`; commit when Settings closes (settings-ui); leftovers at start (app-shell) |
| Notifications (tray balloons, throttling, click navigation) | app-shell | `INotificationService` |
| InfoBars and snackbars | settings-ui | `InfoBar` control is shared; pages derive InfoBars from service state |
| On-screen pill | rendering | `LightsPresenter.ShowPill`; app-shell's hot-key handling decides when and what |
| Peek (`DWMWA_CLOAK` of the Settings window) | settings-ui | its own P/Invoke inside `Settings/` |
| Per-user installer and uninstaller, `tools/publish/` | app-shell | `PerUserSetup` (`--install`, `--uninstall`) |
| 5.4 import engine and leftovers | core-settings | `ILegacyImporter`, `ILegacyLeftovers` |
| 5.4 import UI: Welcome card variants, "Use My 2003 Lights", "Start Fresh Instead" | app-shell | |
| 5.4 import UI: General > Holiday Lights 5.4 group, "Import Again..." confirmation, Import Details dialog | settings-ui | |
| Scene builder, pause aggregation, energy-saver rules, music policy, Automatic theme scheduler | app-shell | |
| Command line, single-instance routing, settings-only session, `--render-test` | app-shell | |
| Instance pipe transport | platform | `ISingleInstance`; protocol types are contracts |
| Desktop host discovery and layer z-order | rendering | (ARCHITECTURE listed shell hosts under Platform; they belong with the layer windows on the Lights thread.) `HolidayLights.Rendering.Shell.DesktopHostLocator` is the only implementation; platform's duplicate `DesktopShell` was removed at the wave-1 merge |
| Display topology, DPI, work areas, wallpapers | platform | |
| Log files | app-shell | `RollingFileLog` |
| Look presets mapping | contracts (`LookPresets`) | UI: settings-ui |
| Font look-alikes of the screen saver | contracts (`SaverFontSubstitutes`) | label: settings-ui; drawing: screensaver |
| Help topic ids | contracts (`HelpTopics`) | content: branding-docs; window: app-shell; F1: settings-ui, bulb-factory |
| Original 5.4 art shared by several owners | contracts (`assets/heritage`, `HeritageAssets`) | decoding: core-bulbs (`HeritageArt`) |
| Window placement memory, last page, Show filter reset | settings-ui | |
| Description and tooltip texts (Appendix A) | settings-ui | the Bulb Editing texts are bulb-factory's. The Appendix A texts are written once in branding-docs' `Assets/Tooltips.xaml` (keys `HL.Tip.<Area>.<Control>`, merged app-wide through `Illustrations.xaml`): use them with `DynamicResource` (`ToolTip="{DynamicResource HL.Tip.FlashSettings.Speed}"`; the Undo/Redo tips are format strings for `string.Format`) instead of literals; texts that are not there stay in the owner's own XAML or `.resx` |

---------------------------------------------------------------------------------------------------------------------

## 8. Test isolation and data locations (PO-3)

* `DataPaths` resolves every user location: `%APPDATA%\Holiday Lights` (settings, themes), `%LOCALAPPDATA%\Holiday
  Lights` (cache, logs, holding folder) and `Documents\Holiday Lights` (Bulbs, Music, Pictures). With
  `HOLIDAYLIGHTS_DATA_ROOT=<dir>` (or `--data-root <dir>`) they move to `<dir>\Roaming`, `<dir>\Local` and
  `<dir>\Documents`. Bundled content always comes from the install folder (`<install>\Content`; in tests the test output
  folder, which receives the App's content). Components never compute these paths themselves: they get a `DataPaths`.
* `--no-system-changes` (or `HOLIDAYLIGHTS_NO_SYSTEM_CHANGES=1`): `AppRuntimeOptions.AllowSystemChanges = false`;
  platform's `StartupRegistration`, `FileAssociation`, `ScreenSaverRegistration`, core-settings' `LegacyLeftovers`
  fixes and app-shell's installer and shortcut code skip every write and log it instead.
* Tests: `TempDataRoot` gives a private data root; `InMemorySettingsStore`, `RecordingLog`, `TestHoldingFolder` replace
  services; registry tests use a key under `HKCU\Software\HolidayLightsTests\<guid>` (platform designs the seams) and
  delete it; `SystemParametersInfo`-based writes are never executed by tests. Tests that set process environment
  variables join `[Collection(nameof(EnvironmentCollection))]` (`tests/HolidayLights.Tests/Shared`), which runs
  without parallelism.

---------------------------------------------------------------------------------------------------------------------

## 9. Coverage: PRODUCT-SPEC item -> owner

Each row has exactly one accountable owner; "with" lists owners that implement parts through the contracts above.

### 9.1 Section 7.3 (MUST for 6.0) and the product-owner amendments

| Priority | 7.3 item | Owner | With |
|---|---|---|---|
| P0 | Lights engine: three layer modes with fallback, retries and status; Each Display; per-display on/off; work areas; rebuilds; exact 5.4 layout with scaling; Smooth and Crisp pixel art; faithful decoding; the step clock; the five classic patterns; Show Lights; pausing, power, lock and Remote Desktop rules; robustness; performance budget (5.1-5.6, 5.12-5.14) | rendering | core-layout (layout, patterns), core-bulbs (decoding), core-sprites (scaling), app-shell (scene, rest rules), platform (displays, signals) |
| P0 | Tray icon and menu exactly as 2.2 (incl. double-click and the header strip) | app-shell | settings-ui (`LightStrip`) |
| P0 | Settings storage, single instance, command line, startup (Run value), per-user `.bul` association, offline-only (6.6, 6.9, 6.10, 6.11, Appendix C) | app-shell | core-settings (store), platform (instance, Run value, association) |
| P0 | 5.4 import incl. the factory-default rule, theme import, add-on id re-creation, content matching, leftovers and the report (6.8) | core-settings | core-bulbs (content identity), audio, screensaver (file copies), app-shell (first-run flow) |
| P0 | Bulb Factory core: frame editor, live exact preview, targets and try-on, Bulb List, selected-bulb bar, Add To flyout, drag-and-drop rules, context menus, file import, keyboard and screen-reader model, Flash Settings and Bulb Drawing (3.2) | settings-ui | core-bulbs (catalog), bulb-factory (dialogs, GIF bulbs) |
| P1 | Control kit; Settings window shell (navigation, OK/Cancel/Help with the full snapshot, Undo/Redo, holding folder, string of lights, Peek) (3.0, 2.3, 4.3.1) | settings-ui | platform (holding folder) |
| P1 | Home page (3.1) | settings-ui | |
| P1 | Look presets, glow, smooth fading, flash limit, reduced-motion and High Contrast rules; power-up wave and theme transition (4.3-4.5, 5.5.3, 5.8, 5.9) | rendering | core-sprites (glow), core-layout (fades, limit), app-shell (effective scene), settings-ui (Look UI, High Contrast) |
| P1 | New patterns Twinkle and Chase Around the Screen (5.7) | core-layout | rendering |
| P1 | First run: Welcome card in all variants, notifications, snackbars, InfoBars, on-screen pill (2.5, 3.11, 3.12) | app-shell | settings-ui (snackbars, InfoBars), rendering (pill) |
| P1 | Themes page: cards, Save Theme, Restore Built-In Themes, Recent Settings, 19 shipped themes, Automatic themes with the region rules, Theme Calendar dialog (3.6, 5.11, 6.4, 7.2) | core-settings | settings-ui (page, dialogs), app-shell (scheduler) |
| P1 | Music Box page and engine: own MIDI sequencer, NAudio, Play Holiday Music, modes, shuffle bag, volume, MIDI output, song list with credits and chips, problems (3.4, 6.1) | audio | settings-ui (page) |
| P1 | Screen Saver page and `/s /p /c`: all modules and styles, every display, Smooth Motion, live preview, status card and the only-on-request install, Color dialog, font picker (3.5, 3.8.3, 6.2) | screensaver | settings-ui (page, Color dialog, font picker), platform (registration), app-shell (`/c`) |
| P1 | General page; hot keys with the recorder, AltGr check and pill (3.7, 3.8.2, 6.6.4) | settings-ui | platform (registration, AltGr check), app-shell (hot-key behaviour), rendering (pill) |
| P1 | **PO-1** "One frame around all displays": outline algorithm, seam-aware pieces, corners, Frame radios (5.2.4, 3.7) | core-layout | settings-ui (radios), app-shell (scene), rendering (pieces per layer) |
| P1 | Per-user installer and uninstaller (6.10) | app-shell | platform (registrations) |
| P2 | Slow Glow and Dance to the Music (MIDI note and audio beat events, latency offset, event stream to the saver), "Lights and Music" card, Holiday Party behaviour (5.7, 5.10, 3.4.5) | core-layout | audio (events), rendering (animations), app-shell (pipe stream), screensaver, settings-ui (card) |
| P2 | Bulb Editing, Edit Categories, New Category, Bulb Credits, Export Bulb File, 5.4-compatible `.bul` writer (3.3, 6.3) | bulb-factory | core-bulbs (`BulFile`, decoders) |
| P2 | About with the 2003 banner and complete credits; Help window with every topic, search and F1; descriptions and tooltips (3.9, 3.10, 6.5, 6.7, Appendix A) | app-shell | branding-docs (topics, credits), settings-ui and bulb-factory (tooltips, F1) |
| P2 | Accessibility pass (keyboard map, Narrator and NVDA, High Contrast) and the acceptance scenarios of 7.5 (Appendix B, 7.5) | settings-ui | app-shell, bulb-factory, screensaver (their windows); per-scenario owners in 9.3 |
| - | **PO-2** implementation details decided in ARCHITECTURE and CONTRACTS | contracts | (this document) |
| - | **PO-3** `HOLIDAYLIGHTS_DATA_ROOT`, `--data-root`, `--no-system-changes` | app-shell | contracts (`DataPaths`, `AppRuntimeOptions`), every owner honours them |

### 9.2 Every [MUST] subsection (and untagged sections, which are MUST)

| Section | Item | Owner | With |
|---|---|---|---|
| 2.2 | Tray: icon states, tooltip, mouse and keyboard [MUST], menu [MUST], Themes submenu [MUST] | app-shell | settings-ui (`LightStrip` header) |
| 2.3 | Settings window: identity, size and position [MUST], navigation, page header [MUST], bottom bar [MUST], apply model [MUST], which page opens [MUST], page skeleton [MUST] | settings-ui | app-shell (callers of `Show`) |
| 2.4 | Windows and dialogs (each assigned below) | - | - |
| 2.5 | First run and later starts [MUST] (2.5.1-2.5.4) | app-shell | core-settings (import, newcomer settings) |
| 2.6, 2.7 | Feature map and retirements | - | each row belongs to the section that implements it |
| 3.0.1 | Control kit [MUST] | settings-ui | |
| 3.0.2 | Light stage [MUST] | settings-ui | core-sprites, core-layout, platform (wallpaper) |
| 3.0.3 | Bulb art in the UI [MUST] | settings-ui | core-sprites (scaling) |
| 3.0.4 | Page conventions [MUST] | settings-ui | |
| 3.1 | Home [MUST] | settings-ui | |
| 3.2.1-3.2.12 | Bulb Factory [MUST]: wireframe, frame editor, preview and Peek, targets, Bulb List, selected-bulb bar and Add To, drag and drop, Flash Settings and Bulb Drawing, empty/loading/error states, adding files and GIFs, context menus, keyboard and screen reader | settings-ui | core-bulbs, bulb-factory |
| 3.3 | Bulb Editing, Edit Categories, New Category, Bulb Credits [MUST] | bulb-factory | |
| 3.4 | Music Box page [MUST] (3.4.1-3.4.6) | settings-ui | audio |
| 3.5 | Screen Saver page [MUST] (3.5.1-3.5.7) | settings-ui | screensaver (preview, Preview Screen Saver), platform (status card) |
| 3.6 | Themes page [MUST] (3.6.1-3.6.6, Save Theme, Theme Calendar dialog, Restore Built-In Themes, Recent Settings) | settings-ui | core-settings |
| 3.7 | General [MUST] incl. the [MUST] "Frame:" radios | settings-ui | app-shell (hot keys, lights controller), platform |
| 3.8.1 | Confirmations [MUST] | settings-ui | (`ContentDialogs` used by bulb-factory, app-shell) |
| 3.8.2 | Change Hot Key dialog [MUST] | settings-ui | platform (validation) |
| 3.8.3 | Color dialog [MUST] | settings-ui | |
| 3.8.4 | Choose a Bulb dialog [MUST] | settings-ui | |
| 3.8.5 | Import Details dialog [MUST] | settings-ui | core-settings (report) |
| 3.9 | About Holiday Lights window [MUST] | app-shell | branding-docs (credits), core-bulbs/core-sprites (banner art) |
| 3.10 | Holiday Lights Help window [MUST] | app-shell | branding-docs (topics) |
| 3.11 | Welcome card [MUST] | app-shell | settings-ui (`LightStage`), branding-docs (taskbar-corner picture) |
| 3.12 | Notifications | app-shell | |
| 3.12 | Snackbars and InfoBars | settings-ui | |
| 3.12 | On-screen pill [MUST] | rendering | app-shell (triggers) |
| 4.1 | Character (festive, not cheesy) | branding-docs | every UI owner |
| 4.2 | Tokens | settings-ui | |
| 4.3.1 | String of lights | settings-ui | |
| 4.3.2 | Power-up wave | rendering | app-shell (transition requests) |
| 4.3.3 | Theme transition | rendering | app-shell |
| 4.3.4 | Tray menu header | app-shell | settings-ui (`LightStrip`) |
| 4.3.5 | Heritage art (provision) | contracts | each surface's owner uses it |
| 4.4 | Motion catalogue: lights (wave, transitions, lights off, layer moves, resting, arrangement edits, pill) | rendering | |
| 4.4 | Motion catalogue: UI (pages, flyouts, snackbars, tiles, previews, stages) | settings-ui | app-shell (About, Welcome strip), screensaver (saver) |
| 4.5 | Light, dark and High Contrast | settings-ui | app-shell (tray icon variants) |
| 4.6 | Sound (none besides music) | audio | |
| 4.7 | Icons | branding-docs | |
| 4.8 | Voice and copy | branding-docs | every UI owner |
| 5.1 | Layer modes [MUST] | rendering | |
| 5.2.1 | Displays policy [MUST] | app-shell | platform, rendering |
| 5.2.2 | Taskbar / work areas [MUST] | app-shell | platform, rendering (z-order) |
| 5.2.3 | Rebuilds [MUST] | app-shell | platform (topology events), rendering (affected displays only) |
| 5.2.4 | One frame around all displays [MUST] (PO-1) | core-layout | |
| 5.3.1 | Classic layout, exactly [MUST] | core-layout | |
| 5.3.2 | Scale [MUST] | core-layout | contracts (`ArtScale`) |
| 5.3.3 | Pixel-art scaling [MUST] | core-sprites | |
| 5.3.4 | Art fidelity [MUST] | core-bulbs | |
| 5.4 | Bulb kinds [MUST] | core-bulbs | |
| 5.5.1 | The step clock [MUST] | rendering | contracts (`StepClock`), core-layout |
| 5.5.2 | Previews share the clock | settings-ui | |
| 5.5.3 | Flash limit | core-layout | app-shell (effective interval) |
| 5.6 | Classic flash patterns, exact [MUST] | core-layout | |
| 5.7 | New flash patterns, exact | core-layout | |
| 5.8 | Brightness and smooth fading [MUST] | rendering | core-layout (fade profiles, waves) |
| 5.9 | Glow [MUST] | core-sprites | rendering, settings-ui (High Contrast previews) |
| 5.10 | Music sync [MUST] | core-layout | audio, rendering, app-shell, screensaver |
| 5.11 | Automatic themes: dates, regions, overlaps, defaults, between-holidays theme | core-settings | |
| 5.11 | Automatic themes: switching at boundaries only, waiting while Settings is open, transition, Recent Settings, notification, first-edit InfoBar | app-shell | settings-ui (InfoBar on Bulb Factory) |
| 5.12.1 | Lights off (user) [MUST] | app-shell | rendering |
| 5.12.2 | Automatic pausing [MUST] | app-shell | platform (signals), rendering, audio |
| 5.12.3 | Power [MUST] | app-shell | |
| 5.13 | Robustness: Explorer restart, display changes, device lost, sleep/resume, unlock, Lights-thread crash | rendering | app-shell (tray re-add via H.NotifyIcon, resume re-evaluation) |
| 5.13 | Robustness: settings file unreadable, theme file unreadable, disk full | core-settings | settings-ui (InfoBars), app-shell (notification) |
| 5.14 | Performance budget [MUST] | rendering | settings-ui (gallery, stages), audio (Dance CPU), core-bulbs (index) |
| 6.1 | Music Box behaviour [MUST] (6.1.1-6.1.4) | audio | |
| 6.2.1-6.2.3 | Screen saver program modes, what is drawn, what changed [MUST] | screensaver | app-shell (`/c`, pipe server) |
| 6.2.4 | Installing as the Windows screen saver | platform | settings-ui, app-shell (Welcome "Fix It") |
| 6.3 | Bulb library: sources, ids, content identity, favorites/hidden/overrides storage, faithful decoding, index cache [MUST] | core-bulbs | |
| 6.3 | Bulb Editing writes (`.bul` v4 writer) | bulb-factory | |
| 6.4 | Themes [MUST] (6.4.1-6.4.4) | core-settings | |
| 6.5 | About and credits content [MUST] | branding-docs | app-shell (About window) |
| 6.6.1 | Startup (Run value) | platform | app-shell, settings-ui |
| 6.6.2 | Single instance | platform | app-shell (commands) |
| 6.6.3 | Command line | app-shell | |
| 6.6.4 | Hot keys | app-shell | platform (`IHotKeyService`), rendering (pill) |
| 6.6.5 | Errors and logs | app-shell | every owner logs through `IAppLog` |
| 6.7 | Help content [MUST] | branding-docs | |
| 6.8 | Import from Holiday Lights 5.4 [MUST] (6.8.1-6.8.5) | core-settings | app-shell, settings-ui (UI, section 7) |
| 6.9 | `.bul` files [MUST] | platform | app-shell (`--open`, repair at start), core-bulbs (watched folder) |
| 6.10 | Install, data and uninstall [MUST] | app-shell | contracts (`DataPaths`) |
| 6.11 | Privacy and offline [MUST] | app-shell | every owner (no network code) |
| 7.1 | Defaults (every setting) | core-settings | contracts (`AppSettings` initializers) |
| 7.2 | Shipped themes (exact) | core-settings | |
| Appendix A | Descriptions and tooltips | settings-ui | bulb-factory (Bulb Editing texts) |
| Appendix B | Keyboard map | settings-ui | app-shell (global keys, tray), bulb-factory (editor), screensaver (running saver) |
| Appendix C | Data model | contracts | core-settings (store, migrations) |
| Appendix D | Copy deck | branding-docs | the owner of each surface writes its strings |

### 9.3 Acceptance scenarios (7.5)

| # | Scenario | Owner |
|---|---|---|
| 1 | Commissioning PC first launch | app-shell |
| 2 | "Use My 2003 Lights" | app-shell |
| 3 | Customized 5.4 user import | core-settings |
| 4 | Newcomer, fresh PC, two-click theme change | app-shell |
| 5 | One bulb on the whole frame | settings-ui |
| 6 | Keyboard only | settings-ui |
| 7 | Layout fidelity | core-layout |
| 8 | Cancel restores everything | settings-ui |
| 9 | Remove Bulb | settings-ui |
| 10 | Screen saver | screensaver |
| 11 | Tray clicks | app-shell |
| 12 | Resting and robustness | rendering |
| 13 | Mouse held | rendering |
| 14 | Performance | rendering |
| 15 | Reduced motion | app-shell |
| 16 | Music and lights | core-layout |
| 17 | Hot keys | app-shell |
| 18 | Bulb Editing round trip | bulb-factory |
| 19 | Automatic themes | app-shell |
| 20 | High Contrast and screen readers | settings-ui |

---------------------------------------------------------------------------------------------------------------------

## 10. Reference data each owner starts from

| Owner | Start here |
|---|---|
| core-bulbs | `assets/builtin/table.json`, goldens `builtin-cells.json`, `bul-frames.json.gz`; PRODUCT-SPEC 5.3.4, 6.3 |
| core-layout | goldens `layout/`, `msvc-rand.json`; PRODUCT-SPEC 5.2.4, 5.3.1, 5.5-5.10 |
| core-sprites | `tests/HolidayLights.Tests/Sprites/Golden` (upscaling references); PRODUCT-SPEC 5.3.3, 5.9 |
| core-settings | goldens `legacy-registry.json`, `default-themes.json`; PRODUCT-SPEC 5.11, 6.4, 6.8, 7.1, 7.2, Appendix C |
| platform | ARCHITECTURE 1 and 5; PRODUCT-SPEC 5.1, 5.12, 6.10 |
| rendering | ARCHITECTURE 1 and 5; PRODUCT-SPEC 5.1-5.9 |
| audio | goldens `midi.json`, `shuffle.json`; PRODUCT-SPEC 6.1, 6.2 |
| app-shell | PRODUCT-SPEC 2, 3.9-3.12, 6.6, 6.10, 6.11 |
| settings-ui | PRODUCT-SPEC 2.3, 3.0-3.8 |
| screensaver | `assets/heritage/saver/`, golden `screensaver-animations.json`; PRODUCT-SPEC 6.2 |
| bulb-factory | PRODUCT-SPEC 3.3, 6.3 |
| branding-docs | `src/HolidayLights.App/HelpContent`, `content/README.md`; PRODUCT-SPEC 4.7, 6.5, 6.7, Appendix A and D |

---------------------------------------------------------------------------------------------------------------------

## 11. Decisions made at the contracts stage

1. **Project references**: Platform and Audio reference Core (ARCHITECTURE 2 listed "none") so that they implement the
   shared interfaces and use the shared types (`DisplayInfo`, `PlayMode`, `MusicEvent`, `DataPaths`) instead of
   duplicating them; Core still references nothing and never WPF.
2. **Target frameworks**: Windows projects target `net10.0-windows10.0.26100.0` (WinRT projection for
   `FocusSessionManager`) with `SupportedOSPlatformVersion` 10.0.22621.0; Core targets `net10.0`.
3. **Desktop host discovery** (Progman/WorkerW/DefView, `0x052C`) is implemented by rendering on its Lights thread,
   not by platform (confirmed at the wave-1 merge, section 13).
4. **Persisted contract types use `{ get; set; }`** (section 4) because the System.Text.Json source generator assigns
   every `init` property and loses initializer defaults; static defaults return new instances.
5. **Data root layout**: `<root>\Roaming`, `<root>\Local`, `<root>\Documents`.
6. **Pipe protocol**: JSON lines (`InstanceMessage`); `reset` is forwarded as its own command; music events stream on
   the saver's connection.
7. **Heritage art** used by several owners is embedded once from `assets/heritage/` (verbatim copies of extracted 5.4
   resources) and decoded by core-bulbs' `HeritageArt`.
8. **Screen saver drawing**: the saver (screensaver) draws in its own windows with the Core APIs (layout, flash,
   sprites, compositor) and either WPF drawing or its own DirectComposition tree (Vortice is available to App through
   the Rendering reference); it does not drive `LightsPresenter`.
9. **Previews**: `LightStage` and `LightStrip` (settings-ui) are the single preview implementation reused by app-shell
   (tray header, Welcome cards) and bulb-factory (edge sample, with a resolver for its unsaved document).
10. **Removed Bulbs** lists hidden bundled bulbs; removed My Bulbs files come back through Undo, the snackbar or Cancel
    while the window is open, and from the Recycle Bin afterwards.
11. **"NoteEvent"** of the brief is `MusicEvent` (note-on, beat, song state).

---------------------------------------------------------------------------------------------------------------------

## 12. Change requests

Put requests for changes outside your paths at the end of your report, one per item: the file, the exact change
(old and new text, or the new member with its documentation) and why. Contracts reviews them after the build stage;
until then, work around them inside your own paths without duplicating contract types.

---------------------------------------------------------------------------------------------------------------------

## 13. Changes after wave 1

Wave 1 (core-bulbs, core-layout, core-sprites, core-settings, platform, audio, rendering, branding-docs) was merged on
2026-10-08. Every file a wave-1 builder changed was inside its own paths (the one new project file is
`assets/icons/generator/BrandingGenerator.csproj`, section 2). A reflection diff of the public API before and after the
merge shows **no removed or changed skeleton signature**, only additions. Wave-2 builders code against the API below.

### 13.1 Contract changes (accepted change requests)

| # | From | File | Change | Why |
|---|---|---|---|---|
| 1 | core-layout | `Abstractions/Media/Music.cs` | New last member `MusicEventKind.AudioBeat` (a beat detected in an audio file); `Beat` now means only a MIDI tempo-map quarter note. audio's `AudioFileSongPlayer` publishes `AudioBeat`; core-layout's `DanceModel` lights every group on `AudioBeat`, only steps the animation bulbs on `Beat`, and no longer guesses ("no note yet = audio beat"). The pipe carries the enum as a number, so the new value 6 is wire-compatible. | PRODUCT-SPEC 5.10 treats the two kinds differently; the guess misread MIDI songs that open with a rest. |
| 2 | core-bulbs | `Abstractions/Bulbs/IBulbCatalog.cs` | New `int AllocateLegacyId()`: a header id (49 or more) that no known bulb uses and that was not handed out before; waits for indexing. | bulb-factory needs a fresh id for `BulFileWriter.Encode`; only the catalog knows every header id. |
| 3 | core-bulbs | `Abstractions/Bulbs/IBulbCatalog.cs` | `ImportFile` summary: it also turns a `.gif` into a My Bulbs bulb through the Bulb Factory writer (outcome `Damaged` = "Cannot Import GIF File"); other file types fail. Section 6.6 updated. | One GIF-to-bulb path. |
| 4 | core-sprites | `Abstractions/Sprites/Sprites.cs` | Documentation: `ISpriteProvider` caches in memory, only `PrefetchAsync` writes the disk cache (with the glow halos of light-bulb animations) and every request reads it; `ICpuCompositor.DrawLights` draws the glow of every lit light bulb first (clipped to its display), then the bulbs, at `floor(v x Zoom + Offset + 0.5)`. | The implemented policy. Rendering's composition tree already puts each display's glow layer beneath its bulb layer, so the desktop and the previews agree. |
| 5 | rendering | `Abstractions/Flash/IFlashEngine.cs` | `IFlashSequencer` remarks: in the classic patterns a light bulb's state repeats every `StripFrameCount` steps and its Glow equals its Brightness. Pinned by `Flash/ClassicPatternTests.ClassicPatterns_RepeatEveryStripFrameCountWithGlowEqualToBrightness`. | Rendering binds one repeating animation per light bulb (no work per step). |
| 6 | platform | `Abstractions/Displays/DisplayInfo.cs` | `IDisplayService` remarks: `Displays`, `Primary` and `FromPoint` read an immutable snapshot from any thread; the window, timers and `DisplaysChanged` stay on the creating thread. Section 6.7 updated. | `PauseSignalSource` reads the displays from its poll on the thread pool. |
| 7 | branding-docs | `App/Contracts/AppResources.cs` | New `AppAssets.TooltipsDictionary`, `AboutBanner`, `AboutFlash1`, `AboutFlash2`, `HelpBanner` (pack URIs, with their display sizes). | The files exist and are tested; consumers must not hard-code the URIs. |

Documentation updated in this file: section 2 (the generator project), 2.1 (rendering and branding-docs rows), 5
(`Media`: the two beat kinds), 6.1 steps 4 and 5 (catalog constructor, libraries started first, the first-run import
call), 6.6 (GIF path, `AllocateLegacyId`), 6.7 (`IDisplayService` reads), 7 (desktop hosts; the Appendix A texts) and 11.3.

### 13.2 Integration fixes made at the merge

* **One desktop host locator.** platform's public `HolidayLights.Platform.Desktop.DesktopShell` (used by nothing) and the
  interop declarations only it needed (`FindWindow`, `EnumWindows`, `SendMessageTimeout` and three constants) were
  removed; rendering's `DesktopHostLocator` is the implementation (11.3; it also sends `0x052C` without blocking the
  Lights thread). The read-only probe of the real desktop moved to
  `Rendering/DesktopHostLocatorTests.ThisDesktop_IsFoundWithoutChangingIt`; `Platform/HotKeyServiceTests` now declares
  its own `FindWindowEx`, which no product code uses any more.
* **Audio stress test under load.** `Audio/MusicEventHubTests.Concurrent_producers_and_a_reader_never_see_torn_or_reordered_events`
  failed in the full suite: with the thread pool saturated, `Parallel.For` ran the producers one after the other and the
  `Task.Run` consumer started after production, so one producer's events were all overwritten. The test now uses
  dedicated threads started together, drains the ring after production and asserts only what is guaranteed. The queue
  itself was correct.
* **Allocation test under load.** `Flash/PerformanceTests.MoveToAndSample_DoNotAllocate` saw about 8 KB in one of five
  full-suite runs (never in isolation, and a probe showed that other threads' garbage collections alone add nothing):
  one-time runtime work on the test thread. It now asserts that the fewest bytes over three 400-step batches is 0, so an
  allocation per step still fails it.
* **Cross-module tests** that no single owner could write: `Rendering/RealEngineSceneTests` (the real catalog, layout,
  flash and sprite engines prepare the reference PC's two 4K displays at 150 % in both frame modes; every sprite fills
  its cell to the pixel; the CPU preview draws both displays) and `Settings/LegacyImportCatalogTests` (the 5.4 import
  with the real catalog right after `StartAsync`: a copy of a bundled bulb maps to `addon:Arrow` by content, another
  bulb is copied into My Bulbs, a damaged file is reported) and `Bulbs/RealHoldingFolderTests` (Remove Bulb and its undo
  through platform's real `HoldingFolder`; nothing is recycled before the session is committed). Also
  `Branding/PictureTests.ContractUrisNameTheBannerAndTooltipFiles`.

### 13.3 Declined or deferred change requests

* core-bulbs (optional, NICE N17): persisted "recently used" bulbs (`BulbPreferences.Recent`, `BulbFilter.RecentlyUsed`,
  `IBulbCatalog.RecentlyUsed`). **Deferred**: not needed for consistency, and a new Show filter would widen settings-ui's
  wave-2 scope. `BulbCatalog.RecentlyUsed` stays a session-only public property.
* platform (optional): `PauseSignals.OnBatteryPower`. **Declined**: no 6.0 rule uses it (5.12.3 uses Energy Saver only);
  add it with the first rule that needs it.
* platform: "rendering calls platform's `DesktopShell.Locate`". **Resolved the other way** (13.2).

### 13.4 Notes for wave-2 builders

* **app-shell**
  * Catalog construction and start-up order: section 6.1. `HoldingFolder` is also `IDisposable` (releases its session
    lock; dispose at exit). `HoldingFolder.CommitSession` recycles synchronously (about 20-150 ms per file): call it off
    the UI thread when many files are held.
  * Music: `MusicLibrary.Start()` once; `new MusicDirector(songs, log)`; `ApplyPolicy` on every change of `music.*`,
    `current.music`, the pause rules, the saver state, Remote Desktop and the held first song; at exit
    `StopAsync(TimeSpan.FromMilliseconds(500))`, then `Dispose` (`StopAsync` is final).
  * Scene builder: `FlashClock.LimitInterval(interval, limitFlashing)` and `FlashClock.RunsClock(options)`
    (`HolidayLights.Core.Flash`). Calendar: store `ActiveEntryId = resolution.EntryId ?? CalendarEntryIds.Between`
    (`HolidayLights.Core.Seasons`).
  * "Use My 2003 Lights": restore the Recent Settings entry labelled `LegacyImporter.RecentSettingsLabel` (or apply
    `preview.LegacyValues`), turn Automatic themes off, and turn music on unless the mode is Never.
  * `--render-test` can use `ScenePreviewRenderer` (`RenderDisplay`, `RenderDesktop`, `CreateStaticStates`;
    `HolidayLights.Core.Sprites`), the same CPU path as the previews.
  * About and Welcome: `AppAssets.AboutBanner` as a 480 x 94 DIP image and the light strip `AboutFlash1`/`AboutFlash2`
    (296 x 15 DIP, alternating every 500 ms), `RenderOptions.BitmapScalingMode="Fant"`, on a #DDEEFF panel with an
    8 DIP rounded border, instead of rescaling `HeritageAssets` at run time; the banner already says "Modern Edition
    6.0". Help: `AppAssets.HelpBanner` (216 x 53 DIP) and `HelpContent/images/*.png` at their natural DIP size (192 DPI
    files; never stretched to the column).
  * Tray and status texts: `MusicStatus.WaitingForScreenSaverToEnd` (new, audio) means "Only When the Screen Saver is
    Off" while the Holiday Lights saver shows; the next song starts when it ends. `LightsPresenter.Diagnostics` is there
    for logs.
* **settings-ui**: descriptions and tooltips from `HL.Tip.*` (section 7). Handle `MusicStatus.WaitingForScreenSaverToEnd`
  in the Music Box status. `IBulbCatalog.Changed` comes in order on a thread-pool thread; an `Updated` event without ids
  means only `DamagedFiles` changed. `BulbCatalog.GifCannotBeImportedText` and `UnsupportedFileText` are the message
  texts for a bad GIF and an unsupported file. Turning "Play Holiday Music" on while the mode is Never sets the mode to
  Always (PRODUCT-SPEC 6.1.1): that is a settings change the UI makes, not the engine.
* **bulb-factory**: new files take their header id from `IBulbCatalog.AllocateLegacyId()`. The catalog's GIF import
  calls `GifBulbFactory` and `BulFileWriter` (your `Core/Bulbs/Writing`) through an internal seam, so keep their skeleton
  signatures. `BulFile.GetFlavorCount` and `GetEntryIndex` expose the slot table. The add-on artist credits decode
  `Doru.bul` as Shift-JIS and `YesMan.bul` as CP949; Bulb Credits shows `BulFile`'s Windows-1252 strings, so those two
  authors look garbled there unless they are decoded the same way.
* **screensaver**: the CPU drawing path is `ICpuCompositor.DrawLights` / `ScenePreviewRenderer`; `AudioBeat` events
  arrive over the pipe like every other music event.

### 13.5 Known issues carried into wave 2

* core-sprites: fixed in review round 1 (PO decision 1): sigma = min(0.22 x the shorter cell side, 24 art px), and
  `PrefetchAsync` bakes no halos (a prefetched light bulb's halo is baked by its first `GetGlow`). The sprite disk cache
  moved to `Cache\Sprites\v2`.
* rendering (updated at the wave-2 integration): verified live on the reference PC (both displays, all three layer
  modes, 4 themes, the wreath, the Classic 2003 and Bright Glow looks; every bulb pixel-exact; every pattern on screen as
  the sequencer says). The desktop lights halve the additive glow per display while that display runs in HDR (DXGI
  output color space ST 2084 / BT.2020, re-read after display changes and when DXGI reports a change), and the screen
  saver does the same per display (`ScreenSaver/FullScreen/HdrGlow`); not verified on HDR hardware (review r1 #43).
  Top-level layer, pill and Identify windows carry the `NonRudeHWND` property so Explorer never takes them for a
  full-screen app (review r1 #1).
  Win+D, virtual desktops, a real Explorer restart, real topology or DPI changes and sleep/resume were not performed on
  the user's session; simulated equivalents are tested.
* audio: R3 measured (review r1): the GS Wavetable Synth sounds 216-220 ms after `midiOutShortMsg`; MIDI events are
  stamped send + 190 ms (device) + `music.syncOffsetMs` (40) = 230 ms and published 40 ms ahead; the 30 ms audio-file
  model lands 8-29 ms after the audio engine. A "MIDI Output" change restarts the current MIDI song on the new device.
* core-layout: fixed (PO decision 5): every ring draws u(i, s) from `DisplaySeeds.PatternSeed(seed, ring id)`; ring 0 (a
  single display, display 1, the All Displays Together wreath) is unchanged. Combination has no segment-change event; rendering
  re-reads `GetWave` and `GetFadeProfile` on every step and never runs Combination as a repeating classic pattern, so
  each 40-step segment is picked up.
* core-settings interpretations: a calendar range ending on Feb 28 also covers Feb 29; Reset and Start Fresh keep the
  history, onboarding flags, favorites, hidden items, overrides, custom colours, window placement and the remembered
  screen saver; `ThemeService.Apply` adds no Recent Settings entry when nothing changes.
* platform: the real writes (Run value, `.bul` association, screen saver) were exercised only against the test registry
  key and fakes (no-system-changes rule).
* Small internal duplications, consistent with each other and left in place: the hot-key key tables
  (`Core/Settings/HotKeyKeys`, `Platform/Input/HotKeyKeys`) and Windows-1252 decoding (`Core/Bulbs/Windows1252`,
  `Core/Legacy/LegacyText`).

---------------------------------------------------------------------------------------------------------------------

## 14. Changes after wave 2

Wave 2 (app-shell, settings-ui, screensaver, bulb-factory, rendering) was merged on 2026-10-08 with an ownership map
that listed rendering's folders and files one by one, so `HolidayLights.Rendering.csproj` stays with contracts. Before the merge every working copy was hash-compared with main: all 373 differences (app-shell
89, settings-ui 132, screensaver 62, bulb-factory 56, rendering 34 including 4 deleted test files) were inside their
owners' paths, and main had not changed in any owned path since the copies were made. After the merge each owner's
paths equal its working copy; the integration's own edits in owned paths (14.1, 14.2) were copied back into the working
copies, so running the merge again changes nothing. No `NotImplementedException` is left on a reachable path; what was
verified on the reference PC is summarized in the integration notes of the project's history.

### 14.1 Contract changes (accepted change requests)

| # | From | File | Change | Why |
|---|---|---|---|---|
| 1 | app-shell, settings-ui | `App/Contracts/WindowServices.cs` | New `SettingsPageId? CurrentPage { get; }` on `ISettingsWindowService` (the page shown while the window is open, else null). app-shell's `MusicController` uses it for "Music is waiting". | PRODUCT-SPEC 3.12 shows the notification only while Music Box is not open; `ui.settings.lastPage` was only an approximation. |
| 2 | bulb-factory, settings-ui | `App/Contracts/WindowServices.cs` | `IBulbFactoryDialogs`: "UI thread, except `CreateBulbFromGif`", whose remarks now say it writes a file, waits for the catalog's add-on index, does not touch the UI, is thread-safe and belongs on the thread pool. Section 6.7 updated. | It can block for the length of first-start indexing; settings-ui calls it from `Task.Run`. |
| 3 | settings-ui | `Abstractions/Bulbs/IBulbCatalog.cs` | Remarks: the members that change settings (favorites, overrides, hide, unhide) are UI-thread; `ImportFile`, `FindByContent` and `AllocateLegacyId` may be called from any thread and block until the add-on index is complete. Section 6.7 updated. | The old "mutations on the UI thread" wording appeared to cover the file members too. |
| 4 | screensaver | `Core/Bulbs/BuiltInBulbs.cs` (core-bulbs) | New `public static BulbCell? GetSheetCell(int legacyId, int cell)`: a built-in sheet cell by its 1-based number over the normal rows (the 5.4 `IncludedBulbObject_CellIndexToRect` walk), or null where 5.4 throws. It returns the shared `BulbCell` (image and sheet rectangle) instead of the requested bare image, so the saver test can still check rectangles. `App/ScreenSaver/Art/BuiltInSheets.cs`, which duplicated the lookup, is deleted. | One implementation of the built-in cell walk. |
| 5 | screensaver | `App/Shell/CommandLine.cs` (app-shell) | `CommandLine.Parse` reads the screen saver switches with `ScreenSaverArguments.TryParse` (one parser; it also accepts `/p1234` and `/l`); app-shell's own switch parser was removed. Section 6.1 step 2 updated. | Two parsers of the same Windows contract had drifted apart. |
| 6 | settings-ui | `tests/HolidayLights.Tests/Shared/StaThread.cs` | `StaThread.Run` runs every call on one long-lived STA thread with a running dispatcher, one call at a time, without a synchronization context (awaits resume on the thread pool as before); the timeout counts from when the code starts; a failure on the thread between calls is reported by the next call; a call that times out leaves the thread behind and later calls get a new one. | With a thread per call, WPF's text services could reach an earlier call's text box from the wrong thread when the user moved a window during a test run, and the test host crashed. |
| 7 | bulb-factory | `tests/HolidayLights.Tests/Shared/TestDoubles.cs` | `TestHoldingFolder` names held files by a running sequence number instead of the item count. | After a restore, the next hold of a same-named file reused a name that was still held. |
| 8 | app-shell | `App/ScreenSaver/ScreenSaverEntry.cs` (screensaver; documentation) | Class remarks: `Program` has created the process's `App` and a started screen-saver `AppHost` (no mutex, tray, hot keys or presenter; settings never written); the entry creates no other `Application`, runs its own message loop and returns the exit code, after which `Program` stops the host. | WPF allows one `Application` per process; the loop is the entry's to run. |
| 9 | app-shell | `docs/CONTRACTS.md` 6.1 step 2 | `--diagnostics [file]` and the `Setup.exe` meaning of an empty command line documented. | Implemented but undocumented. |
| 10 | rendering | `docs/CONTRACTS.md` 13.5 | The rendering bullet now states what was verified live in wave 2. | The wave-1 text was out of date. |
| 11 | bulb-factory | `App/HelpContent/topics/making-bulbs.md`, `docs/USER-GUIDE.md` (branding-docs) | After "Select **Change…** to choose a new GIF animation for that place, or drop a GIF picture on the preview." both now add "You can also choose one or more PNG pictures of the same size: they become the frames of the animation, in file-name order." (the guide stays the composition of the topics). | Bulb Editing's Change… also accepts PNG frames. |

### 14.2 Integration fixes made at the merge

* **The XAML services registration moved to `Program`.** `AppHost`'s constructor called `AppServicesHost.Initialize(this)`,
  so every host a test built (the App tests' render-test and diagnostics hosts, with a fake music director) became the
  services of every XAML-created control in the process. The Ui tests' `LightStrip`s then reached that host, its
  presenter creation threw, the exception repeated in `Unloaded`, and the test host crashed (12 Ui failures and an
  aborted run right after the raw merge). `Program` now registers the host of the session it runs (normal, settings-only,
  screen saver, setup); `AppHostTests.AHostLeavesTheServicesOfXamlControlsAlone` pins it. Section 6.1 step 4 updated.
* **Subscriptions recorded only when they succeed.** `LightStrip`, `LightStage` and `BulbThumbnail` (settings-ui) set
  their `subscribed` services after subscribing, so a services object that throws cannot throw again from `Unloaded`.
* **No "Your lights stay on" while exiting.** `SettingsWindowService` showed the first-close notification when tray
  "Exit Holiday Lights" (or `--exit`) closed an open Settings window. `CloseKeepingChanges` (the exit path) now closes
  without it; a normal close still shows it once ever. Verified live: the exit log shows "Settings closed" and no
  notification.
* **Catalog events read on the catalog's thread.** `BulbThumbnail` (settings-ui) read its `BulbId` dependency property
  in its `IBulbCatalog.Changed` handler, which runs on the catalog's thread (6.7): every change of a bulb in use logged
  "A bulb catalog subscriber failed." and the chip thumbnails never redrew. Found live by turning a GIF into a bulb. The
  handler now reads a copy of the id kept by the property's change callback and redraws on the UI thread;
  `Ui/BulbFactoryPageTests.RemovingAMyBulbsBulbUpdatesTheEdgesAndUndoBringsBothBack` now asserts that no catalog
  subscriber fails (it failed before the fix).

### 14.3 Declined or already done

* app-shell: rows for `--diagnostics`, `--install` and `--uninstall` in PRODUCT-SPEC 6.6.3. **Declined for the spec**:
  PRODUCT-SPEC is read-only and these are support and setup commands, not product behaviour; they are documented in 6.1
  step 2 (14.1 #9).
* bulb-factory: changing `HL.Tip.BulbEditing.Change` to mention PNG pictures. **Declined**: the Appendix A text ("Chooses
  a new GIF animation for the selected side and flavor.") is binding and pinned by `Branding/TooltipTests`; GIF stays
  the first and default choice, and Help now mentions PNG frames (14.1 #11).
* screensaver: glow and bulb z-order in rendering's `SceneVisuals` and `BulbVisuals`. **Already done** by rendering in
  wave 2 (`Composition/VisualTree.AddInFront` / `AddInFrontOf`: glow behind the bulbs, later bulbs in front), so 13.1 #4
  ("each display's glow layer beneath its bulb layer") now holds.
* screensaver: `Program.Main` and `AppHost.Pictures` / `AppHost.ScreenSaver` wiring. **Already done** by app-shell in
  wave 2 against the skeletons; it works unchanged with the real implementations.

### 14.4 Known issues carried into the review phase

The ranked list was handled in review round 1 (section 15).

---------------------------------------------------------------------------------------------------------------------

## 15. Changes after review round 1

Twelve owner-scoped fixers worked in isolated copies; the merge integrator copied each fixer's listed files after
checking by hash that every file differed from main and that main had not changed it since the copies were made (170
files, no overlaps), then made the cross-owner changes below.

### 15.1 Contract and public API changes

| # | From | File | Change | Why |
|---|---|---|---|---|
| 1 | integration (r1 #9) | `Abstractions/Instance/InstanceMessages.cs`, `Platform/Instance/SingleInstance.cs`, `App/Shell/RunningInstance.cs` | `InstanceNames.MutexFor(dataRoot)`, `PipePrefixFor(dataRoot)` and `RootTag(dataRoot)` (16 hex digits of SHA-256 over the full, case-folded data root); `SingleInstance(IAppLog, string? dataRoot)` (the one-argument constructor reads `HOLIDAYLIGHTS_DATA_ROOT`); `RunningInstance.IsPresent(dataRoot)` and `WaitForExit(dataRoot, timeout)`. `Program`, the installer and the screen saver's pipe client pass `DataPaths.DataRoot`. Without a data root the names are unchanged. | A launch with `--data-root` forwarded its command to any running instance, including the user's real one. |
| 2 | core-settings (r1 #6) | `Abstractions/Settings/ISettingsStore.cs` | New `SettingsLoadOutcome.Unavailable`: the file exists but could not be read; defaults are used, the file is neither renamed nor written, and its settings replace the defaults (`Changed`, internal) once it can be read. app-shell shows "Settings couldn't be read", skips the start-up reconciliation of Start with Windows and the `.bul` association, and shows no Welcome card for that start. | A locked file was reported as damaged. |
| 3 | core-layout (r1 #10, #46) | `Core/Flash/DisplaySeeds.cs` (new public), `Abstractions/Flash/IFlashEngine.cs`, `Abstractions/Layout/LayoutModels.cs` | `DisplaySeeds.PatternSeed(ulong, int)` and `ClassicRandomSeed(uint, int)`; `FlashOptions.PatternSeed`, `BulbPlacement.ChaseIndex` and `StripLayout.OutlineEdge` documented for per-ring seeds and the chase counter that runs on across an outline edge. The screen saver's `SaverOptions.ForDisplay` uses `DisplaySeeds` (one rule for desktop and saver). | PO decision 5; review findings. |
| 4 | core-sprites (r1 #12, #13) | `Abstractions/Sprites/Sprites.cs` (docs) | The emissive rule (luma or brightest channel +32), sigma = min(0.22 x shorter side, 24 art px), and `PrefetchAsync` baking no halos. | PO decision 1. |
| 5 | audio (r1 #8) | `Abstractions/Media/Music.cs`, `Abstractions/Settings/DeviceSettings.cs` (docs) | `MusicEvent.Timestamp` = send + the device's own latency (GS synth 190 ms) + `music.syncOffsetMs`, published 40 ms ahead; `MusicSettings.SyncOffsetMs` is the way from the device to the listener. | R3 measured. |
| 6 | platform (r1 #77, #78) | `Abstractions/Platform/PauseSignals.cs` (docs) | `FullScreenDisplayIds` keeps a full-screen window that lost the focus to another display while it still covers its own; the poll slows to 2 s while the lights rest everywhere. | Review findings. |
| 7 | screensaver (r1 #73) | `App/Contracts/WindowServices.cs` (docs) | `IScreenSaverService.RunPreviewAsync` fails with the error when the saver could not start (the session is then already reported as stopped); the Screen Saver page shows "The screen saver couldn't start." | The button stayed dead. |
| 8 | settings-ui (r1 #4, #17, #24, #42) | `App/Preview/LightStage.cs`, `LightStrip.cs`, `PreviewActivity.cs`, `App/Controls/ArrangementChip.cs`, `LiveRegions.cs` | `LightStage.Zoom` (`StageZoom` Auto/Fit/Corner), `ZoomCorner`, `CornerScale` (0.4); `LightStrip.CropRun`; `ArrangementChip.TryOnBulbId`; `PreviewActivity` (previews rest while the session is locked, the display is off or the user is away); `Controls/LiveRegions` (every TextBlock with a Polite or Assertive live setting announces its text). The Welcome cards use `Zoom = Corner`, `ZoomCorner = TopLeft`. | PO decision 8 and review findings. |
| 9 | settings-ui (r1 #40) | `App/Settings/FileProblems.cs`, `ISettingsPage.cs` | `FileProblems.Is/Sentence`; `ISettingsHost.ShowMessage(text, severity)` (window InfoBar). Music Box and Screen Saver removals report failures the same way. | File actions had no error handling. |
| 10 | integration (r1 #14) | `Core/Bulbs/CreditText.cs` (new public) | `CreditText.Decode(author, copyright)`: Shift-JIS (932) or CP949 when the fields name a .jp or .kr address and their bytes are valid there with a double-byte character; used by the catalog (shown and searched text) and Bulb Credits. Files and Bulb Editing keep the original bytes. | PO decision 4. |
| 11 | integration (r1 #19) | `Core/Bulbs/BulbCatalog.cs`, `DecodedArtCache.cs`, `Core/Sprites/SpriteProvider.cs`, `SpriteMemoryCache.cs`, `App/Preview/SpriteBitmaps.cs`, `WallpaperBackdrops.cs` | `BulbCatalog.TrimMemory()`, `SpriteProvider.TrimMemory()`, `DecodedArtCache.Clear()`, `SpriteMemoryCache.Clear()`, `SpriteBitmaps.Clear()`, `WallpaperBackdrops.Clear()`; `Settings/MemoryTrim` calls them and runs one compacting collection when the Settings window has closed (not on exit). | 5.14: < 200 MB with Settings closed. |
| 12 | integration (r1 #55) | `Core/Bulbs/Catalog/FolderWatcher.cs` | Now public, with a constructor taking a file predicate; My Music (`MusicLibrary`) and My Pictures (`PictureLibrary`) are watched through it, so the folders are created on first use only. | PRODUCT-SPEC 6.10. |
| 13 | integration (r1 #37) | `App/ScreenSaver/Rendering/InstalledFonts.cs` (new) | Installed fonts = WPF families plus GDI faces (`EnumFontFamiliesExW`) that WPF resolves; `SaverText.IsInstalled` and `FontPicker.IsInstalled` use it and the picker lists faces such as Arial Black. | 5.4 checked GDI faces. |

### 15.2 Not changed

* `JsonSettingsStore.Update` still invokes `Changed` as one multicast delegate (optional hardening from app-shell for
  r1 #3); the tray's `Refresh` no longer throws, so one subscriber can no longer cut off the others.

---------------------------------------------------------------------------------------------------------------------

## 16. Release preparation (public repository)

The repository was made self-contained for publishing and for release builds by GitHub Actions:

| # | File | Change | Why |
|---|---|---|---|
| 1 | `content/`, `HolidayLights.App.csproj` | The 1,501 bulbs, 46 songs and 11 pictures moved into `content/Bulbs`, `content/Music` and `content/Pictures` (provenance and credits in `content/README.md`); the App links them from there. The output layout (`Content\Bulbs`, `Content\Music`, `Content\Pictures` next to the exe) is unchanged. | The original distribution folder is not part of the repository. |
| 2 | `Abstractions/ProjectInfo.cs` | New `ProjectInfo` (`Author` = "StarrLord", `HomePage` = https://github.com/starrlord/holidaylights, `HomePageText`, `License` = "MIT License"). | One source for the About window, the Apps entry and the release Read Me. |
| 3 | `Abstractions/Platform/SystemServices.cs`, `Platform/Shell/ShellOperations.cs` | New `IShellOperations.OpenProjectHomePage()`: opens `ProjectInfo.HomePage` in the default browser; no other web page is ever opened. | The project link in About (PRODUCT-SPEC 3.9, 6.11). |
| 4 | `App/About/AboutWindow.xaml(.cs)` | Creator line, project link (mouse, or Tab and Enter) and the licence line "Open source under the MIT License." | PRODUCT-SPEC 3.9. |
| 5 | `App/Install/UninstallEntry.cs` | `Publisher` = "StarrLord"; `URLInfoAbout` and `HelpLink` = the home page. | Windows Settings > Apps shows the creator and the project link. |
| 6 | `tests/.../Shared/TestPaths.cs` | `OriginalSource` and `Reference` replaced by `Fixture(relativePath)` (`tests/HolidayLights.Tests/Fixtures`, copied to the output). Tests read the bundled content from `TestPaths.ContentFolder`, the upscaling references from `Sprites/Golden/upscale`, the saver table from golden `screensaver-animations.json`; the 5.4 screen saver is a generated stand-in (`Platform/StringResourceImage`). | The tests run in a clean clone. |
