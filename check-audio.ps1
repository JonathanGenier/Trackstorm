param (
    [Parameter(Mandatory)] [string]$GodotPath,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Audio build failed.' }
}
$log = & $GodotPath --headless --path $PSScriptRoot 'res://scenes/verification/audio_checks.tscn' 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content (Join-Path $PSScriptRoot '.godot/audio-check.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Audio integration passed:')) {
    throw 'Audio integration failed. See .godot/audio-check.log.'
}
