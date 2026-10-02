param(
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Impaired,
    [switch]$Visual,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'World wall build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/world_wall_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += @('--', '--world-wall-impaired') }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $PSScriptRoot '.godot/world-wall-checks/runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'World wall integration passed:')) {
    throw 'World wall integration failed; inspect .godot/world-wall-checks/runtime.log.'
}
