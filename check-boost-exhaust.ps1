param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    & "$PSScriptRoot/tools/check-fast.ps1" -Area Client
    if ($LASTEXITCODE -ne 0) { throw 'Boost VFX build failed.' }
}
# Rendering is required: headless particle state cannot establish visual quality.
$output = Join-Path $PSScriptRoot '.godot/ts225-vfx'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$log = & $GodotPath --path $PSScriptRoot --resolution 1280x720 'res://scenes/verification/boost_exhaust_checks.tscn' 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $output 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Boost VFX checks passed:')) { throw 'Boost VFX checks failed.' }
