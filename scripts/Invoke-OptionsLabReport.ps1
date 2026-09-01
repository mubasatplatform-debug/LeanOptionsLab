[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$RunId,
    [string]$DataEvidencePath,
    [string]$EvaluationsPath,
    [string]$LeanLogPath,
    [string]$OrderEventsPath,
    [string]$AssignmentEventsPath,
    [string]$ExerciseEventsPath
)

$ErrorActionPreference = 'Stop'

$workspace = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $workspace 'LeanOptionsLab\configs\experiment.v1.json'
$toolingProject = Join-Path $workspace 'LeanOptionsLab.Tooling\LeanOptionsLab.Tooling.csproj'
$outputRoot = Join-Path $workspace 'results'
$codeVersion = 'uncommitted'

if (Get-Command git -ErrorAction SilentlyContinue) {
    $gitVersion = & git -C $workspace rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitVersion)) {
        $codeVersion = $gitVersion.Trim()
    }
}

$toolArguments = @(
    'run',
    '--project', $toolingProject,
    '--',
    'write-report',
    '--config', $configPath,
    '--output-root', $outputRoot,
    '--run-id', $RunId,
    '--code-version', $codeVersion
)

if ($DataEvidencePath) {
    $toolArguments += @('--data-evidence', $DataEvidencePath)
}

if ($EvaluationsPath) {
    $toolArguments += @('--evaluations', $EvaluationsPath)
}

if ($LeanLogPath) {
    $toolArguments += @('--lean-log', $LeanLogPath)
}

if ($OrderEventsPath) {
    $toolArguments += @('--order-events', $OrderEventsPath)
}

if ($AssignmentEventsPath) {
    $toolArguments += @('--assignment-events', $AssignmentEventsPath)
}

if ($ExerciseEventsPath) {
    $toolArguments += @('--exercise-events', $ExerciseEventsPath)
}

& dotnet @toolArguments
exit $LASTEXITCODE
