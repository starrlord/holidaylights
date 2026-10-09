<#
.SYNOPSIS
    Builds the files of a GitHub release: the setup, SHA256SUMS.txt and the release notes (docs/RELEASING.md).

.DESCRIPTION
    Runs tools/publish/publish.ps1 and collects the results in <Output>\assets:

        HolidayLights-<version>-Setup.exe    the one download: unpacks itself and runs the per-user installer, which
                                             installs Holiday Lights or updates the installed version
        SHA256SUMS.txt                       SHA-256 of the setup (sha256sum format)

    and writes <Output>\release-notes.md from .github/release-notes-template.md.

    The setup is checked by installing it silently twice (a new installation, then installing over it) into a private
    data root with --no-system-changes under <Output>\install-check: each time it must exit with 0 and leave the program,
    the screen saver copy, the bundled bulbs, the .NET runtime and the install marker of this version. Nothing is
    installed for the user, registered or started otherwise.

    The version comes from Directory.Build.props, as in publish.ps1. On GitHub Actions the paths are written to
    $GITHUB_OUTPUT (assets, notes) and a table of the files to the job summary.

.PARAMETER Output
    The folder for staging\, install-check\, assets\ and release-notes.md. Defaults to $env:RUNNER_TEMP\release on
    GitHub Actions, else to $env:HL_BUILD_ROOT\release or <repo>\artifacts\release. Earlier staging\, install-check\,
    assets\ and release-notes.md there are replaced; nothing else in the folder is touched.

.PARAMETER Tag
    The tag the notes name (default v<version>).

.PARAMETER Configuration
    The build configuration (Release by default).
#>
[CmdletBinding()]
param(
    [string] $Output,
    [string] $Tag,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$publishScript = Join-Path $repo 'tools\publish\publish.ps1'
$templatePath = Join-Path $repo '.github\release-notes-template.md'

[xml] $props = Get-Content -Raw -LiteralPath (Join-Path $repo 'Directory.Build.props')
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
if (-not $versionNode -or -not $versionNode.InnerText.Trim()) {
    throw 'The version was not found in Directory.Build.props.'
}

$version = $versionNode.InnerText.Trim()
$projectUrlNode = $props.SelectSingleNode('/Project/PropertyGroup/PackageProjectUrl')
if (-not $Tag) {
    $Tag = "v$version"
}

if (-not $Output) {
    $base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } elseif ($env:HL_BUILD_ROOT) { $env:HL_BUILD_ROOT } else { Join-Path $repo 'artifacts' }
    $Output = Join-Path $base 'release'
}

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$staging = Join-Path $Output 'staging'
$installCheck = Join-Path $Output 'install-check'
$assets = Join-Path $Output 'assets'
$notesPath = Join-Path $Output 'release-notes.md'

# Only what this script makes is removed, never anything else in the folder.
foreach ($previous in @($staging, $installCheck, $assets, $notesPath)) {
    if (Test-Path -LiteralPath $previous) {
        Remove-Item -LiteralPath $previous -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $staging, $assets | Out-Null

# publish.ps1 runs in its own PowerShell process, exactly as a developer runs it.
$powershell = (Get-Process -Id $PID).Path
Write-Host '::group::publish.ps1'
& $powershell -NoProfile -NonInteractive -File $publishScript -Output $staging -Configuration $Configuration
$publishExit = $LASTEXITCODE
Write-Host '::endgroup::'
if ($publishExit -ne 0) {
    throw "publish.ps1 failed with exit code $publishExit."
}

$assetName = "HolidayLights-$version-Setup.exe"
$setup = Join-Path $staging $assetName
if (-not (Test-Path -LiteralPath $setup)) {
    throw "publish.ps1 did not produce $setup."
}

# Install it as users will (silently, so no window waits for a click), into a private data root, twice: the second run
# installs over the first, as an update does.
$program = Join-Path $installCheck 'Programs\HolidayLights'
foreach ($run in @('new installation', 'installing over it')) {
    Write-Host "Checking the setup: $run"
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '--data-root', "`"$installCheck`"", '--no-system-changes')
    $process = Start-Process -FilePath $setup -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        $log = Join-Path $installCheck 'Local\Logs\HolidayLights.log'
        $tail = if (Test-Path -LiteralPath $log) { (Get-Content -LiteralPath $log -Tail 20) -join "`n" } else { '(no log)' }
        throw "The setup ($run) exited with code $($process.ExitCode). The end of its log:`n$tail"
    }

    foreach ($required in @('HolidayLights.exe', 'HolidayLights.dll', 'Holiday Lights.scr', 'coreclr.dll', 'Assets\BulbDocument.ico')) {
        if (-not (Test-Path -LiteralPath (Join-Path $program $required))) {
            throw "The setup ($run) did not install $required."
        }
    }

    $bulbs = @(Get-ChildItem -LiteralPath (Join-Path $program 'Content\Bulbs') -Filter '*.bul').Count
    if ($bulbs -ne 1501) {
        throw "The setup ($run) installed $bulbs bulbs instead of 1501."
    }

    $marker = Get-Content -Raw -LiteralPath (Join-Path $program 'HolidayLights.install.json') | ConvertFrom-Json
    if ($marker.version -ne $version) {
        throw "The setup ($run) installed version $($marker.version) instead of $version."
    }
}

Copy-Item -LiteralPath $setup -Destination (Join-Path $assets $assetName)
$hash = (Get-FileHash -LiteralPath (Join-Path $assets $assetName) -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = "$hash  $assetName"

# LF line endings, as sha256sum writes and reads them.
$checksumsPath = Join-Path $assets 'SHA256SUMS.txt'
[System.IO.File]::WriteAllText($checksumsPath, "$checksum`n", [System.Text.UTF8Encoding]::new($false))

$repositoryUrl = if ($env:GITHUB_SERVER_URL -and $env:GITHUB_REPOSITORY) {
    "$env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY"
}
elseif ($projectUrlNode) {
    $projectUrlNode.InnerText.Trim()
}
else {
    throw 'The repository URL is unknown: set GITHUB_REPOSITORY or <PackageProjectUrl> in Directory.Build.props.'
}

$commit = if ($env:GITHUB_SHA) {
    "commit [``$($env:GITHUB_SHA.Substring(0, 7))``]($repositoryUrl/commit/$env:GITHUB_SHA)"
}
else {
    'a local working copy (not a GitHub build)'
}
$values = [ordered] @{
    '{{VERSION}}' = $version
    '{{TAG}}' = $Tag
    '{{SETUP_EXE}}' = $assetName
    '{{CHECKSUMS}}' = $checksum
    '{{REPOSITORY_URL}}' = $repositoryUrl
    '{{COMMIT}}' = $commit
}

$notes = Get-Content -Raw -LiteralPath $templatePath
foreach ($key in $values.Keys) {
    $notes = $notes.Replace($key, [string] $values[$key])
}

$unknown = [regex]::Matches($notes, '\{\{[A-Z_]+\}\}') | ForEach-Object Value | Sort-Object -Unique
if ($unknown) {
    throw ".github/release-notes-template.md uses placeholders this script does not fill: $($unknown -join ', ')."
}

[System.IO.File]::WriteAllText($notesPath, $notes.Replace("`r`n", "`n"), [System.Text.UTF8Encoding]::new($false))

$file = Get-Item -LiteralPath (Join-Path $assets $assetName)
$table = [System.Text.StringBuilder]::new()
[void] $table.AppendLine("## Release files for $Tag")
[void] $table.AppendLine()
[void] $table.AppendLine('| File | Size | SHA-256 |')
[void] $table.AppendLine('|---|---:|---|')
[void] $table.AppendLine(("| {0} | {1:N1} MB | ``{2}`` |" -f $assetName, ($file.Length / 1MB), $hash))

if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $table.ToString() -Encoding utf8
}

Write-Host $table.ToString()
Write-Host "Assets: $assets"
Write-Host "Release notes: $notesPath"

if ($env:GITHUB_OUTPUT) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Value @(
        "assets=$assets"
        "notes=$notesPath"
    )
}
