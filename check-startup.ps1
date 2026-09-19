param(
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual,
    [switch]$SkipBuild,
    [switch]$SkipMediaCheck
)

$ErrorActionPreference = 'Stop'

if (-not $SkipMediaCheck) {
    & "$PSScriptRoot/tools/check-frontend-media.ps1"
}

if (-not $SkipBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Startup verification build failed.' }
}

$arguments = @('--path', $PSScriptRoot, 'res://scenes/main.tscn', '--quit-after', '3600')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$output = & $GodotPath @arguments -- '--startup-check' 2>&1
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
$reportedDiagnostics = $output -match 'ERROR:|WARNING:'
$reportedSuccess = $output -match 'Startup integration passed:'
Write-Host "Godot startup exit code: $exitCode; success marker: $reportedSuccess; diagnostics: $reportedDiagnostics."
if ($exitCode -ne 0 -or $reportedDiagnostics -or -not $reportedSuccess) {
    throw "Startup integration failed or did not complete (exit code $exitCode; success marker $reportedSuccess; diagnostics $reportedDiagnostics)."
}

Write-Host 'Startup integration checks passed.'
