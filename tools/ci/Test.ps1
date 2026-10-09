<#
.SYNOPSIS
    Runs the tests that are safe on a GitHub-hosted Windows runner (CI and release workflows; docs/RELEASING.md).

.DESCRIPTION
    Runs dotnet test on HolidayLights.sln without the tests that need a real PC:

        Category=Live     opt-in probes that show real lights or play sound (they also skip themselves)
        Category=Desktop  the real interactive desktop: Explorer's desktop windows, the real displays and their scaling,
                          DXGI outputs, the session's live state, or a screen larger than a runner's 1024 x 768
        Category=Audio    an audio output or MIDI synthesizer that can open

    The results go to -ResultsDirectory as TRX (and a hang dump if a test stops responding for 10 minutes). On GitHub
    Actions, the totals and every failed test are written to the job summary, and each failure becomes an annotation.

    To run everything on your own PC instead (as before every release):
        dotnet test HolidayLights.sln -c Release --filter "Category!=Live"

.PARAMETER Configuration
    The configuration to test (Release by default).

.PARAMETER ResultsDirectory
    Where the TRX file goes. Defaults to $env:RUNNER_TEMP\TestResults on GitHub Actions, else to
    $env:HL_BUILD_ROOT\TestResults or <repo>\artifacts\TestResults.

.PARAMETER NoBuild
    Test what the previous build step produced instead of building first.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $ResultsDirectory,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The filter CI applies; keep docs/RELEASING.md in step when it changes.
$filter = 'Category!=Live&Category!=Desktop&Category!=Audio'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $ResultsDirectory) {
    $base = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } elseif ($env:HL_BUILD_ROOT) { $env:HL_BUILD_ROOT } else { Join-Path $repo 'artifacts' }
    $ResultsDirectory = Join-Path $base 'TestResults'
}

New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$ResultsDirectory = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$startedAt = Get-Date

$arguments = @(
    'test', (Join-Path $repo 'HolidayLights.sln'),
    '--configuration', $Configuration,
    '--filter', $filter,
    '--logger', 'trx;LogFileName=HolidayLights.Tests.trx',
    '--results-directory', $ResultsDirectory,
    '--blame-hang-timeout', '10m',
    '--blame-hang-dump-type', 'mini'
)
if ($NoBuild) {
    $arguments += '--no-build'
}

Write-Host "dotnet $($arguments -join ' ')"
& dotnet @arguments
$exitCode = $LASTEXITCODE

function ConvertTo-CommandValue([string] $Text) {
    $Text.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
}

function ConvertTo-CommandProperty([string] $Text) {
    (ConvertTo-CommandValue $Text).Replace(':', '%3A').Replace(',', '%2C')
}

# Only the TRX files of this run: the results folder may hold older ones.
$trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse -File | Where-Object { $_.LastWriteTime -ge $startedAt.AddSeconds(-5) })
$summary = [System.Text.StringBuilder]::new()
[void] $summary.AppendLine('## Tests')
[void] $summary.AppendLine()
[void] $summary.AppendLine("Filter: ``$filter`` (the Live, Desktop and Audio tests need a real PC; see docs/RELEASING.md).")
[void] $summary.AppendLine()
if ($trxFiles.Count -eq 0) {
    [void] $summary.AppendLine('No test results were written.')
}

foreach ($trx in $trxFiles) {
    [xml] $doc = Get-Content -Raw -LiteralPath $trx.FullName
    $ns = [System.Xml.XmlNamespaceManager]::new($doc.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $counters = $doc.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $ns)
    $results = @($doc.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
    $failed = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'Failed' })
    $skipped = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'NotExecuted' }).Count
    $outcome = $doc.SelectSingleNode('/t:TestRun/t:ResultSummary', $ns).GetAttribute('outcome')

    [void] $summary.AppendLine('| Result | Total | Passed | Failed | Skipped |')
    [void] $summary.AppendLine('|---|---:|---:|---:|---:|')
    [void] $summary.AppendLine("| $outcome | $($counters.GetAttribute('total')) | $($counters.GetAttribute('passed')) | $($counters.GetAttribute('failed')) | $skipped |")
    [void] $summary.AppendLine()

    foreach ($result in $failed) {
        $name = $result.GetAttribute('testName')
        $messageNode = $result.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
        $message = if ($messageNode) { $messageNode.InnerText.Trim() } else { '(no message)' }
        $firstLine = ($message -split "`r?`n")[0]
        Write-Host "::error title=$(ConvertTo-CommandProperty "Test failed: $name")::$(ConvertTo-CommandValue $message)"
        [void] $summary.AppendLine("- **$name**: $($firstLine.Replace('|', '\|'))")
    }

    if ($failed.Count -gt 0) {
        [void] $summary.AppendLine()
    }
}

if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary.ToString() -Encoding utf8
}
else {
    Write-Host $summary.ToString()
}

if ($exitCode -ne 0) {
    throw "dotnet test failed with exit code $exitCode."
}
