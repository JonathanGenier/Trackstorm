param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild, [switch]$Visual)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Following-contact network build failed.' }
}
$arguments = @('--path', $PSScriptRoot)
if (-not $Visual) { $arguments += '--headless' }
$arguments += 'res://scenes/verification/following_contact_network_checks.tscn'
$output = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match 'Following contact network checks passed:')) { throw 'Following-contact network check failed.' }
