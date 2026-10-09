<#
.SYNOPSIS
    Returns the path of the Inno Setup compiler (ISCC.exe) that builds the Holiday Lights setup, installing it first
    when it is not there yet.

.DESCRIPTION
    Every build uses the same pinned Inno Setup, in a private folder under the build output:

        <build root>\tools\InnoSetup-<version>\ISCC.exe

    (<build root> is $env:HL_BUILD_ROOT when set, else <repo>\artifacts.) The first time, the official installer is
    downloaded from the Inno Setup GitHub releases, its SHA-256 is checked, and it is installed there in portable mode
    for this user: no administrator rights, no Start menu entry, no Apps entry, no file associations. Delete the folder
    to remove it.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$innoVersion = '6.7.3'
$installerUrl = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$installerSha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$buildRoot = if ($env:HL_BUILD_ROOT) { $env:HL_BUILD_ROOT } else { Join-Path $repo 'artifacts' }
$folder = Join-Path $buildRoot "tools\InnoSetup-$innoVersion"
$compiler = Join-Path $folder 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) {
    return $compiler
}

$download = Join-Path ([System.IO.Path]::GetTempPath()) "innosetup-$innoVersion-$([guid]::NewGuid().ToString('N')).exe"
try {
    Write-Host "Downloading Inno Setup $innoVersion"
    Invoke-WebRequest -Uri $installerUrl -OutFile $download -UseBasicParsing
    $actual = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $installerSha256) {
        throw "The Inno Setup installer has the SHA-256 $actual instead of $installerSha256."
    }

    Write-Host "Installing Inno Setup $innoVersion (portable) into $folder"
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/PORTABLE=1', '/NOICONS', "/DIR=`"$folder`"")
    $process = Start-Process -FilePath $download -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "The Inno Setup installer failed with exit code $($process.ExitCode)."
    }
}
finally {
    Remove-Item -LiteralPath $download -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Inno Setup was installed, but $compiler is missing."
}

return $compiler
