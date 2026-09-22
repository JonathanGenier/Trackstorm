param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Camera build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot ('.godot/camera-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$output = & $GodotPath --headless --path $PSScriptRoot res://scenes/verification/camera_checks.tscn --quit-after 600 2>&1
$exitCode = $LASTEXITCODE
$output | Set-Content (Join-Path $outputDirectory 'camera.log')
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Camera integration passed:')) { throw 'Camera checks failed.' }
Write-Host "Camera verification artifacts: $outputDirectory"
