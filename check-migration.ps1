param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Migration build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot '.godot/migration-checks'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$log = & $GodotPath --headless --path $PSScriptRoot res://scenes/verification/migration_checks.tscn 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $outputDirectory 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Migration integration passed:')) {
    throw 'Migration integration failed; inspect .godot/migration-checks/runtime.log.'
}
