param (
    [Parameter(Mandatory)] [string]$GodotPath,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
# Validate source-controlled bytes before Godot can hide missing files behind its cache.
$manifest = Get-Content (Join-Path $PSScriptRoot 'assets/audio/sources.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $path = Join-Path $PSScriptRoot $entry.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing committed audio asset: $($entry.path)"
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Audio asset checksum mismatch: $($entry.path)"
    }
}
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
