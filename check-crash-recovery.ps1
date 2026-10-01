param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild, [string]$Case = '')
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Crash recovery build failed.' }
}
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60', 'res://scenes/verification/crash_recovery_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Case) { $arguments += @('--', "--crash-case=$Case") }
$log = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Crash recovery checks passed.')) {
    throw 'Crash recovery checks failed; see .godot/crash-recovery-checks.'
}
