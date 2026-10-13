param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild, [switch]$Motion)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Twin Fang build failed.' }
}
$arguments = @('--path', $PSScriptRoot, '--resolution', '1440x1000', 'res://scenes/verification/twin_fang_checks.tscn')
if ($Motion) { $arguments += @('--', '--twin-fang-motion') }
$output = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$output | Set-Content (Join-Path $PSScriptRoot '.godot/ts234-visual.log')
$output | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match 'Twin Fang visual checks passed:')) { throw 'Twin Fang visual checks failed.' }
