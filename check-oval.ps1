param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Oval verification build failed.' }
}
$ovalOutput = Join-Path $PSScriptRoot ('.godot/oval-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $ovalOutput -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60', 'res://scenes/verification/oval_checks.tscn', '--', "--oval-output=$ovalOutput")
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $ovalOutput 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Oval integration passed:')) {
    throw "Oval integration failed. Artifacts: $ovalOutput"
}
Write-Host "Oval verification artifacts: $ovalOutput"
