[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$requiredLeanCommit = 'abeb0a0627ec484b92291c45c3f2553726c26199'
$workspace = Split-Path -Parent $PSScriptRoot
$engineRoot = Join-Path $workspace '.tools\lean-engine'
$engineData = Join-Path $engineRoot 'Data'
$localData = Join-Path $workspace 'data'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required to verify the pinned local LEAN source.'
}

if (-not (Test-Path -LiteralPath $engineRoot -PathType Container)) {
    throw "Pinned LEAN source is missing: $engineRoot"
}

$actualLeanCommit = & git -C $engineRoot rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read the pinned LEAN commit from: $engineRoot"
}

$actualLeanCommit = "$actualLeanCommit".Trim()
if ($actualLeanCommit -ne $requiredLeanCommit) {
    throw "LEAN source must be pinned to $requiredLeanCommit. Detected: $actualLeanCommit"
}

if (-not (Test-Path -LiteralPath $engineData -PathType Container)) {
    throw "Pinned LEAN sample data is missing: $engineData"
}

$sampleDirectories = @(
    'equity\usa\minute\goog',
    'option\usa\minute\goog',
    'option\usa\universes\goog'
)

if ($Remove) {
    foreach ($relativePath in $sampleDirectories) {
        $target = Join-Path $localData $relativePath
        if (Test-Path -LiteralPath $target) {
            $resolvedData = [IO.Path]::GetFullPath($localData).TrimEnd('\') + '\'
            $resolvedTarget = [IO.Path]::GetFullPath($target)
            if (-not ($resolvedTarget + '\').StartsWith($resolvedData, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove a path outside data/: $resolvedTarget"
            }

            if ($PSCmdlet.ShouldProcess($resolvedTarget, 'Remove seeded GOOG sample directory')) {
                Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
            }
        }
    }

    if ($WhatIfPreference) {
        Write-Host 'Previewed removal of the seeded GOOG market-data directories; no files were changed.'
    }
    else {
        Write-Host 'Removed only the seeded GOOG market-data directories. Shared map and factor files were preserved.'
    }
    exit 0
}

$sharedDirectories = @(
    'equity\usa\map_files',
    'equity\usa\factor_files'
)

$copiedPaths = 0
$copiedFiles = 0
$skippedPaths = 0
$plannedPaths = 0
$plannedFiles = 0

foreach ($relativePath in @($sharedDirectories + $sampleDirectories)) {
    $source = Join-Path $engineData $relativePath
    $target = Join-Path $localData $relativePath
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Required LEAN sample directory is missing: $source"
    }

    $sourceFileCount = @(Get-ChildItem -LiteralPath $source -File -Recurse).Count
    if (Test-Path -LiteralPath $target) {
        Write-Host "present  $relativePath (left unchanged)"
        $skippedPaths++
        continue
    }

    if ($PSCmdlet.ShouldProcess($target, "Copy sample data from $source")) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target -Recurse
        Write-Host ("copied   {0} ({1} files)" -f $relativePath, $sourceFileCount)
        $copiedPaths++
        $copiedFiles += $sourceFileCount
    }
    elseif ($WhatIfPreference) {
        $plannedPaths++
        $plannedFiles += $sourceFileCount
    }
}

if ($WhatIfPreference) {
    Write-Host ("Previewed the GOOG sample-data seed: {0} paths / {1} files would be copied; {2} paths already present; no files were changed." -f $plannedPaths, $plannedFiles, $skippedPaths)
}
else {
    Write-Host ("Seeded audited LEAN sample data: {0} paths / {1} files copied; {2} paths already present and left unchanged." -f $copiedPaths, $copiedFiles, $skippedPaths)
}
Write-Host 'This does not add SPY option data for the 2021-2025 experiment.'
