param(
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual
)

$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/tools/check-frontend-media.ps1"
dotnet build Trackstorm.sln -c Debug -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Startup verification build failed.' }
# Native audio teardown needs real mixer time; uncapped headless frames can outrun its drain.
$arguments = @('--path', $PSScriptRoot, 'res://scenes/main.tscn', '--max-fps', '60', '--quit-after', '3600')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments -- '--startup-check' 2>&1
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Startup integration passed:')) {
    throw 'Startup integration failed or did not complete.'
}

Write-Host 'Startup integration checks passed.'
