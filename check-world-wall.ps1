param(
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Impaired,
    [switch]$Visual,
    [switch]$ProductionMap,
    [switch]$TerrainStability,
    [switch]$Profile,
    [switch]$Heavy,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'World wall build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/world_wall_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired -or $ProductionMap -or $TerrainStability -or $Profile -or $Heavy) { $arguments += '--' }
if ($Heavy) { $arguments += '--world-wall-heavy' }
if ($Profile) { $arguments += '--world-wall-profile' }
if ($Impaired) { $arguments += '--world-wall-impaired' }
if ($ProductionMap) { $arguments += '--world-wall-production' }
if ($TerrainStability) { $arguments += '--world-wall-terrain' }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$outputDirectory = if ($ProductionMap) { '.godot/world-wall-production-checks' } else { '.godot/world-wall-checks' }
$log | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$outputDirectory/runtime.log")
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'World wall integration passed:')) {
    throw "World wall integration failed; inspect $outputDirectory/runtime.log."
}
