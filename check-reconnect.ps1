param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Reconnect build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/reconnect_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$outputDirectory = Join-Path $PSScriptRoot '.godot/reconnect-checks'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $outputDirectory 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Reconnect integration passed:')) {
    throw 'Reconnect integration failed; inspect .godot/reconnect-checks/runtime.log.'
}
