param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Play Menu build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/play_menu_checks.tscn', '--max-fps', '60', '--quit-after', '3000')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments 2>&1
$output | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match 'Play Menu checks passed:')) { throw 'Play Menu verification failed.' }
