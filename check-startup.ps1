param(
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual
)

$ErrorActionPreference = 'Stop'
dotnet build Trackstorm.sln -c Debug -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Startup verification build failed.' }
$arguments = @('--path', $PSScriptRoot, 'res://scenes/main.tscn', '--quit-after', '1200')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments -- '--startup-check' 2>&1
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Startup integration passed:')) {
    throw 'Startup integration failed or did not complete.'
}

Write-Host 'Startup integration checks passed.'
