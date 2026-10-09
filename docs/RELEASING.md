# Releasing Holiday Lights

Two GitHub Actions workflows build Holiday Lights. **CI** checks every change. **Release** turns a version tag into a
GitHub release with the downloads. Both run on a GitHub-hosted Windows runner (`windows-latest`) with the .NET SDK that
`global.json` names.

| Workflow | Runs on | What it does |
|---|---|---|
| CI (`.github/workflows/ci.yml`) | every push to a branch, every pull request, or by hand | restore, build `Release` with warnings as errors, run the CI tests, upload the test results (`test-results` artifact, summary on the run page) |
| Release (`.github/workflows/release.yml`) | a pushed tag `v*`, or by hand | the same build and tests, then the setup (`tools/publish/publish.ps1`, checked by installing it), `SHA256SUMS.txt` and the release notes, then the GitHub release |

The scripts the workflows run live in `tools/ci` and work on your own PC too:

| Script | Does |
|---|---|
| `Test.ps1` | runs the tests CI runs (see below), writes TRX results and, on GitHub, the job summary and failure annotations |
| `Get-ReleaseVersion.ps1 -Tag v6.0.1` | checks that a tag matches the version in `Directory.Build.props` |
| `New-ReleaseAssets.ps1` | runs `publish.ps1`, installs the setup silently twice into a private data root (a new installation, then an update over it) and checks what it installed, writes `SHA256SUMS.txt` and `release-notes.md` |
| `Publish-GitHubRelease.ps1` | creates the GitHub release with `gh` (only in the workflow; `-DryRun` prints the commands) |

## Tests on GitHub

A GitHub runner is a virtual machine: its screen is small (usually 1024 x 768 at 100 %), it has no real graphics card
or sound device, and nobody sits at its desktop. Tests that need a real PC carry a category, and CI leaves them out
with the filter `Category!=Live&Category!=Desktop&Category!=Audio` (in `tools/ci/Test.ps1`):

| Category | The test needs | Tests |
|---|---|---|
| `Live` | someone watching: it shows real lights or the screen saver on the real displays, or plays sound (opt-in, see `tests/HolidayLights.Tests/Rendering/README.md`) | `Rendering/Live/*`, `ScreenSaver/SaverLiveChecks`, the two `Live_` tests in `Audio/AudioHardwareTests` |
| `Desktop` | the real interactive desktop: Explorer's desktop windows, the real displays and their scaling, DXGI outputs, the session's live state, or a screen larger than 1024 x 768 | `DesktopHostLocatorTests.ThisDesktop_IsFoundWithoutChangingIt`, `DisplayServiceTests.RealDisplays_AreReadInPhysicalPixels`, `SystemProbeTests.Wallpaper_IsReadForEveryDisplayWithoutThrowing`, `PauseSignalTests.Start_ReadsTheSignalsOfThisMachine`, `AdvancedColorTests.Refresh_ReadsTheOutputsOfThisPc`, `SettingsSnapshotTests.EveryPageRendersInLightAndDarkThemes` (the window is 1240 x 800) |
| `Audio` | a sound output or a MIDI synthesizer that opens | the other three tests in `Audio/AudioHardwareTests` |

Everything else runs on GitHub, including the WPF window tests (off-screen, rendered at 150 %) and the performance
budgets, which pass on this project's reference PC with at least five times the margin they need.

Give a new test `[Trait("Category", "Desktop")]` or `[Trait("Category", "Audio")]` when it reads this PC's displays,
desktop or devices, or needs a window larger than 1024 x 768.

To run the tests:

```powershell
# What CI runs (builds first; add -NoBuild after a build)
pwsh -NoProfile -File tools/ci/Test.ps1

# Everything but the Live probes, on your own PC: do this before every release
dotnet test HolidayLights.sln -c Release --filter "Category!=Live"
```

## Cutting a release

1. **Choose the version** ([semantic versioning](https://semver.org)): `6.0.1` for fixes, `6.1.0` for new features,
   `6.1.0-beta.1` for a pre-release.
2. **Set it everywhere it is written:**
   * `Directory.Build.props`: `<Version>6.0.1</Version>`, `<AssemblyVersion>6.0.1.0</AssemblyVersion>` and
     `<FileVersion>6.0.1.0</FileVersion>`. The last two are four numbers only, so a pre-release `6.1.0-beta.1` uses
     `6.1.0.0`.
   * `src/HolidayLights.App/app.manifest`: `<assemblyIdentity version="6.0.1.0" .../>`.
3. **Test on your PC**: build, then run everything but the Live probes (above). The Desktop and Audio tests run only
   here.
4. **Commit and push**, and wait for CI to pass.
5. **Tag the commit and push the tag:**

   ```powershell
   git tag -a v6.0.1 -m "Holiday Lights 6.0.1"
   git push origin v6.0.1
   ```

6. **Watch Actions > Release.** When it finishes (in about 10 minutes), the release is on the Releases page, titled
   "Holiday Lights 6.0.1", with:

   | File | For |
   |---|---|
   | `HolidayLights-6.0.1-Setup.exe` | everyone (about 65 MB): one file with the .NET runtime inside, so it works on any Windows 11 PC, offline, without administrator rights; it installs Holiday Lights or updates the installed version |
   | `SHA256SUMS.txt` | the SHA-256 of the setup, in `sha256sum` format |

   The text comes from `.github/release-notes-template.md`, followed by GitHub's list of changes since the previous
   release. A tag with a pre-release suffix (`v6.1.0-beta.1`) makes a pre-release. Edit the text on the Releases page
   if you like.

The release workflow stops, with a message that says what to change, when:

* the tag is not `v<major>.<minor>.<patch>` with an optional `-<pre-release>` suffix;
* `<Version>` in `Directory.Build.props` is not the tag without its `v`, or `<FileVersion>`/`<AssemblyVersion>` start
  with other numbers (a stale `app.manifest` version is only a warning);
* a test fails, `publish.ps1` fails (it checks the bundled 1,501 bulbs, 46 songs and 11 pictures and the bundled
  runtime), or installing the setup fails or leaves out the program, the screen saver, the runtime or the bulbs;
* a release for the tag already exists, or (when run by hand from a branch) the tag already exists on another commit.

## Running the release workflow by hand

**Actions > Release > Run workflow** offers:

* **Use workflow from**: a tag (to build that tag again, for example after a failed run) or a branch (then fill in
  **tag**: the tag is created on that branch's latest commit; for a draft, when you publish it).
* **draft** (on by default): the release is created as a draft. Check it, then press **Publish release**. A pushed tag
  always publishes at once.
* **create-release**: clear it to build, test and package only. The setup, `SHA256SUMS.txt` and `release-notes.md` are
  then in the run's `release-v<version>` artifact. Use this to try the release before the first real tag.

## If something goes wrong

* **"Release version check failed"**: the tag and `Directory.Build.props` disagree. Fix the version, commit, delete the
  tag (`git tag -d v6.0.1` and `git push --delete origin v6.0.1`) and tag the new commit.
* **"A release for v6.0.1 already exists"**: delete that release on the Releases page (and its tag, if it names the wrong
  commit), then run the workflow again from the tag.
* **A test fails only on GitHub**: when it needs the real desktop or devices, give it the `Desktop` or `Audio` category;
  otherwise fix it. The `test-results` artifact holds the TRX file (and a memory dump if a test hung for 10 minutes).

## Notes for maintainers

* Permissions: both workflows read the repository only. The release's last job (`publish`) alone may write, to create
  the release; it runs only `gh` and `tools/ci/Publish-GitHubRelease.ps1`, none of the built code. The repository's default workflow permissions can stay read-only.
* The actions are pinned to major versions (`actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/cache@v6`,
  `actions/upload-artifact@v7`, `actions/download-artifact@v8`); the NuGet packages are cached between runs.
* The setup is built by Inno Setup, pinned in `tools/publish/Get-InnoSetup.ps1` (version and SHA-256 of the official
  installer, downloaded on first use and installed portable under the build folder). To move to a newer Inno Setup,
  change the three values there.
* The downloads are not code-signed, so Windows SmartScreen warns the first time the setup runs (the release notes
  tell people what to do). With a code-signing certificate, sign `HolidayLights.exe` in `tools/publish/publish.ps1`
  before Inno Setup packs the folder, and the setup itself with Inno Setup's `SignTool` directive.
* Optionally, protect the `v*` tags with a tag ruleset (Settings > Rules) so that only maintainers can start a release.
