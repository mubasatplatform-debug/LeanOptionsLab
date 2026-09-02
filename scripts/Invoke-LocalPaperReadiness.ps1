[CmdletBinding()]
param()

# Verifies the paper-trading wiring without running LEAN. A paper session would
# write a results directory indistinguishable from a real one, so this script
# proves the wiring statically and refuses to produce any run artifact.

$ErrorActionPreference = 'Stop'

$requiredLeanCommit = 'abeb0a0627ec484b92291c45c3f2553726c26199'
$workspace = Split-Path -Parent $PSScriptRoot
$engineRoot = Join-Path $workspace '.tools\lean-engine'
$configPath = Join-Path $workspace 'lean.json'
$experimentPath = Join-Path $workspace 'LeanOptionsLab\configs\experiment.v1.json'
$algorithmProject = Join-Path $workspace 'LeanOptionsLab\LeanOptionsLab.csproj'
$toolingProject = Join-Path $workspace 'LeanOptionsLab.Tooling\LeanOptionsLab.Tooling.csproj'
$algorithmAssembly = Join-Path $workspace 'LeanOptionsLab\bin\Release\LeanOptionsLab.dll'
$paperBrokerageSource = Join-Path $engineRoot 'Brokerages\Paper\PaperBrokerage.cs'
$dataQueueTypeName = 'LeanOptionsLab.LiveData.OptionsLabLiveDataQueue'

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

$actualLeanCommit = (& git -C $engineRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualLeanCommit -ne $requiredLeanCommit) {
    throw "LEAN source must be pinned to $requiredLeanCommit. Detected: $actualLeanCommit"
}

foreach ($path in @($configPath, $experimentPath, $algorithmProject, $toolingProject)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required local file is missing: $path"
    }
}

$leanConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$paperEnvironment = $leanConfig.environments.'live-paper'
if ($null -eq $paperEnvironment) {
    throw "lean.json does not define the 'live-paper' environment."
}

if ($paperEnvironment.'live-mode-brokerage' -ne 'PaperBrokerage') {
    throw "The 'live-paper' environment must use PaperBrokerage. Detected: $($paperEnvironment.'live-mode-brokerage')"
}

if (@($paperEnvironment.'data-queue-handler') -notcontains $dataQueueTypeName) {
    throw "The 'live-paper' environment must route data through $dataQueueTypeName."
}

if (-not (Test-Path -LiteralPath $paperBrokerageSource)) {
    throw "PaperBrokerage is not present in the pinned LEAN source: $paperBrokerageSource"
}

& dotnet build $algorithmProject --configuration Release --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $algorithmAssembly)) {
    throw "Expected build output is missing: $algorithmAssembly"
}

$assemblyBytes = [System.IO.File]::ReadAllBytes($algorithmAssembly)
$assemblyText = [System.Text.Encoding]::UTF8.GetString($assemblyBytes)
if (-not $assemblyText.Contains('OptionsLabLiveDataQueue')) {
    throw "The compiled assembly does not expose $dataQueueTypeName."
}

Write-Host 'Configured wiring names verified: live-paper -> PaperBrokerage, data queue -> OptionsLabLiveDataQueue.'
Write-Host ''

# The queue in this repository is intentionally a fail-closed placeholder, so
# the provider flag remains false until an audited IDataQueueHandler replaces it.
$brokerageIsPaperOnly = ($paperEnvironment.'live-mode-brokerage' -eq 'PaperBrokerage').ToString().ToLowerInvariant()
& dotnet run --project $toolingProject --configuration Release -- `
    paper-readiness `
    --config $experimentPath `
    --approved-live-data-provider false `
    --paper-only-brokerage $brokerageIsPaperOnly
$readinessExit = $LASTEXITCODE

if ($readinessExit -eq 3) {
    Write-Host ''
    Write-Host 'Paper trading remains blocked. No LEAN session or results directory was created.'
}

exit $readinessExit
