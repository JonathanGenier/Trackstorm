param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild, [switch]$TuningOnly)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Destructible environment build failed.' }
}
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60', 'res://scenes/verification/destructible_environment_checks.tscn')
if ($TuningOnly) { $arguments += @('--', '--destruction-tuning') }
# Like the map-budget harness, use the real renderer: dummy rendering does not
# retain all MultiMesh transform data needed to verify clearing and reset.
$log = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:|FAIL:' -or -not ($log -match 'Destructible environment checks: failures=0')) {
    throw 'Destructible environment checks failed; see .godot/destructible-checks.'
}
