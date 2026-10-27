param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$Impaired, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Salvo build failed.' }
}
$output = Join-Path $PSScriptRoot '.godot/salvo-checks'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/salvo_projectile_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$log = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $output 'projectile-runtime.log')
$log | Write-Output
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Salvo projectile presentation passed:')) { throw 'Salvo projectile presentation failed.' }
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/salvo_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += @('--', '--salvo-impaired') }
$log = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $output 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Salvo integration passed:')) { throw 'Salvo integration failed.' }
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/weapon_aim_checks.tscn', '--', '--aim-salvo', "--aim-output=$output")
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += '--aim-impaired' }
$log = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $output 'aim-runtime.log')
$log | Write-Output
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Weapon aiming integration passed:')) { throw 'Salvo aiming integration failed.' }
$arguments += '--salvo-polish'
$arguments = $arguments | ForEach-Object { if ($_ -like '--aim-output=*') { "--aim-output=$output/polish" } else { $_ } }
$log = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $output 'polish-runtime.log')
$log | Write-Output
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Weapon aiming integration passed:')) { throw 'Salvo aiming polish integration failed.' }
