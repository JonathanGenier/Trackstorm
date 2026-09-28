param(
    [string]$BlenderPath = "blender",
    [switch]$SkipAudit
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$rootCheck = Join-Path $PSScriptRoot "check-root-artifacts.ps1"
$exportScript = Join-Path $root "assets/vehicles/source/ExportCar.py"
$auditScript = Join-Path $root "assets/vehicles/source/AuditCar.py"
$snapshot = [System.IO.Path]::GetTempFileName()

if (Test-Path -LiteralPath $BlenderPath -PathType Leaf) {
    $resolvedBlender = (Resolve-Path -LiteralPath $BlenderPath).Path
}
else {
    $command = Get-Command $BlenderPath -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "Blender executable '$BlenderPath' was not found."
    }
    $resolvedBlender = $command.Source
}

try {
    & $rootCheck -WriteSnapshot $snapshot
    Push-Location $root
    try {
        & $resolvedBlender --background --python-exit-code 1 --python $exportScript
        if ($LASTEXITCODE -ne 0) {
            throw "Blender car export failed with exit code $LASTEXITCODE."
        }

        if (-not $SkipAudit) {
            & $resolvedBlender --background --python-exit-code 1 --python $auditScript
            if ($LASTEXITCODE -ne 0) {
                throw "Blender car audit failed with exit code $LASTEXITCODE."
            }
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    try {
        & $rootCheck -BaselineSnapshot $snapshot
    }
    finally {
        $resolvedSnapshot = [System.IO.Path]::GetFullPath($snapshot)
        $tempPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if (-not $resolvedSnapshot.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove snapshot outside the temporary directory: '$resolvedSnapshot'."
        }
        Remove-Item -LiteralPath $resolvedSnapshot -Force
    }
}

$operation = if ($SkipAudit) { "export" } else { "export and audit" }
Write-Host "Car Blender $operation completed without creating repository-root entries."
