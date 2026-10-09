<#
.SYNOPSIS
    Builds the files of a GitHub release: both zips, SHA256SUMS.txt and the release notes (docs/RELEASING.md).

.DESCRIPTION
    Runs tools/publish/publish.ps1 twice and collects the results in <Output>\assets:

        HolidayLights-<version>-win-x64.zip                        self-contained: the .NET runtime is included, so
                                                                   it runs on any Windows 11 PC (the download for most people)
        HolidayLights-<version>-win-x64-framework-dependent.zip    smaller; needs the .NET 10 Desktop Runtime (x64)
        SHA256SUMS.txt                                             SHA-256 of both zips (sha256sum format)

    and writes <Output>\release-notes.md from .github/release-notes-template.md. Each zip is opened and checked
    (program, Setup.exe, Read Me, the .NET runtime only in the self-contained one). Nothing is installed or started.

    The version comes from Directory.Build.props, as in publish.ps1. On GitHub Actions the paths are written to
    $GITHUB_OUTPUT (assets, notes) and a table of the files to the job summary.

.PARAMETER Output
    The folder for staging\, assets\ and release-notes.md. Defaults to $env:RUNNER_TEMP\release on GitHub Actions,
    else to $env:HL_BUILD_ROOT\release or <repo>\artifacts\release. Earlier staging\, assets\ and release-notes.md
    there are replaced; nothing else in the folder is touched.

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
Add-Type -AssemblyName System.IO.Compression.FileSystem

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
$assets = Join-Path $Output 'assets'
$notesPath = Join-Path $Output 'release-notes.md'

# Only what this script makes is removed, never anything else in the folder.
foreach ($previous in @($staging, $assets, $notesPath)) {
    if (Test-Path -LiteralPath $previous) {
        Remove-Item -LiteralPath $previous -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $staging, $assets | Out-Null

# publish.ps1 runs in its own PowerShell process, exactly as a developer runs it.
$powershell = (Get-Process -Id $PID).Path
$variants = @(
    [pscustomobject] @{ Name = 'self-contained'; Switches = @(); Asset = "HolidayLights-$version-win-x64.zip"; HasRuntime = $true }
    [pscustomobject] @{ Name = 'framework-dependent'; Switches = @('-FrameworkDependent'); Asset = "HolidayLights-$version-win-x64-framework-dependent.zip"; HasRuntime = $false }
)

foreach ($variant in $variants) {
    $variantOutput = Join-Path $staging $variant.Name
    $switches = @($variant.Switches)
    Write-Host "::group::publish.ps1 ($($variant.Name))"
    & $powershell -NoProfile -NonInteractive -File $publishScript -Output $variantOutput -Configuration $Configuration @switches
    $publishExit = $LASTEXITCODE
    Write-Host '::endgroup::'
    if ($publishExit -ne 0) {
        throw "publish.ps1 ($($variant.Name)) failed with exit code $publishExit."
    }

    $zip = Join-Path $variantOutput "HolidayLights-$version-win-x64.zip"
    if (-not (Test-Path -LiteralPath $zip)) {
        throw "publish.ps1 ($($variant.Name)) did not produce $zip."
    }

    # The zip holds one folder, "Holiday Lights <version>", with the program, the installer and the Read Me.
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    }
    finally {
        $archive.Dispose()
    }

    $folder = "Holiday Lights $version/"
    foreach ($required in @('HolidayLights.exe', 'HolidayLights.dll', 'Setup.exe', 'Read Me.txt', 'Assets/BulbDocument.ico')) {
        if ($entries -notcontains "$folder$required") {
            throw "$($variant.Asset) is missing $folder$required."
        }
    }

    $bulbs = @($entries | Where-Object { $_ -like "${folder}Content/Bulbs/*.bul" }).Count
    if ($bulbs -ne 1501) {
        throw "$($variant.Asset) holds $bulbs bulbs instead of 1501."
    }

    $hasRuntime = $entries -contains "${folder}coreclr.dll"
    if ($hasRuntime -ne $variant.HasRuntime) {
        throw "$($variant.Asset): the .NET runtime is $(if ($hasRuntime) { 'included' } else { 'missing' }), which is wrong for a $($variant.Name) build."
    }

    Copy-Item -LiteralPath $zip -Destination (Join-Path $assets $variant.Asset)
}

$checksums = foreach ($variant in $variants) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $assets $variant.Asset) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($variant.Asset)"
}

# LF line endings, as sha256sum writes and reads them.
$checksumsPath = Join-Path $assets 'SHA256SUMS.txt'
[System.IO.File]::WriteAllText($checksumsPath, (($checksums -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))

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
    '{{SELF_CONTAINED_ZIP}}' = $variants[0].Asset
    '{{FRAMEWORK_DEPENDENT_ZIP}}' = $variants[1].Asset
    '{{CHECKSUMS}}' = ($checksums -join "`n")
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

$table = [System.Text.StringBuilder]::new()
[void] $table.AppendLine("## Release files for $Tag")
[void] $table.AppendLine()
[void] $table.AppendLine('| File | Size | SHA-256 |')
[void] $table.AppendLine('|---|---:|---|')
foreach ($variant in $variants) {
    $file = Get-Item -LiteralPath (Join-Path $assets $variant.Asset)
    $hash = ($checksums | Where-Object { $_.EndsWith("  $($variant.Asset)") }).Split(' ')[0]
    [void] $table.AppendLine(("| {0} | {1:N1} MB | ``{2}`` |" -f $variant.Asset, ($file.Length / 1MB), $hash))
}

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
