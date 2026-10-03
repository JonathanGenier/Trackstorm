param(
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Impaired,
    [switch]$Visual,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Rear shield build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/rear_shield_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += @('--', '--rear-shield-impaired') }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $PSScriptRoot '.godot/rear-shield-checks/runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Rear shield integration passed:')) {
    throw 'Rear shield integration failed; inspect .godot/rear-shield-checks/runtime.log.'
}
