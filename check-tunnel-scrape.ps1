param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild, [switch]$Visual, [string]$Case = '', [string]$Label = 'checks')
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Tunnel scrape verification build failed.' }
}
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60')
if (-not $Visual) { $arguments += '--headless' }
$arguments += @('res://scenes/verification/tunnel_scrape_checks.tscn', '--', "label=$Label")
if ($Case) { $arguments += "case=$Case" }
$output = & $GodotPath @arguments 2>&1
$result = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($result -ne 0 -or $output -match 'ERROR:|WARNING:|FAIL:' -or -not ($output -match 'Tunnel scrape checks: failures=0')) { throw 'Tunnel scrape checks failed.' }
