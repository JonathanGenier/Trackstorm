param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$Impaired, [switch]$NoBuild, [switch]$VisualOnly, [switch]$ProductionBank, [switch]$Clearance, [switch]$GameplayCamera)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Oil Jack build failed.' }
}
$output = Join-Path $PSScriptRoot $(if ($Clearance) { '.godot/ts-245-round8/clearance' } elseif ($GameplayCamera) { '.godot/ts-245-round8/gameplay' } elseif ($ProductionBank) { '.godot/ts-245-round8/bank' } else { '.godot/ts-245-round8/runtime' })
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/oil_jack_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired -or $VisualOnly -or $ProductionBank -or $Clearance -or $GameplayCamera) { $arguments += '--' }
if ($Impaired) { $arguments += '--oil-impaired' }; if ($VisualOnly) { $arguments += '--oil-visual-only' }
if ($ProductionBank) { $arguments += '--oil-production-bank' }
if ($Clearance) { $arguments += '--oil-clearance' }
if ($GameplayCamera) { $arguments += '--oil-gameplay-camera' }
$log = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $output 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Oil jack integration passed:')) { throw 'Oil Jack integration failed.' }
