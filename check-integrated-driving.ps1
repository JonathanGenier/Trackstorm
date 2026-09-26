param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Integrated driving build failed.' }
}
# Real renderer is required for persistent MultiMesh and plant-buffer evidence.
$log = & $GodotPath --path $PSScriptRoot --fixed-fps 60 res://scenes/verification/integrated_driving_checks.tscn 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Integrated driving passed:')) {
    throw 'Integrated driving failed; see .godot/integrated-driving-checks.'
}
