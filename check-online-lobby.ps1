param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Online lobby build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/online_lobby_checks.tscn', '--resolution', '1280x720', '--quit-after', '1200')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match 'Online lobby UI integration passed:')) {
    throw 'Online lobby UI verification failed.'
}
Write-Host 'Fake-provider UI verified. This does not authenticate EOS or establish multi-PC behavior.'
