param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Collision verification build failed.' }
}
$output = & $GodotPath --headless --path $PSScriptRoot --fixed-fps 60 res://scenes/verification/environment_collision_checks.tscn 2>&1
$result = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match 'Environment collision checks: failures=0')) { throw 'Environment collision checks failed.' }
