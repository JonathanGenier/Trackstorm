param(
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Match start build failed.' }
}
$output = Join-Path $PSScriptRoot ('.godot/match-start-checks/' + [Guid]::NewGuid().ToString('N'))
foreach ($scenario in @('two', 'four', 'leave', 'resume')) {
    $destination = Join-Path $output $scenario
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/match_start_checks.tscn', '--', "--start-output=$destination")
    if (-not $Visual) { $arguments = @('--headless') + $arguments }
    if ($scenario -in @('four', 'leave')) { $arguments += '--four' }
    if ($scenario -eq 'leave') { $arguments += '--leave' }
    if ($scenario -eq 'resume') { $arguments += '--resume' }
    $log = & $GodotPath @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $log | Set-Content -LiteralPath (Join-Path $destination 'runtime.log')
    $log | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Match start integration passed:')) {
        throw "Match start $scenario failed. Artifacts: $destination"
    }
}
Write-Host "Match start verification artifacts: $output"
