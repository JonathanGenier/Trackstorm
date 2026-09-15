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
    if ($LASTEXITCODE -ne 0) { throw 'Match build failed.' }
}
$matchOutput = Join-Path $PSScriptRoot ('.godot/match-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $matchOutput -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/match_checks.tscn', '--', "--match-output=$matchOutput")
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += '--match-impaired' }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $matchOutput 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Match integration passed.')) {
    throw "Match integration failed. Artifacts: $matchOutput"
}
Write-Host "Match verification artifacts: $matchOutput"
