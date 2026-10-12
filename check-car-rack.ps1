param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild, [switch]$HandoffsOnly)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Car rack build failed.' }
}
$frameLimit = if ($HandoffsOnly) { '60000' } else { '20000' }
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/car_rack_checks.tscn', '--quit-after', $frameLimit)
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($HandoffsOnly) { $arguments += @('--', '--weapon-handoffs') }
$output = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$output | Write-Output
$success = if ($HandoffsOnly) { 'Weapon handoff integration passed:' } else { 'Car rack integration passed:' }
if ($code -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match $success)) { throw 'Car rack verification failed.' }
if ($HandoffsOnly) { return }
$consecutive = & $GodotPath @($arguments + @('--', '--consecutive-mines')) 2>&1
$code = $LASTEXITCODE
$consecutive | Write-Output
if ($code -ne 0 -or $consecutive -match 'ERROR:|WARNING:' -or -not ($consecutive -match 'Car rack consecutive mine passed:')) { throw 'Car rack consecutive mine verification failed.' }
& "$PSScriptRoot/check-car-rack.ps1" -GodotPath $GodotPath -Visual:$Visual -NoBuild -HandoffsOnly
