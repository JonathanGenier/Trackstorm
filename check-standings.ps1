param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$NoBuild,
    [switch]$Visual
)

$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Standings build failed.' }
}
$standingsArgs = @('--path', $PSScriptRoot)
if (-not $Visual) { $standingsArgs += '--headless' }
$standingsArgs += 'res://scenes/verification/standings_checks.tscn'
$standingsLog = & $GodotPath @standingsArgs 2>&1
$standingsExit = $LASTEXITCODE
$standingsLog | Set-Content -LiteralPath (Join-Path $PSScriptRoot '.godot/ts28-standings.log')
$standingsLog | ForEach-Object { Write-Host $_ }
if ($standingsExit -ne 0 -or $standingsLog -match 'ERROR:|WARNING:' -or -not ($standingsLog -match 'Standings integration passed:')) {
    throw 'Standings integration failed; see .godot/ts28-standings.log.'
}
