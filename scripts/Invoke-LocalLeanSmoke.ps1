[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$RunId = ("local-smoke-" + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'))
)

$ErrorActionPreference = 'Stop'

$requiredLeanCommit = 'abeb0a0627ec484b92291c45c3f2553726c26199'
$workspace = Split-Path -Parent $PSScriptRoot
$engineRoot = Join-Path $workspace '.tools\lean-engine'
$launcherProject = Join-Path $engineRoot 'Launcher\QuantConnect.Lean.Launcher.csproj'
$launcherAssembly = Join-Path $engineRoot 'Launcher\bin\Release\QuantConnect.Lean.Launcher.dll'
$smokeProject = Join-Path $workspace 'tests\LocalLeanSmoke\LocalLeanSmoke.csproj'
$smokeAssembly = Join-Path $workspace 'tests\LocalLeanSmoke\bin\Release\LocalLeanSmoke.dll'
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

foreach ($path in @($smokeProject, $configPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required local file is missing: $path"
    }
}

if (Test-Path -LiteralPath $runDirectory) {
    throw "Run directory already exists: $runDirectory"
}

New-Item -ItemType Directory -Force -Path $dataFolder | Out-Null
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

$engineSymbolProperties = Join-Path $engineRoot 'Data\symbol-properties\symbol-properties-database.csv'
$localSymbolPropertiesDirectory = Join-Path $dataFolder 'symbol-properties'
$localSymbolProperties = Join-Path $localSymbolPropertiesDirectory 'symbol-properties-database.csv'
if (-not (Test-Path -LiteralPath $engineSymbolProperties)) {
    throw "Pinned LEAN source is missing its required symbol-properties file: $engineSymbolProperties"
}

if (-not (Test-Path -LiteralPath $localSymbolProperties)) {
    New-Item -ItemType Directory -Force -Path $localSymbolPropertiesDirectory | Out-Null
    Copy-Item -LiteralPath $engineSymbolProperties -Destination $localSymbolProperties
}

$engineMarketHours = Join-Path $engineRoot 'Data\market-hours\market-hours-database.json'
$localMarketHoursDirectory = Join-Path $dataFolder 'market-hours'
$localMarketHours = Join-Path $localMarketHoursDirectory 'market-hours-database.json'
if (-not (Test-Path -LiteralPath $engineMarketHours)) {
    throw "Pinned LEAN source is missing its required market-hours file: $engineMarketHours"
}

if (-not (Test-Path -LiteralPath $localMarketHours)) {
    New-Item -ItemType Directory -Force -Path $localMarketHoursDirectory | Out-Null
    Copy-Item -LiteralPath $engineMarketHours -Destination $localMarketHours
}

& dotnet build $smokeProject --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& dotnet build $launcherProject --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

foreach ($path in @($smokeAssembly, $launcherAssembly)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Expected build output is missing: $path"
    }
}

$launcherArguments = @(
    '--config', $configPath,
    '--close-automatically', 'true',
    '--environment', 'backtesting',
    '--algorithm-type-name', 'QuantConnect.Algorithm.CSharp.LocalLeanSmoke',
    '--algorithm-language', 'CSharp',
    '--algorithm-location', $smokeAssembly,
    '--data-folder', $dataFolder,
    '--results-destination-folder', $runDirectory,
    '--backtest-name', $RunId,
    '--algorithm-id', "local-smoke-$RunId"
)

& dotnet $launcherAssembly @launcherArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$logPath = Join-Path $runDirectory 'log.txt'
if (-not (Test-Path -LiteralPath $logPath)) {
    throw "LEAN smoke run did not create its expected log: $logPath"
}

if (-not (Select-String -LiteralPath $logPath -Pattern 'LOCAL_LEAN_SMOKE\|initialized\|subscriptions=0\|orders=0' -Quiet)) {
    throw 'LEAN smoke run completed without the subscription-free marker.'
}

Write-Host "Local LEAN smoke completed: $runDirectory"
