param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Impaired,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Shield build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot ('.godot/shield-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$arguments = @('--headless', '--path', $PSScriptRoot, 'res://scenes/verification/shield_checks.tscn', '--', "--shield-output=$outputDirectory")
if ($Impaired) { $arguments += '--shield-impaired' }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $outputDirectory 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Shield integration passed:')) {
    throw "Shield integration failed. Artifacts: $outputDirectory"
}
Write-Host "Shield verification artifacts: $outputDirectory"
