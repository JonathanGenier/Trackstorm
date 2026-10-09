param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild, [string]$Case = '')
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Combat collision verification build failed.' }
}
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60', 'res://scenes/verification/combat_collision_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Case) { $arguments += @('--', "--combat-case=$Case") }
$log = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Combat collision integration passed')) { throw 'Combat collision integration failed.' }
