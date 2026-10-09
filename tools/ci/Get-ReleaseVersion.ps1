<#
.SYNOPSIS
    Checks that a release tag matches the version in Directory.Build.props (docs/RELEASING.md).

.DESCRIPTION
    The release workflow builds exactly the version that Directory.Build.props declares; the tag only names it. This
    script fails, with a message that says what to change, unless

        the tag is v<major>.<minor>.<patch>, optionally with a pre-release suffix (v6.1.0-beta.1),
        <Version> is the tag without its "v",
        <FileVersion> and <AssemblyVersion> start with the same <major>.<minor>.<patch>.

    It warns (without failing) when the assemblyIdentity version in src/HolidayLights.App/app.manifest is behind.

    On GitHub Actions it writes version, tag and prerelease (true or false) to $GITHUB_OUTPUT.

.PARAMETER Tag
    The tag, for example v6.0.0 or refs/tags/v6.0.0.

.EXAMPLE
    pwsh -NoProfile -File tools/ci/Get-ReleaseVersion.ps1 -Tag v6.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [AllowEmptyString()]
    [string] $Tag
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Stop-WithError([string] $Message) {
    if ($env:GITHUB_ACTIONS -eq 'true') {
        # A workflow-command annotation: the message appears on the run's summary page.
        $escaped = $Message.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
        Write-Host "::error title=Release version check failed::$escaped"
    }

    throw $Message
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$propsPath = Join-Path $repo 'Directory.Build.props'
$manifestPath = Join-Path $repo 'src\HolidayLights.App\app.manifest'

$tagName = $Tag.Trim()
if ($tagName.StartsWith('refs/tags/', [StringComparison]::Ordinal)) {
    $tagName = $tagName.Substring('refs/tags/'.Length)
}

if (-not $tagName) {
    Stop-WithError 'No release tag was given. Push a tag such as v6.0.0, or run the workflow from a tag or with the "tag" input.'
}

$tagPattern = '^v(?<core>(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*))(?<pre>-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'
$match = [regex]::Match($tagName, $tagPattern)
if (-not $match.Success) {
    Stop-WithError "The tag '$tagName' is not a release tag. Use v<major>.<minor>.<patch>, optionally with a pre-release suffix: v6.0.1 or v6.1.0-beta.1."
}

$tagVersion = $tagName.Substring(1)
$core = $match.Groups['core'].Value
$prerelease = $match.Groups['pre'].Success

[xml] $props = Get-Content -Raw -LiteralPath $propsPath
function Read-Property([string] $Name) {
    $node = $props.SelectSingleNode("/Project/PropertyGroup/$Name")
    if ($node -and $node.InnerText.Trim()) { $node.InnerText.Trim() } else { $null }
}

$version = Read-Property 'Version'
$fileVersion = Read-Property 'FileVersion'
$assemblyVersion = Read-Property 'AssemblyVersion'

$problems = [System.Collections.Generic.List[string]]::new()
if (-not $version) {
    $problems.Add('<Version> is missing from Directory.Build.props.')
}
elseif ($version -cne $tagVersion) {
    $problems.Add("<Version> in Directory.Build.props is $version, but the tag $tagName asks for $tagVersion.")
}

foreach ($pair in @(@('FileVersion', $fileVersion), @('AssemblyVersion', $assemblyVersion))) {
    $name, $value = $pair
    if ($value -and $value -ne $core -and -not $value.StartsWith("$core.", [StringComparison]::Ordinal)) {
        $problems.Add("<$name> in Directory.Build.props is $value; it must start with $core (for example $core.0).")
    }
}

if ($problems.Count -gt 0) {
    $problems.Add("Set <Version>$tagVersion</Version>, <AssemblyVersion>$core.0</AssemblyVersion> and <FileVersion>$core.0</FileVersion> in Directory.Build.props, commit, and tag that commit (docs/RELEASING.md). The tag must name the version the code declares.")
    Stop-WithError ($problems -join ' ')
}

if (Test-Path -LiteralPath $manifestPath) {
    [xml] $manifest = Get-Content -Raw -LiteralPath $manifestPath
    $identity = $manifest.DocumentElement.ChildNodes | Where-Object { $_.LocalName -eq 'assemblyIdentity' } | Select-Object -First 1
    $manifestVersion = if ($identity) { $identity.GetAttribute('version') } else { '' }
    if ($manifestVersion -and -not $manifestVersion.StartsWith("$core.", [StringComparison]::Ordinal)) {
        $warning = "src/HolidayLights.App/app.manifest declares version $manifestVersion; set its assemblyIdentity version to $core.0 too."
        if ($env:GITHUB_ACTIONS -eq 'true') {
            Write-Host "::warning title=app.manifest version::$warning"
        }
        else {
            Write-Warning $warning
        }
    }
}

$prereleaseText = if ($prerelease) { 'true' } else { 'false' }
Write-Host "Release $tagName builds Holiday Lights $version (file version $fileVersion, pre-release: $prereleaseText)."

if ($env:GITHUB_OUTPUT) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Value @(
        "version=$version"
        "tag=$tagName"
        "prerelease=$prereleaseText"
    )
}
