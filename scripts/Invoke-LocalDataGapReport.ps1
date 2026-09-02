[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$RunId
)

$ErrorActionPreference = 'Stop'

$workspace = Split-Path -Parent $PSScriptRoot
$resultsRoot = Join-Path $workspace 'results'
$dataFolder = Join-Path $workspace 'data'

if (-not (Test-Path -LiteralPath $resultsRoot)) {
    throw "Results root is missing: $resultsRoot"
}

if ($RunId) {
    $runDirectory = Join-Path $resultsRoot $RunId
    if (-not (Test-Path -LiteralPath $runDirectory -PathType Container)) {
        throw "Run directory is missing: $runDirectory"
    }
} else {
    $runDirectory = Get-ChildItem -LiteralPath $resultsRoot -Directory |
        Sort-Object LastWriteTimeUtc -Descending |
        Where-Object {
            @(Get-ChildItem -LiteralPath $_.FullName -Filter 'failed-data-requests-*.txt' -File).Count -gt 0
        } |
        Select-Object -First 1 -ExpandProperty FullName

    if (-not $runDirectory) {
        throw "No measured run containing failed-data-requests-*.txt exists below $resultsRoot"
    }
}

$reportPath = Join-Path $runDirectory 'comparison-report.json'
if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw "The measured run has no comparison-report.json: $runDirectory"
}

$failedFiles = @(Get-ChildItem -LiteralPath $runDirectory -Filter 'failed-data-requests-*.txt' -File)
$succeededFiles = @(Get-ChildItem -LiteralPath $runDirectory -Filter 'succeeded-data-requests-*.txt' -File)
if ($failedFiles.Count -ne 1 -or $succeededFiles.Count -ne 1) {
    throw "Expected exactly one failed and one succeeded data-request file in $runDirectory"
}

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$experiment = $report.experiment
if ($null -eq $experiment) {
    throw "The comparison report has no experiment object: $reportPath"
}

$failed = @(Get-Content -LiteralPath $failedFiles[0].FullName | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$succeeded = @(Get-Content -LiteralPath $succeededFiles[0].FullName | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

Write-Host ("Run:        {0}" -f (Split-Path -Leaf $runDirectory))
Write-Host ("Underlying: {0}   Resolution: {1}" -f $experiment.underlying, $experiment.resolution)
Write-Host ("Period:     {0} .. {1}" -f $experiment.startDate, $experiment.endDate)
Write-Host ("Status:     {0}   Ranking: {1}" -f $report.finalStatus, $report.rankingAssessment.status)
Write-Host ''
Write-Host ("Data requests: {0} succeeded, {1} failed." -f $succeeded.Count, $failed.Count)
Write-Host ''
Write-Host 'Missing by data kind:'

$groups = @($failed |
    Group-Object { $_ -replace '\d{8}', '<DATE>' } |
    Sort-Object -Property @(
        @{ Expression = 'Count'; Descending = $true },
        @{ Expression = 'Name'; Descending = $false }
    ))

foreach ($group in $groups) {
    Write-Host ("  {0,6}  {1}" -f $group.Count, $group.Name)
}

$dates = @($failed | ForEach-Object { if ($_ -match '(\d{8})') { $Matches[1] } } | Sort-Object -Unique)
Write-Host ''
if ($dates.Count -gt 0) {
    Write-Host ("Distinct trading days requested: {0}  ({1} .. {2})" -f $dates.Count, $dates[0], $dates[-1])
}

Write-Host 'This request list is a floor, not a ceiling; re-run it after each acquired data batch.'

$presentSymbols = @()
$optionMinute = Join-Path $dataFolder 'option\usa\minute'
if (Test-Path -LiteralPath $optionMinute) {
    $presentSymbols = @(Get-ChildItem -LiteralPath $optionMinute -Directory | Select-Object -ExpandProperty Name)
}

Write-Host ''
Write-Host ("Option symbols present in data/: {0}" -f $(if ($presentSymbols.Count -gt 0) { $presentSymbols -join ', ' } else { 'none' }))

$target = ([string]$experiment.underlying).ToLowerInvariant()
if ($presentSymbols -contains $target) {
    Write-Host "The configured underlying '$target' has option data on disk; re-run the backtest to re-measure."
} else {
    Write-Host "The configured underlying '$target' has no option data on disk. This is the gap."
}

Write-Host ''
Write-Host 'Acquisition — owner action required:'
Write-Host '  1. Create a CLI root OUTSIDE this repository (it writes its own lean.json and data/).'
Write-Host '  2. Sign in to QuantConnect, then initialize that root for your organization.'
Write-Host '  3. Acquire every data path reported above for the period, then copy it under data/.'
Write-Host '  4. Re-run .\scripts\Invoke-LocalLeanBacktest.ps1 and this report to confirm the gap closed.'
Write-Host ''
Write-Host 'Pricing and entitlements come from your QuantConnect account; this script does not query them.'
Write-Host 'See docs/DATA-ACQUISITION.md for the on-disk format if you use a non-QuantConnect feed.'

# Reporting a measured gap is the expected outcome, not a script failure.
exit 0
