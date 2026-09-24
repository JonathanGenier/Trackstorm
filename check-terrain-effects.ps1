param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild, [switch]$PresetsOnly)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Terrain effects build failed.' }
}
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60', 'res://scenes/verification/terrain_effects_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($PresetsOnly) { $arguments += @('--', '--presets-only') }
$log = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Terrain effects integration passed:')) {
    throw 'Terrain effects integration failed; see .godot/terrain-effects-checks.'
}
