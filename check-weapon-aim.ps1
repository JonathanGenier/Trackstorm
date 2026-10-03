param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild, [switch]$Visual, [switch]$Impaired, [switch]$Oval)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Weapon aiming build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot ('.godot/aim-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/weapon_aim_checks.tscn', '--quit-after', '6000', '--max-fps', '30')
if (-not $Visual) { $arguments += '--headless' }
$arguments += @('--', "--aim-output=$outputDirectory")
if ($Impaired) { $arguments += '--aim-impaired' }
if ($Oval) { $arguments += '--aim-oval' }
$output = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$output | Set-Content -LiteralPath (Join-Path $outputDirectory 'aim.log')
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Weapon aiming integration passed:')) { throw "Weapon aiming checks failed: $outputDirectory" }
Write-Host "Weapon aiming artifacts: $outputDirectory"
