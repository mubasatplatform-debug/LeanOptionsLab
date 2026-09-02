[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$workspace = Split-Path -Parent $PSScriptRoot
$engineData = Join-Path $workspace '.tools\lean-engine\Data'
$localData = Join-Path $workspace 'data'

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

foreach ($relativePath in @($sharedDirectories + $sampleDirectories)) {
    $source = Join-Path $engineData $relativePath
    $target = Join-Path $localData $relativePath
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Required LEAN sample directory is missing: $source"
    }

    if ($PSCmdlet.ShouldProcess($target, "Copy sample data from $source")) {
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
    }
}

if ($WhatIfPreference) {
    Write-Host 'Previewed the GOOG sample-data seed; no files were changed.'
}
else {
    Write-Host 'Seeded audited LEAN sample files for GOOG plus shared US equity map and factor files.'
}
Write-Host 'This does not add SPY option data for the 2021-2025 experiment.'
