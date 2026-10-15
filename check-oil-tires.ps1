param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$Impaired, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Oil tire build failed.' }
}
$output = Join-Path $PSScriptRoot '.godot/oil-tire-checks'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60', 'res://scenes/verification/oil_tire_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += @('--', '--oil-impaired') }
$log = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$log | Set-Content (Join-Path $output 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($code -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Oil tire checks passed:')) { throw 'Oil tire checks failed.' }
