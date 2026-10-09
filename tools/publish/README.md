# tools/publish - building the distributable

Owner: app-shell. Turns the solution into what users install (PRODUCT-SPEC 6.10).

```
pwsh -NoProfile -File tools/publish/publish.ps1 [-Output <folder>] [-Configuration Release] [-FrameworkDependent]
```

The script

* runs `dotnet publish src/HolidayLights.App -c Release -r win-x64 --self-contained true`: the .NET runtime (with WPF) is
  bundled, so `Setup.exe` installs and starts Holiday Lights on a stock Windows 11 PC without administrator rights and
  without going online (PRODUCT-SPEC 6.10, 6.11; Windows does not include the .NET 10 Desktop Runtime, and that runtime
  installs per machine with administrator rights). The script checks that the runtime files are there and that
  `HolidayLights.runtimeconfig.json` asks for no shared framework. `-FrameworkDependent` leaves the runtime out, for
  developers only. It is not a single-file build: `Setup.exe` and the screen saver `Holiday Lights.scr` are copies of
  the small `HolidayLights.exe` launcher, which loads the same `HolidayLights.dll` and runtime from its own folder;
* lays out `<output>\Holiday Lights <version>\`: the published program, `Content\` (1,501 bulbs, 46 songs, 11 pictures,
  checked), `Assets\BulbDocument.ico`, `Setup.exe` and `Read Me.txt`;
* zips that folder as `<output>\HolidayLights-<version>-win-x64.zip`.

`<output>` defaults to `$HL_BUILD_ROOT\dist` (or `<repo>\artifacts\dist` without `HL_BUILD_ROOT`); intermediate files go
to `$HL_BUILD_ROOT\publish`. The script never installs, registers, starts or deletes anything outside those folders.

## The installer

`Setup.exe` is a copy of the program. Started without arguments under that name, it is the per-user installer
(`HolidayLights.exe --install` does the same): a small window offers to close a running Holiday Lights 5.4, copies the
program and its content to `%LOCALAPPDATA%\Programs\HolidayLights\`, creates `Holiday Lights.scr` beside it, writes the
Start menu entry "Holiday Lights", the per-user `.bul` association and the Windows Settings > Apps entry
(`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\HolidayLights`), and ends with "Start Holiday Lights"
(checked). No administrator rights are needed. Installing again over an installation updates it (a running Holiday
Lights is asked to exit first).

Windows Settings > Apps runs `HolidayLights.exe --uninstall`: it restores the previous screen saver when Holiday Lights
is the screen saver, removes the Run value, the association, the Start menu entry and the Apps entry, removes the
program folder (only a folder the installer created, marked by `HolidayLights.install.json`), and moves your bulbs,
songs and pictures and your settings and themes to the Recycle Bin only when you ask. It never touches Holiday Lights
5.4.

For tests, `--data-root <dir>` installs into `<dir>\Programs\HolidayLights` (Start menu entry under `<dir>\Start Menu`)
and `--no-system-changes` skips every registry and shortcut write (they are logged instead).
