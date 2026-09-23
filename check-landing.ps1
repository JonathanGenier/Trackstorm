param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild,
    [string]$Case = ''
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Landing verification build failed.' }
}
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60', 'res://scenes/verification/landing_checks.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Case) { $arguments += @('--', "--landing-case=$Case") }
$log = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$log | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Landing integration passed.')) {
    throw 'Landing integration failed; see .godot/landing-checks.'
}
