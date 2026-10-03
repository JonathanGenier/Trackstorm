param(
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$NoBuild,
    [switch]$Visual
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Camera responsiveness build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot ('.godot/ts-279/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, '--resolution', '1280x720', '--fixed-fps', '60', 'res://scenes/verification/camera_responsiveness_playtest.tscn', '--quit-after', '5000')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$arguments += @('--', "--camera-output=$outputDirectory")
if ($Visual) { $arguments += '--camera-captures' }
$output = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$output | Set-Content (Join-Path $outputDirectory 'playtest.log')
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Camera responsiveness playtest passed:')) { throw "Camera responsiveness playtest failed: $outputDirectory" }
Write-Host "Camera responsiveness evidence: $outputDirectory"
