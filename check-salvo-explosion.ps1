param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Salvo explosion build failed.' }
}
$output = Join-Path $PSScriptRoot '.godot/ts-213/polish1/explosions'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/salvo_explosion_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$log = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $output 'runtime.log')
$log | Write-Output
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Salvo explosion integration passed:')) { throw 'Salvo explosion integration failed.' }
