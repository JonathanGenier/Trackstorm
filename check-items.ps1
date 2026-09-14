param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual,
    [switch]$Impaired,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Item build failed.' }
}
$itemOutput = Join-Path $PSScriptRoot ('.godot/item-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $itemOutput -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/item_checks.tscn', '--', "--item-output=$itemOutput")
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($Impaired) { $arguments += '--item-impaired' }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $itemOutput 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Item integration passed:')) {
    throw "Item integration failed. Artifacts: $itemOutput"
}
Write-Host "Item verification artifacts: $itemOutput"
