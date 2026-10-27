param (
    [Parameter(Mandatory)][string]$GodotPath,
    [ValidateSet(2, 3)][int]$Players = 3,
    [switch]$OilFiring,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if ($OilFiring -and $Players -ne 3) { throw 'Oil firing recovery requires the three-peer Active-match scenario.' }
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Migration build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot ".godot/migration-checks/$Players-players"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$arguments = @('--headless', '--path', $PSScriptRoot, 'res://scenes/verification/migration_checks.tscn', '--', "--migration-players=$Players")
if ($OilFiring) { $arguments += '--oil-firing-recovery' }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $outputDirectory 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Migration integration passed:')) {
    throw 'Migration integration failed; inspect .godot/migration-checks/runtime.log.'
}
