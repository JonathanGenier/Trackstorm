param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$Quick, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Carnival smash build failed.' }
}
$smashOutput = Join-Path $PSScriptRoot ('.godot/ts-311/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smashOutput -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/carnival_smash_checks.tscn', '--', "--smash-output=$smashOutput")
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Quick) { $arguments += '--smash-quick' }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $smashOutput 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Carnival smash checks passed:')) { throw "Carnival smash checks failed: $smashOutput" }
Write-Host "Carnival smash artifacts: $smashOutput"
