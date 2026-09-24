param([Parameter(Mandatory)][string]$GodotPath)
$ErrorActionPreference = 'Stop'
# Use the real renderer: dummy rendering does not retain all MultiMesh data.
$log = & $GodotPath --path $PSScriptRoot --script res://scenes/verification/map_budget_checks.gd 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Map budget inventory passed:')) {
    throw 'Map budget inventory failed; see .godot/map-budget-checks.'
}
