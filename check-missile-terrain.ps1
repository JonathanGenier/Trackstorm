param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$ProductionOnly,
    [string]$Case = "",
    [switch]$Diagnose,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Missile build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/missile_terrain_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($ProductionOnly -or $Diagnose -or $Case) { $arguments += @('--', '--missile-production') }
if ($Diagnose) { $arguments += '--missile-diagnose' }
if ($Case) { $arguments += "--missile-case=$Case" }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$output = Join-Path $PSScriptRoot '.godot/missile-checks'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$log | Set-Content -LiteralPath (Join-Path $output 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
$marker = if ($Diagnose) { 'Missile terrain diagnosis complete:' } else { 'Missile terrain integration passed:' }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match $marker)) {
    throw "Missile terrain verification failed. Artifacts: $output"
}
Write-Host "Missile verification artifacts: $output"
