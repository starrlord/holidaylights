<#
.SYNOPSIS
    Creates the GitHub release from the files New-ReleaseAssets.ps1 made (release workflow; docs/RELEASING.md).

.DESCRIPTION
    Checks the downloaded files again (both zips against SHA256SUMS.txt), refuses to touch a release that already
    exists, makes sure an existing tag points at the commit that was built, and then runs

        gh release create <tag> <zips> SHA256SUMS.txt --title "Holiday Lights <version>"
            --notes-file release-notes.md --generate-notes [--prerelease] [--draft] (--verify-tag | --target <commit>)

    It reads these environment variables (set by the workflow): TAG, VERSION, PRERELEASE and DRAFT (true or false),
    TAG_PUSHED (true when the run is for the tag itself), COMMIT (the commit that was built), GH_REPO and GH_TOKEN.

.PARAMETER Folder
    The folder that holds assets\ and release-notes.md.

.PARAMETER DryRun
    Check the files and print the gh commands instead of calling GitHub.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Folder,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-Setting([string] $Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "The environment variable $Name is not set."
    }

    $value.Trim()
}

$tag = Get-Setting 'TAG'
$version = Get-Setting 'VERSION'
$commit = Get-Setting 'COMMIT'
$repository = Get-Setting 'GH_REPO'
$prerelease = (Get-Setting 'PRERELEASE') -eq 'true'
$draft = (Get-Setting 'DRAFT') -eq 'true'
$tagPushed = (Get-Setting 'TAG_PUSHED') -eq 'true'

$Folder = (Resolve-Path -LiteralPath $Folder).Path
$assetsFolder = Join-Path $Folder 'assets'
$notes = Join-Path $Folder 'release-notes.md'
if (-not (Test-Path -LiteralPath $notes)) {
    throw "The release notes are missing: $notes."
}

$sumsPath = Join-Path $assetsFolder 'SHA256SUMS.txt'
$expectedZips = @("HolidayLights-$version-win-x64.zip", "HolidayLights-$version-win-x64-framework-dependent.zip")
$found = @(Get-ChildItem -LiteralPath $assetsFolder -File | ForEach-Object Name)
$missing = @($expectedZips + 'SHA256SUMS.txt' | Where-Object { $found -notcontains $_ })
if ($missing.Count -gt 0 -or $found.Count -ne 3) {
    throw "Expected $($expectedZips -join ', ') and SHA256SUMS.txt in $assetsFolder; found: $($found -join ', ')."
}

# The self-contained zip first: it is the download for most people.
$zips = @($expectedZips | ForEach-Object { Get-Item -LiteralPath (Join-Path $assetsFolder $_) })

# The files crossed from one job to the other: they must still be the ones that were hashed.
$sums = @{}
foreach ($line in [System.IO.File]::ReadAllLines($sumsPath)) {
    if ($line -match '^(?<hash>[0-9a-f]{64})  (?<file>.+)$') {
        $sums[$Matches.file] = $Matches.hash
    }
}

foreach ($zip in $zips) {
    $actual = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $sums.ContainsKey($zip.Name) -or $sums[$zip.Name] -ne $actual) {
        throw "$($zip.Name) does not match SHA256SUMS.txt."
    }
}

function Invoke-Gh([string[]] $Arguments, [switch] $AllowFailure) {
    if ($DryRun) {
        Write-Host "[dry run] gh $($Arguments -join ' ')"
        return $null
    }

    # gh writes its errors to stderr; collect them as text instead of letting them stop the script.
    $ErrorActionPreference = 'Continue'
    $output = & gh @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $global:LASTEXITCODE = 0
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "gh $($Arguments[0..1] -join ' ') failed with exit code ${exitCode}: $($output -join ' ')"
    }

    if ($exitCode -ne 0) { $null } else { ($output | ForEach-Object { "$_" }) -join "`n" }
}

if (-not $DryRun) {
    $existing = Invoke-Gh @('release', 'view', $tag, '--json', 'url', '--jq', '.url') -AllowFailure
    if ($existing) {
        throw "A release for $tag already exists: $existing. Delete that release (and its tag, if it points at the wrong commit) or release a new version (docs/RELEASING.md)."
    }
}

$tagExists = $tagPushed
if (-not $tagPushed -and -not $DryRun) {
    # Run by hand from a branch: an existing tag must name the commit this run built.
    $tagCommit = Invoke-Gh @('api', "repos/$repository/commits/tags/$tag", '--jq', '.sha') -AllowFailure
    if ($tagCommit) {
        if ($tagCommit.Trim() -ne $commit) {
            throw "The tag $tag already exists at commit $($tagCommit.Trim()), but this run built $commit. Run the workflow from the tag $tag instead, or choose another version."
        }

        $tagExists = $true
    }
}

$arguments = @('release', 'create', $tag) + @($zips.FullName) + @($sumsPath) + @(
    '--title', "Holiday Lights $version",
    '--notes-file', $notes,
    '--generate-notes'
)
if ($prerelease) {
    $arguments += '--prerelease'
}

if ($draft) {
    $arguments += '--draft'
}

if ($tagExists) {
    $arguments += '--verify-tag'
}
else {
    $arguments += @('--target', $commit)
}

$url = Invoke-Gh $arguments
$kind = if ($draft) { 'Draft release' } elseif ($prerelease) { 'Pre-release' } else { 'Release' }
if ($DryRun) {
    Write-Host "[dry run] $kind $tag would be created."
}
else {
    Write-Host "$kind $tag created: $url"
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value "## $kind [$tag]($url)"
    }
}
