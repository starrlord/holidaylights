<#
.SYNOPSIS
    Builds the Holiday Lights 6 setup (owner: app-shell; PRODUCT-SPEC 6.10).

.DESCRIPTION
    Publishes src/HolidayLights.App for win-x64, lays out the distribution folder

        <output>\Holiday Lights <version>\
            HolidayLights.exe, HolidayLights.dll and its libraries, with the .NET runtime (self-contained)
            Content\Bulbs (1,501 .bul), Content\Music (46 songs), Content\Pictures (11 pictures)
            Assets\BulbDocument.ico

    and packs it into one file with Inno Setup (tools/publish/HolidayLights.iss):

        <output>\HolidayLights-<version>-Setup.exe

    Double-clicking the setup unpacks the folder into a temporary folder and runs the program's per-user installer
    (HolidayLights.exe --install), which installs into %LOCALAPPDATA%\Programs\HolidayLights without administrator rights,
    or updates the installation that is there; the installer creates "Holiday Lights.scr" next to the program. Nothing is
    installed, registered or started by this script. The pinned Inno Setup compiler comes from Get-InnoSetup.ps1.

    The output goes under $env:HL_BUILD_ROOT when it is set (parallel builds), else under <repo>\artifacts.

.PARAMETER Output
    The folder that receives the distribution folder and the setup.

.PARAMETER Configuration
    The build configuration (Release by default).
#>
[CmdletBinding()]
param(
    [string] $Output,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$buildRoot = if ($env:HL_BUILD_ROOT) { $env:HL_BUILD_ROOT } else { Join-Path $repo 'artifacts' }
if (-not $Output) {
    $Output = Join-Path $buildRoot 'dist'
}

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)

[xml] $props = Get-Content -Raw (Join-Path $repo 'Directory.Build.props')
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
$fileVersionNode = $props.SelectSingleNode('/Project/PropertyGroup/FileVersion')
if (-not $versionNode -or -not $versionNode.InnerText.Trim() -or -not $fileVersionNode -or -not $fileVersionNode.InnerText.Trim()) {
    throw 'The version or the file version was not found in Directory.Build.props.'
}
$version = $versionNode.InnerText.Trim()
$fileVersion = $fileVersionNode.InnerText.Trim()

$stage = Join-Path $buildRoot "publish\HolidayLights-$version"
$dist = Join-Path $Output "Holiday Lights $version"
$setup = Join-Path $Output "HolidayLights-$version-Setup.exe"

foreach ($previous in @($stage, $dist, $setup)) {
    if (Test-Path -LiteralPath $previous) {
        Remove-Item -LiteralPath $previous -Recurse -Force
    }
}

Write-Host "Publishing Holiday Lights $version ($Configuration, self-contained)"
& dotnet publish (Join-Path $repo 'src\HolidayLights.App\HolidayLights.App.csproj') `
    -c $Configuration -r win-x64 --self-contained true -o $stage `
    -p:GenerateDocumentationFile=false -p:DebugType=none
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item -Path (Join-Path $stage '*') -Destination $dist -Recurse -Force

# HolidayLights.exe and the installed "Holiday Lights.scr" are copies of the same launcher: each must find the runtime
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

$counts = @{
    Bulbs = (Get-ChildItem -LiteralPath (Join-Path $dist 'Content\Bulbs') -Filter '*.bul').Count
    Music = (Get-ChildItem -LiteralPath (Join-Path $dist 'Content\Music') -Filter '*.mid').Count
    Pictures = (Get-ChildItem -LiteralPath (Join-Path $dist 'Content\Pictures') -Filter '*.bmp').Count
}
if ($counts.Bulbs -ne 1501 -or $counts.Music -ne 46 -or $counts.Pictures -ne 11) {
    throw "Unexpected bundled content: $($counts.Bulbs) bulbs, $($counts.Music) songs, $($counts.Pictures) pictures."
}

$compiler = & (Join-Path $PSScriptRoot 'Get-InnoSetup.ps1')
Write-Host "Building the setup with $compiler"
& $compiler /Q "/DAppVersion=$version" "/DFileVersion=$fileVersion" "/DSourceDir=$dist" "/DOutputDir=$Output" (Join-Path $PSScriptRoot 'HolidayLights.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $setup)) {
    throw "Inno Setup did not produce $setup."
}

Write-Host "Distribution folder: $dist"
Write-Host "Setup: $setup"
