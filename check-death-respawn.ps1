param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual,
    [switch]$Impaired,
    [switch]$Water,
    [switch]$OutOfBounds,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Death/respawn build failed.' }
}
$deathOutput = Join-Path $PSScriptRoot ('.godot/death-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $deathOutput -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/death_respawn_checks.tscn', '--', "--death-output=$deathOutput")
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += '--death-impaired' }
if ($OutOfBounds) { $arguments += '--death-oob' }
if ($Water) { $arguments += '--death-water' }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $deathOutput 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Death/respawn integration passed.')) {
    throw "Death/respawn integration failed. Artifacts: $deathOutput"
}
Write-Host "Death/respawn verification artifacts: $deathOutput"
