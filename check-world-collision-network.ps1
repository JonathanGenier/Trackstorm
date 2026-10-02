param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'World collision network build failed.' }
}
$output = & $GodotPath --headless --path $PSScriptRoot res://scenes/verification/world_collision_network_checks.tscn 2>&1
$result = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match 'World collision multiplayer passed')) { throw 'World collision multiplayer checks failed.' }
