param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Trophy truck verification build failed.' }
}
$output = & $GodotPath --headless --path $PSScriptRoot --fixed-fps 60 res://scenes/verification/trophy_truck_checks.tscn 2>&1
$result = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match 'Trophy truck integration passed:')) {
    throw 'Trophy truck integration failed; see .godot/ts-197/trophy.'
}
