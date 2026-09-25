param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$Impaired,
    [switch]$OldMap,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Pickup drive build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot ('.godot/pickup-drive-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
foreach ($mode in @('inventory', 'motion')) {
    $arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/pickup_drive_checks.tscn', '--', "--pickup-output=$outputDirectory")
    if (-not $Visual) { $arguments = @('--headless') + $arguments }
    if ($mode -eq 'motion') { $arguments += '--pickup-motion' }
    if ($OldMap) { $arguments += '--pickup-old-map' }
    if ($Impaired) { $arguments += '--pickup-impaired' }
    $log = & $GodotPath @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $log | Set-Content -LiteralPath (Join-Path $outputDirectory "$mode.log")
    $log | ForEach-Object { Write-Host $_ }
    $expected = if ($mode -eq 'motion') { 'Pickup motion attempts: 180/180.' } else { 'Pickup drive passed:' }
    if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match [regex]::Escape($expected))) {
        throw "Pickup drive verification failed. Artifacts: $outputDirectory"
    }
}
Write-Host "Pickup drive verification artifacts: $outputDirectory"
