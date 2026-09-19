param(
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual
)

$ErrorActionPreference = 'Stop'
$manifest = Get-Content (Join-Path $PSScriptRoot 'assets/frontend/sources.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    $path = Join-Path $PSScriptRoot $entry.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing committed frontend media asset: $($entry.path)"
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Frontend media checksum mismatch: $($entry.path)"
    }
}
dotnet build Trackstorm.sln -c Debug -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Startup verification build failed.' }
$arguments = @('--path', $PSScriptRoot, 'res://scenes/main.tscn', '--quit-after', '3600')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments -- '--startup-check' 2>&1
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Startup integration passed:')) {
    throw 'Startup integration failed or did not complete.'
}

Write-Host 'Startup integration checks passed.'
