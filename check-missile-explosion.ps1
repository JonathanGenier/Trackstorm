param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Missile explosion build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/missile_explosion_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$output = Join-Path $PSScriptRoot '.godot/ts241-checks'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$log | Set-Content -LiteralPath (Join-Path $output 'explosion-runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Missile explosion integration passed:')) {
    throw "Missile explosion verification failed. Artifacts: $output"
}
Write-Host "Missile explosion artifacts: $output"
