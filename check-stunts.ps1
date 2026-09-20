param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Impaired,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Stunt build failed.' }
}
$stuntOutput = Join-Path $PSScriptRoot ('.godot/stunt-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stuntOutput -Force | Out-Null
$arguments = @('--headless', '--path', $PSScriptRoot, 'res://scenes/verification/stunt_checks.tscn', '--', "--stunt-output=$stuntOutput")
if ($Impaired) { $arguments += '--stunt-impaired' }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $stuntOutput 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Stunt integration passed.')) {
    throw "Stunt integration failed. Artifacts: $stuntOutput"
}
Write-Host "Stunt verification artifacts: $stuntOutput"
