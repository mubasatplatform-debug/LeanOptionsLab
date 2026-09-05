[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$RunId = ("local-data-proof-" + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
)

$ErrorActionPreference = 'Stop'

$requiredLeanCommit = 'abeb0a0627ec484b92291c45c3f2553726c26199'
$workspace = Split-Path -Parent $PSScriptRoot
$engineRoot = Join-Path $workspace '.tools\lean-engine'
$launcherProject = Join-Path $engineRoot 'Launcher\QuantConnect.Lean.Launcher.csproj'
$launcherAssembly = Join-Path $engineRoot 'Launcher\bin\Release\QuantConnect.Lean.Launcher.dll'
$proofProject = Join-Path $workspace 'tests\LocalDataProof\LocalDataProof.csproj'
$proofAssembly = Join-Path $workspace 'tests\LocalDataProof\bin\Release\LocalDataProof.dll'
$configPath = Join-Path $workspace 'lean.json'
$dataFolder = Join-Path $workspace 'data'
$resultsRoot = Join-Path $workspace 'results'
$runDirectory = Join-Path $resultsRoot $RunId

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is not available on PATH.'
}

$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith('10.')) {
    throw "This launcher path requires .NET SDK 10.x. Detected: $sdkVersion"
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required to verify the pinned local LEAN source.'
}

if (-not (Test-Path -LiteralPath $launcherProject)) {
    throw "Pinned LEAN source is missing: $launcherProject"
}

$actualLeanCommit = (& git -C $engineRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualLeanCommit -ne $requiredLeanCommit) {
    throw "LEAN source must be pinned to $requiredLeanCommit. Detected: $actualLeanCommit"
}

foreach ($path in @($proofProject, $configPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required local file is missing: $path"
    }
}

$requiredSamplePaths = @(
    'equity\usa\minute\goog',
    'option\usa\minute\goog',
    'option\usa\universes\goog'
)

foreach ($relativePath in $requiredSamplePaths) {
    $samplePath = Join-Path $dataFolder $relativePath
    if (-not (Test-Path -LiteralPath $samplePath -PathType Container)) {
        throw "Required GOOG sample data is missing: $samplePath. Run scripts\Invoke-LocalSampleDataSeed.ps1 first."
    }
}

if (Test-Path -LiteralPath $runDirectory) {
    throw "Run directory already exists: $runDirectory"
}

New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

& dotnet build $proofProject --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& dotnet build $launcherProject --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

foreach ($path in @($proofAssembly, $launcherAssembly)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Expected build output is missing: $path"
    }
}

$launcherArguments = @(
    '--config', $configPath,
    '--close-automatically', 'true',
    '--environment', 'backtesting',
    '--algorithm-type-name', 'QuantConnect.Algorithm.CSharp.LocalDataProof',
    '--algorithm-language', 'CSharp',
    '--algorithm-location', $proofAssembly,
    '--data-folder', $dataFolder,
    '--results-destination-folder', $runDirectory,
    '--backtest-name', $RunId,
    '--algorithm-id', $RunId
)

& dotnet $launcherAssembly @launcherArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$logPath = Join-Path $runDirectory 'log.txt'
if (-not (Test-Path -LiteralPath $logPath)) {
    throw "LEAN data proof did not create its expected log: $logPath"
}

$initializationMarker = 'LOCAL_DATA_PROOF\|initialized\|underlying=GOOG\|resolution=Minute\|orders=0'
if (-not (Select-String -LiteralPath $logPath -Pattern $initializationMarker -Quiet)) {
    throw 'LEAN data proof completed without its initialization marker.'
}

$completionMatch = Select-String -LiteralPath $logPath -Pattern 'LOCAL_DATA_PROOF\|completed\|chainSlices=(\d+)\|contractQuotes=(\d+)\|uniqueContracts=(\d+)\|orders=0' |
    Select-Object -First 1
if (-not $completionMatch) {
    throw 'LEAN data proof completed without its zero-order completion marker.'
}

$chainSlices = [long]$completionMatch.Matches[0].Groups[1].Value
$contractQuotes = [long]$completionMatch.Matches[0].Groups[2].Value
$uniqueContracts = [long]$completionMatch.Matches[0].Groups[3].Value
if ($chainSlices -le 0 -or $contractQuotes -le 0 -or $uniqueContracts -le 0) {
    throw "LEAN ran but did not deliver usable option data: chainSlices=$chainSlices contractQuotes=$contractQuotes uniqueContracts=$uniqueContracts"
}

Write-Host "Local data proof completed: $runDirectory"
Write-Host "chainSlices=$chainSlices contractQuotes=$contractQuotes uniqueContracts=$uniqueContracts orders=0"
