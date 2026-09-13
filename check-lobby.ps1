param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Lobby build failed.' }
}
$lobbyOutput = Join-Path $PSScriptRoot ('.godot/lobby-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $lobbyOutput -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/lobby_checks.tscn', '--', "--lobby-output=$lobbyOutput")
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $lobbyOutput 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Lobby integration passed:')) {
    throw "Lobby integration failed. Artifacts: $lobbyOutput"
}
Write-Host "Lobby verification artifacts: $lobbyOutput"
