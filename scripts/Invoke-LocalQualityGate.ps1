[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$RunIdPrefix = ("quality-" + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')),
    [switch]$SkipFetch
)

$ErrorActionPreference = 'Stop'

$requiredLeanCommit = 'abeb0a0627ec484b92291c45c3f2553726c26199'
$workspace = Split-Path -Parent $PSScriptRoot
$engineRoot = Join-Path $workspace '.tools\lean-engine'
$testProject = Join-Path $workspace 'LeanOptionsLab.Tests\LeanOptionsLab.Tests.csproj'
$toolingProject = Join-Path $workspace 'LeanOptionsLab.Tooling\LeanOptionsLab.Tooling.csproj'
$algorithmProject = Join-Path $workspace 'LeanOptionsLab\LeanOptionsLab.csproj'
$smokeProject = Join-Path $workspace 'tests\LocalLeanSmoke\LocalLeanSmoke.csproj'
$experimentConfig = Join-Path $workspace 'LeanOptionsLab\configs\experiment.v1.json'
$smokeScript = Join-Path $PSScriptRoot 'Invoke-LocalLeanSmoke.ps1'
$backtestScript = Join-Path $PSScriptRoot 'Invoke-LocalLeanBacktest.ps1'
$smokeRunId = "$RunIdPrefix-smoke"
$noDataRunId = "$RunIdPrefix-nodata"

function Assert-CommandAvailable {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command is not available on PATH: $Name"
    }
}

function Invoke-External {
    param(
        [string]$Name,
        [scriptblock]$Script
    )

    Write-Host "==> $Name"
    & $Script
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Assert-NoRipgrepMatches {
    param(
        [string]$Name,
        [string]$Pattern,
        [string[]]$AdditionalExcludes = @()
    )

    $searchArgs = @(
        '-n',
        '-i',
        '--hidden',
        '-g', '!/.git/**',
        '-g', '!/.tools/**',
        '-g', '!**/bin/**',
        '-g', '!**/obj/**',
        '-g', '!data/**',
        '-g', '!results/**',
        '-g', '!storage/**',
        '-g', '!backtests/**',
        '-g', '!logs/**'
    )

    foreach ($exclude in $AdditionalExcludes) {
        $searchArgs += @('-g', $exclude)
    }

    $searchArgs += @($Pattern, '.')

    Write-Host "==> $Name"
    $matches = & rg @searchArgs
    if ($LASTEXITCODE -eq 0) {
        $matches
        throw "$Name found forbidden matches."
    }

    if ($LASTEXITCODE -ne 1) {
        exit $LASTEXITCODE
    }
}

function Get-ProjectTextFiles {
    $fileArgs = @(
        '--files',
        '--hidden',
        '-g', '!/.git/**',
        '-g', '!/.tools/**',
        '-g', '!**/bin/**',
        '-g', '!**/obj/**',
        '-g', '!data/**',
        '-g', '!results/**',
        '-g', '!storage/**',
        '-g', '!backtests/**',
        '-g', '!logs/**'
    )

    $paths = & rg @fileArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    return $paths
}

Set-Location $workspace

Assert-CommandAvailable 'dotnet'
Assert-CommandAvailable 'git'
Assert-CommandAvailable 'rg'
Assert-CommandAvailable 'gitleaks'

if (-not $SkipFetch) {
    Invoke-External 'git fetch' { git fetch --all --prune }
}

Invoke-External 'git status before gate' { git status --short --branch }

$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith('10.')) {
    throw "This project requires .NET SDK 10.x. Detected: $sdkVersion"
}

if (-not (Test-Path -LiteralPath $engineRoot)) {
    throw "Pinned LEAN source is missing: $engineRoot"
}

$actualLeanCommit = (& git -C $engineRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualLeanCommit -ne $requiredLeanCommit) {
    throw "LEAN source must be pinned to $requiredLeanCommit. Detected: $actualLeanCommit"
}

Invoke-External 'LEAN source status' { git -C $engineRoot status --short }
Invoke-External 'C# tests' { dotnet run --project $testProject --configuration Release }
Invoke-External 'algorithm build' { dotnet build $algorithmProject --configuration Release --nologo }
Invoke-External 'smoke fixture build' { dotnet build $smokeProject --configuration Release --nologo }
Invoke-External 'experiment config validation' {
    dotnet run --project $toolingProject --configuration Release -- validate --config $experimentConfig
}
Invoke-External 'local launcher smoke' { & $smokeScript -RunId $smokeRunId }
Invoke-External 'local no-data backtest report' { & $backtestScript -RunId $noDataRunId }

$reportPath = Join-Path $workspace "results\$noDataRunId\comparison-report.json"
if (-not (Test-Path -LiteralPath $reportPath)) {
    throw "No-data run did not create its JSON report: $reportPath"
}

$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($report.finalStatus -ne 'invalid-data') {
    throw "Expected finalStatus invalid-data. Detected: $($report.finalStatus)"
}

if ($report.rankingAssessment.status -ne 'not-rankable') {
    throw "Expected ranking status not-rankable. Detected: $($report.rankingAssessment.status)"
}

if (@($report.rankingAssessment.rankedStrategies).Count -ne 0) {
    throw 'No-data run must not emit ranked strategies.'
}

if ($report.dataReadiness.isReady -ne $false) {
    throw 'No-data run must keep dataReadiness.isReady=false.'
}

if (@($report.orderEvents).Count -ne 0 -or @($report.assignmentEvents).Count -ne 0 -or @($report.exerciseEvents).Count -ne 0) {
    throw 'No-data run must not emit order, assignment, or exercise events.'
}

Write-Host "==> no-data report status"
Write-Host "finalStatus=$($report.finalStatus)"
Write-Host "rankingStatus=$($report.rankingAssessment.status)"
Write-Host "report=$reportPath"

$legacyExecutionPattern = @(
    ('do' + 'cker'),
    ('--download' + '-data'),
    (('do' + 'cker') + 'smoke'),
    ('invoke-lean' + ('do' + 'cker')),
    ('invoke-lean' + 'optionslab')
) -join '|'
Assert-NoRipgrepMatches 'legacy execution reference scan' $legacyExecutionPattern

# The CLI remains valid for acquiring licensed data, but it is not an accepted
# algorithm execution path. Its executable may therefore be documented only in
# the dedicated acquisition guide; code, scripts, configs, and the README stay
# protected from silently reintroducing it as a launcher.
$legacyCliPattern = @(
    ('lean' + '-cli'),
    ('lean' + '\.exe')
) -join '|'
Assert-NoRipgrepMatches 'legacy CLI execution reference scan' $legacyCliPattern -AdditionalExcludes @('!docs/DATA-ACQUISITION.md')

# Two unrelated policies used to share one pattern. Splitting them keeps the
# secret scan absolute while letting the broker scan say what it actually means.
$credentialPattern = @(
    ('api[-_ ]?' + 'key'),
    ('api-access' + '-token'),
    ('access[-_ ]?' + 'token'),
    ('client[-_ ]?' + 'secret'),
    ('pass' + 'word'),
    ('private[-_ ]?' + 'key')
) -join '|'
Assert-NoRipgrepMatches 'credential leak scan' $credentialPattern

# Names real venues explicitly instead of the generic word 'brokerage'. LEAN's
# own config key is live-mode-brokerage and PaperBrokerage is an in-process
# simulator that opens no connection, so the generic word stopped being evidence
# of drift while these names never stop being evidence of it. This list is
# strictly wider than the word it replaces: only the first two entries were
# blocked before, the other ten were not. Entries are split across a join so
# this scan does not match its own definition. docs/ is excluded because it
# documents why these venues do not work here; no execution path may name them.
$realBrokerPattern = @(
    ('ib' + 'kr'),
    ('interactive' + ' brokers'),
    ('trad' + 'ier'),
    ('alp' + 'aca'),
    ('oan' + 'da'),
    ('fx' + 'cm'),
    ('zero' + 'dha'),
    ('sam' + 'co'),
    ('coin' + 'base'),
    ('bit' + 'finex'),
    ('bin' + 'ance'),
    ('kra' + 'ken')
) -join '|'
Assert-NoRipgrepMatches 'real brokerage scan' $realBrokerPattern -AdditionalExcludes @('!docs/**')

Invoke-External 'git diff whitespace check' { git diff --check }

$projectFiles = Get-ProjectTextFiles
Write-Host "==> trailing whitespace scan"
$badWhitespace = foreach ($path in $projectFiles) {
    Select-String -LiteralPath (Join-Path $workspace $path) -Pattern '[ \t]$' | ForEach-Object {
        '{0}:{1}' -f $path, $_.LineNumber
    }
}

if ($badWhitespace) {
    $badWhitespace
    throw 'Trailing whitespace found.'
}

Write-Host "scannedFiles=$($projectFiles.Count)"

Write-Host '==> gitleaks'
$contents = foreach ($path in $projectFiles) {
    Get-Content -LiteralPath (Join-Path $workspace $path) -Raw
}

$contents | gitleaks detect --pipe --no-banner --redact=100
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Invoke-External 'git ignored artifact check' { git check-ignore -v .tools data results storage }
Invoke-External 'git status after gate' { git status --short --branch }

Write-Host "Local quality gate completed. smokeRunId=$smokeRunId noDataRunId=$noDataRunId"
