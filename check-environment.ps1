param(
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual
)
$ErrorActionPreference = 'Stop'
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60', '--script', 'res://scenes/verification/environment_library_checks.gd')
if (-not $Visual) { $arguments = @('--headless') + $arguments + @('--', '--no-visual') }
$log = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Environment library passed.')) {
    throw 'Environment library verification failed; inspect .godot/environment-checks.'
}
