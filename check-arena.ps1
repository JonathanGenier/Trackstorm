param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Arena build failed.' }
}
$arenaOutput = Join-Path $PSScriptRoot ('.godot/arena-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arenaOutput -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/arena_checks.tscn', '--', "--arena-output=$arenaOutput")
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $arenaOutput 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Arena integration passed:')) {
    throw "Arena integration failed. Artifacts: $arenaOutput"
}
Write-Host "Arena verification artifacts: $arenaOutput"
