# tools/ci - the scripts of the GitHub Actions workflows

The CI and Release workflows (`.github/workflows`) call these scripts, so every step also runs on your own PC.
How to cut a release, and which tests GitHub leaves out, is in [docs/RELEASING.md](../../docs/RELEASING.md).

| Script | Used by | Does |
|---|---|---|
| `Test.ps1 [-Configuration Release] [-NoBuild] [-ResultsDirectory <folder>]` | CI, Release | runs the tests without the `Live`, `Desktop` and `Audio` categories; writes a TRX file, and on GitHub the job summary and an annotation per failed test |
| `Get-ReleaseVersion.ps1 -Tag v6.0.1` | Release | fails unless the tag is `v<major>.<minor>.<patch>[-<pre-release>]` and matches `<Version>`, `<FileVersion>` and `<AssemblyVersion>` in `Directory.Build.props`; writes `version`, `tag` and `prerelease` to `$GITHUB_OUTPUT` |
| `New-ReleaseAssets.ps1 [-Output <folder>] [-Tag v6.0.1]` | Release | runs `tools/publish/publish.ps1` for the self-contained and the framework-dependent build, checks both zips, and writes `assets\` (both zips and `SHA256SUMS.txt`) and `release-notes.md` (from `.github/release-notes-template.md`) |
| `Publish-GitHubRelease.ps1 -Folder <folder> [-DryRun]` | Release | checks the files against `SHA256SUMS.txt` and creates the GitHub release with `gh`; `-DryRun` prints the `gh` command instead |

Without `-Output` or `-ResultsDirectory`, output goes to `$env:RUNNER_TEMP` on GitHub, else to `$env:HL_BUILD_ROOT` or
`<repo>\artifacts`. Nothing is installed, registered or started.
