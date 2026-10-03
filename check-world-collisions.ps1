param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild, [switch]$Visual)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'World collision verification build failed.' }
}
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60')
if (-not $Visual) { $arguments += '--headless' }
$arguments += 'res://scenes/verification/world_collision_checks.tscn'
$output = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $output -match 'ERROR:|WARNING:|FAIL:' -or -not ($output -match 'World collision checks: failures=0')) { throw 'World collision checks failed.' }
