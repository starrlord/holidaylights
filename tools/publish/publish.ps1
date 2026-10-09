<#
.SYNOPSIS
    Builds the Holiday Lights 6 distributable (owner: app-shell; PRODUCT-SPEC 6.10).

.DESCRIPTION
    Publishes src/HolidayLights.App for win-x64 and lays out the distribution folder

        <output>\Holiday Lights <version>\
            HolidayLights.exe, HolidayLights.dll and its libraries, with the .NET runtime (self-contained)
            Content\Bulbs (1,501 .bul), Content\Music (46 songs), Content\Pictures (11 pictures)
            Assets\BulbDocument.ico
            Setup.exe      a copy of the program: started without arguments it is the per-user installer
            Read Me.txt

    and zips it as <output>\HolidayLights-<version>-win-x64.zip. Double-clicking Setup.exe (in the extracted folder)
    installs into %LOCALAPPDATA%\Programs\HolidayLights without administrator rights; the installer creates
    "Holiday Lights.scr" next to the program. Nothing is installed, registered or started by this script.

    The output goes under $env:HL_BUILD_ROOT when it is set (parallel builds), else under <repo>\artifacts.

.PARAMETER Output
    The folder that receives the distribution folder and the zip.

.PARAMETER Configuration
    The build configuration (Release by default).

.PARAMETER FrameworkDependent
    For developers only: leave the .NET runtime out (the program then needs the .NET 10 Desktop Runtime, x64, which
    Windows does not include and which installs per machine with administrator rights). By default the .NET runtime
    is bundled (self-contained, not single-file), so Setup.exe works on a stock Windows 11 PC, offline and without
    administrator rights (PRODUCT-SPEC 6.10, 6.11).
#>
[CmdletBinding()]
param(
    [string] $Output,
    [string] $Configuration = 'Release',
    [switch] $FrameworkDependent
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$buildRoot = if ($env:HL_BUILD_ROOT) { $env:HL_BUILD_ROOT } else { Join-Path $repo 'artifacts' }
if (-not $Output) {
    $Output = Join-Path $buildRoot 'dist'
}

[xml] $props = Get-Content -Raw (Join-Path $repo 'Directory.Build.props')
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
if (-not $versionNode -or -not $versionNode.InnerText.Trim()) {
    throw 'The version was not found in Directory.Build.props.'
}
$version = $versionNode.InnerText.Trim()

$stage = Join-Path $buildRoot "publish\HolidayLights-$version"
$dist = Join-Path $Output "Holiday Lights $version"
$zip = Join-Path $Output "HolidayLights-$version-win-x64.zip"

foreach ($folder in @($stage, $dist)) {
    if (Test-Path $folder) {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
}

$selfContained = -not $FrameworkDependent
Write-Host "Publishing Holiday Lights $version ($Configuration, $(if ($selfContained) { 'self-contained' } else { 'framework-dependent' }))"
$selfContainedValue = if ($selfContained) { 'true' } else { 'false' }
& dotnet publish (Join-Path $repo 'src\HolidayLights.App\HolidayLights.App.csproj') `
    -c $Configuration -r win-x64 --self-contained $selfContainedValue -o $stage `
    -p:GenerateDocumentationFile=false -p:DebugType=none
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item -Path (Join-Path $stage '*') -Destination $dist -Recurse -Force
Copy-Item -LiteralPath (Join-Path $dist 'HolidayLights.exe') -Destination (Join-Path $dist 'Setup.exe') -Force

if ($selfContained) {
    # Setup.exe, HolidayLights.exe and "Holiday Lights.scr" are copies of the same launcher: each must find the runtime
    # next to itself, never a machine-wide .NET.
    foreach ($runtimeFile in @('hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'PresentationFramework.dll', 'wpfgfx_cor3.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $dist $runtimeFile))) {
            throw "The self-contained publish is missing $runtimeFile."
        }
    }
    $runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $dist 'HolidayLights.runtimeconfig.json') | ConvertFrom-Json
    if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'frameworks' -or $runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'framework') {
        throw 'The self-contained publish still asks for a shared .NET runtime.'
    }
}

$counts = @{
    Bulbs = (Get-ChildItem -LiteralPath (Join-Path $dist 'Content\Bulbs') -Filter '*.bul').Count
    Music = (Get-ChildItem -LiteralPath (Join-Path $dist 'Content\Music') -Filter '*.mid').Count
    Pictures = (Get-ChildItem -LiteralPath (Join-Path $dist 'Content\Pictures') -Filter '*.bmp').Count
}
if ($counts.Bulbs -ne 1501 -or $counts.Music -ne 46 -or $counts.Pictures -ne 11) {
    throw "Unexpected bundled content: $($counts.Bulbs) bulbs, $($counts.Music) songs, $($counts.Pictures) pictures."
}

$readMe = @(
    "Holiday Lights - Modern Edition $version"
    ''
    'To install Holiday Lights, extract this folder, then double-click Setup.exe.'
    'No administrator rights are needed: Holiday Lights is installed for you only, in'
    '%LOCALAPPDATA%\Programs\HolidayLights. To remove it later, use Windows Settings > Apps.'
    ''
)
if (-not $selfContained) {
    $readMe += 'This developer build needs the .NET 10 Desktop Runtime (x64) from Microsoft.'
    $readMe += ''
}
$readMe += 'Holiday Lights works completely offline and never collects information about you.'
$readMe += ''
$readMe += 'Holiday Lights 6, the modern edition, was created by StarrLord. Its source code is open source under the'
$readMe += 'MIT License; the bulbs, music and pictures remain the copyright of their authors.'
$readMe += 'Project home: https://github.com/starrlord/holidaylights'
Set-Content -LiteralPath (Join-Path $dist 'Read Me.txt') -Value ($readMe -join "`r`n") -Encoding utf8

if (Test-Path $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path $dist -DestinationPath $zip

Write-Host "Distribution folder: $dist"
Write-Host "Zip: $zip"
