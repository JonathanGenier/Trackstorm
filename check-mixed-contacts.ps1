param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Mixed contact verification build failed.' }
}
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60', 'res://scenes/verification/mixed_contact_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$log = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Mixed contact checks passed')) { throw 'Mixed contact checks failed.' }
