param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$Impaired, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Proxy Mine build failed.' }
}
$output = Join-Path $PSScriptRoot '.godot/mine-checks'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/mine_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += @('--', '--mine-impaired') }
$log = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $output 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Proxy Mine integration passed:')) { throw 'Proxy Mine integration failed.' }
