param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual
)

$ErrorActionPreference = 'Stop'
$checkDirectory = Join-Path $PSScriptRoot ('.godot/menu-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory -Force | Out-Null
dotnet build Trackstorm.sln -c Debug -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Menu verification build failed.' }
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/menu_checks.tscn', '--quit-after', '2400')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments -- "--menu-output=$checkDirectory" 2>&1
$exitCode = $LASTEXITCODE
$output | Set-Content (Join-Path $checkDirectory 'runtime.log')
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Menu integration passed:')) {
    throw 'Menu integration failed or did not complete.'
}
Write-Host "Menu verification artifacts: $checkDirectory"
