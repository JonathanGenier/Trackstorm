param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Catapult build failed.' }
}
$outputDir = Join-Path $PSScriptRoot '.godot/ts175'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/car_rack_checks.tscn', '--quit-after', '18000', '--', '--salvo-catapult')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$log = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $outputDir 'mechanical-runtime.log')
$log | Write-Output
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Salvo catapult integration passed:')) { throw 'Catapult verification failed.' }
