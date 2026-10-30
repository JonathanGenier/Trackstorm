param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild, [switch]$OldMap, [switch]$Visual, [switch]$Aim, [switch]$Pond, [switch]$ControllerEmp, [switch]$DrivingMachineGun)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Hosted practice build failed.' }
}
$arguments = @('--headless', '--path', $PSScriptRoot, 'res://scenes/verification/hosted_practice_checks.tscn')
if ($Visual) { $arguments = $arguments | Where-Object { $_ -ne '--headless' } }
$arguments += '--'
if ($OldMap) { $arguments += '--old-map' }
if ($Visual) { $arguments += '--visual' }
if ($Aim) { $arguments += '--missile-aim' }
if ($ControllerEmp) { $arguments += '--controller-emp' }
if ($DrivingMachineGun) { $arguments += '--driving-mg' }
if ($Pond) { $arguments += '--pond-cycle' }
$log = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
$success = if ($DrivingMachineGun) { 'Hosted driving Machine Gun passed.' } elseif ($ControllerEmp) { 'Hosted controller EMP passed:' } elseif ($Aim) { 'Hosted Missile aiming passed:' } else { 'Hosted practice passed:' }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match $success)) { throw 'Hosted practice failed.' }
