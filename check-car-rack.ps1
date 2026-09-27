param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Car rack build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/car_rack_checks.tscn', '--quit-after', '20000')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$output | Write-Output
if ($code -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match 'Car rack integration passed:')) { throw 'Car rack verification failed.' }
