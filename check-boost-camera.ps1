param(
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$NoBuild,
    [switch]$Visual
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Boost camera build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot '.godot/ts-226/playtest'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, '--resolution', '1280x720', '--fixed-fps', '60', 'res://scenes/verification/boost_camera_playtest.tscn', '--quit-after', '2000')
if ($Visual) { $arguments += @('--', '--boost-camera-captures') }
else { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$output | Set-Content (Join-Path $outputDirectory 'playtest.log')
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Boost camera playtest passed:')) { throw 'Boost camera playtest failed.' }
Write-Host "Boost camera evidence: $outputDirectory"
