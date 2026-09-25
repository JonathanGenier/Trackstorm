param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$Impaired, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Salvo build failed.' }
}
$output = Join-Path $PSScriptRoot '.godot/salvo-checks'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/salvo_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += @('--', '--salvo-impaired') }
$log = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $output 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Salvo integration passed:')) { throw 'Salvo integration failed.' }
