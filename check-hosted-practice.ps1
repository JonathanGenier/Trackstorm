param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild, [switch]$OldMap)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Hosted practice build failed.' }
}
$arguments = @('--headless', '--path', $PSScriptRoot, 'res://scenes/verification/hosted_practice_checks.tscn')
if ($OldMap) { $arguments += @('--', '--old-map') }
$log = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Hosted practice passed:')) { throw 'Hosted practice failed.' }
