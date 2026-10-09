# tests/HolidayLights.Tests/Settings

Owner: core-settings. Namespace `HolidayLights.Tests.Settings`; run with

```
dotnet test tests/HolidayLights.Tests -c Debug --filter "FullyQualifiedName~HolidayLights.Tests.Settings"
```

| File | Covers |
|---|---|
| `SettingsStoreTests` | `JsonSettingsStore`: first run, debounced atomic writes, round trip of every value, damaged files, values of the wrong type, newer versions, write failures and retry, migrations |
| `SettingsFileLockTests` | a settings file held by another program, being replaced or denied (PRODUCT-SPEC 5.13): retried, never renamed or replaced before it was read, used once it can be read (edits made meanwhile applied again); a damaged file that cannot be renamed is copied |
| `SettingsSanitizerTests`, `SettingsMigratorTests` | validation and clamping of every section; version migrations |
| `NewcomerSettingsTests` | first-run values (today's Automatic theme, region, reduced motion), "Reset All Settings" |
| `ShippedThemesTests` | the 19 shipped themes against PRODUCT-SPEC 7.2 and the golden installer themes (the build-time check) |
| `ThemeLibraryTests`, `ThemeServiceTests`, `ThemePackageTests` | theme files, loading, matching, Recent Settings, `.hltheme` export and import |
| `SeasonCalendarTests` (+ `HolidayDateTables`) | Easter (Western and Orthodox), US and Canadian Thanksgiving and Chanukah 2000-2050, PRODUCT-SPEC 7.5 #19, overlaps, regions |
| `LegacyGoldenImportTests` | golden `legacy-registry.json` (the commissioning PC): the factory-default rule and the first-run import |
| `LegacyImportCatalogTests` | the import with the real bulb catalog: content matching, copies into My Bulbs, damaged files, the 5.4 categories of add-on files kept as category overrides |
| `LegacyImporterTests` | a customized 5.4 user (PRODUCT-SPEC 7.5 #3), add-on id re-creation, "Import Again..." |
| `LegacyPlatformTests` | the read-only registry reader and the 5.4 leftovers, on a key under `HKCU\Software\HolidayLightsTests\<guid>` that is deleted afterwards |

Test doubles of this area are in `Fakes/`; the shared helpers (owner: contracts) are in `../Shared`.
