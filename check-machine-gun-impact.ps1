param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Machine Gun impact build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot '.godot/machine-gun-impact-checks'
New-Item -ItemType Directory -Force $outputDirectory | Out-Null
$output = & $GodotPath --path $PSScriptRoot --resolution 1440x900 res://scenes/verification/machine_gun_impact_checks.tscn 2>&1
$result = $LASTEXITCODE
$output | Set-Content (Join-Path $outputDirectory 'runtime.log')
$output | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match 'Machine Gun impact checks passed:')) { throw 'Machine Gun impact checks failed.' }
