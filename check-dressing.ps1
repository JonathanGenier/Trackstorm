param(
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual
)
$ErrorActionPreference = 'Stop'
$arguments = @('--path', $PSScriptRoot, '--script', 'res://scenes/verification/dressing_checks.gd')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$log = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Dressing checks: .*; 0 failures.')) {
    throw 'Production dressing verification failed.'
}
