param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Camera shake build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot ('.godot/camera-shake-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputPath = Join-Path $outputDirectory 'shake'
$frameLimit = if ($Visual) { '1800' } else { '1200000' }
$arguments = @('--path', $PSScriptRoot, '--resolution', '1280x720', '--fixed-fps', '60', 'res://scenes/verification/camera_shake_playtest.tscn', '--quit-after', $frameLimit)
if ($Visual) { $arguments += @('--write-movie', "$outputPath.avi") }
else { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments -- "--shake-output=$outputPath" 2>&1
$exitCode = $LASTEXITCODE
$output | Set-Content (Join-Path $outputDirectory 'shake.log')
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Camera shake playtest passed:')) { throw 'Camera shake playtest failed.' }
Write-Host "Camera shake verification artifacts: $outputDirectory"
