# tools/publish - building the setup

Owner: app-shell. Turns the solution into what users install (PRODUCT-SPEC 6.10).

```
pwsh -NoProfile -File tools/publish/publish.ps1 [-Output <folder>] [-Configuration Release]
```

The script

* runs `dotnet publish src/HolidayLights.App -c Release -r win-x64 --self-contained true`: the .NET runtime (with WPF) is
  bundled, so Holiday Lights installs and starts on a stock Windows 11 PC without administrator rights and without
  going online (PRODUCT-SPEC 6.10, 6.11; Windows does not include the .NET 10 Desktop Runtime, and that runtime installs
  per machine with administrator rights). The script checks that the runtime files are there and that
  `HolidayLights.runtimeconfig.json` asks for no shared framework. It is not a single-file build: the installed screen
  saver `Holiday Lights.scr` is a copy of the small `HolidayLights.exe` launcher, which loads the same
  `HolidayLights.dll` and runtime from its own folder;
* lays out `<output>\Holiday Lights <version>\`: the published program, `Content\` (1,501 bulbs, 46 songs, 11 pictures,
  checked) and `Assets\BulbDocument.ico`;
* packs that folder into one file, `<output>\HolidayLights-<version>-Setup.exe`, with Inno Setup
  (`HolidayLights.iss`). `Get-InnoSetup.ps1` provides the compiler: a pinned Inno Setup, downloaded from its GitHub
  releases the first time, checked against its SHA-256 and installed portable under `<build root>\tools`.

`<output>` defaults to `$HL_BUILD_ROOT\dist` (or `<repo>\artifacts\dist` without `HL_BUILD_ROOT`); intermediate files go
to `$HL_BUILD_ROOT\publish`. The script never installs, registers, starts or deletes anything outside those folders.

## The setup

`HolidayLights-<version>-Setup.exe` is the one download. Inno Setup only carries the program: double-clicking the
setup unpacks the distribution folder into a temporary folder (a progress bar, nothing to choose), runs the program's
own installer from there (`HolidayLights.exe --install`) and removes the temporary folder when the installer closes.
It needs no administrator rights, writes nothing to the registry and has no uninstaller of its own.

The installer window offers to close a running Holiday Lights 5.4, copies the program and its content to
`%LOCALAPPDATA%\Programs\HolidayLights\`, creates `Holiday Lights.scr` beside it, writes the Start menu entry
"Holiday Lights", the per-user `.bul` association and the Windows Settings > Apps entry
(`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\HolidayLights`), and ends with "Start Holiday Lights"
(checked). No administrator rights are needed.

Running a newer setup updates the installation: the window says "Update Holiday Lights" (or "Reinstall" for the same
version, and asks first before installing an older one). The installer asks a running Holiday Lights to exit, closes
the copies still running from the program folder (such as the preview in Windows' Screen Saver Settings), replaces the
files, removes the files the previous version had that this one does not, and keeps settings, themes, bulbs, songs and
pictures.

`/SILENT` and `/VERYSILENT` install without a window (`HolidayLights.exe --install --quiet`) and start Holiday Lights
again when the update closed it. The setup's exit code is the installer's: 0 when installed, 1 when cancelled or failed.
Arguments that do not start with `/` go to the installer, so a test install stays out of your profile:

```
HolidayLights-<version>-Setup.exe /VERYSILENT --data-root "%TEMP%\hl-setup-test" --no-system-changes
```

Windows Settings > Apps runs `HolidayLights.exe --uninstall`: it restores the previous screen saver when Holiday Lights
is the screen saver, removes the Run value, the association, the Start menu entry and the Apps entry, removes the
program folder (only a folder the installer created, marked by `HolidayLights.install.json`), and moves your bulbs,
songs and pictures and your settings and themes to the Recycle Bin only when you ask. It never touches Holiday Lights
5.4.

For tests, `--data-root <dir>` installs into `<dir>\Programs\HolidayLights` (Start menu entry under `<dir>\Start Menu`)
and `--no-system-changes` skips every registry and shortcut write (they are logged instead).
