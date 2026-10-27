param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild, [switch]$Motion)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Machine Gun VFX build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot '.godot/ts235-readability-vfx'
New-Item -ItemType Directory -Force $outputDirectory | Out-Null
$arguments = @('--path', $PSScriptRoot, '--resolution', '1440x900', 'res://scenes/verification/machine_gun_vfx_checks.tscn')
if ($Motion) { $arguments += @('--', '--vfx-motion') }
$output = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$output | Set-Content (Join-Path $outputDirectory 'runtime.log')
$output | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match 'Machine Gun VFX checks passed:')) { throw 'Machine Gun VFX checks failed.' }
