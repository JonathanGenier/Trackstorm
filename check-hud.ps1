param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'HUD build failed.' }
}
$hudOutput = Join-Path $PSScriptRoot ('.godot/hud-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $hudOutput -Force | Out-Null
$hudLog = & $GodotPath --path $PSScriptRoot --position 0,0 --resolution 640x360 res://scenes/verification/hud_checks.tscn -- "--hud-output=$hudOutput" 2>&1
$hudExit = $LASTEXITCODE
$hudLog | Set-Content -LiteralPath (Join-Path $hudOutput 'runtime.log')
$hudLog | ForEach-Object { Write-Host $_ }
if ($hudExit -ne 0 -or $hudLog -match 'ERROR:|WARNING:' -or -not ($hudLog -match 'HUD integration passed:')) {
    throw "HUD integration failed. Artifacts: $hudOutput"
}
Write-Host "HUD verification artifacts: $hudOutput"
