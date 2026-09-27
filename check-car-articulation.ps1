param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual)
$ErrorActionPreference = 'Stop'
dotnet build "$PSScriptRoot/Trackstorm.Client.csproj" -c Debug -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Car build failed.' }
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60', '--resolution', '1280x800', 'res://scenes/verification/car_articulation_checks.tscn', '--quit-after', '3000')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$output | Write-Output
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Car articulation passed:')) { throw 'Car articulation verification failed.' }
