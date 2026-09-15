param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual,
    [switch]$Impaired,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Item spawn build failed.' }
}
$spawnOutput = Join-Path $PSScriptRoot ('.godot/spawn-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $spawnOutput -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/item_spawn_checks.tscn', '--', "--spawn-output=$spawnOutput")
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += '--spawn-impaired' }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $spawnOutput 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Item spawn integration passed:')) {
    throw "Item spawn integration failed. Artifacts: $spawnOutput"
}
Write-Host "Item spawn verification artifacts: $spawnOutput"
