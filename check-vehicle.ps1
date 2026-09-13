param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual
)

$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path $PSScriptRoot ('.godot/vehicle-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
dotnet build Trackstorm.sln -c Debug -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Vehicle verification build failed.' }
foreach ($fps in @(30, 144)) {
    $outputPath = Join-Path $outputDirectory "fps-$fps"
    $arguments = @('--headless', '--path', $PSScriptRoot, '--fixed-fps', "$fps", 'res://scenes/verification/vehicle_checks.tscn', '--quit-after', '1200000')
    $output = & $GodotPath @arguments -- "--vehicle-output=$outputPath" 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Vehicle integration passed:')) { throw "Vehicle checks failed at $fps render FPS." }
}
$first = Get-Content (Join-Path $outputDirectory 'fps-30.replay.json') | ConvertFrom-Json
$second = Get-Content (Join-Path $outputDirectory 'fps-144.replay.json') | ConvertFrom-Json
if ($first.Count -ne 3840 -or $second.Count -ne $first.Count) { throw 'Fixed-step replay traces are incomplete.' }
for ($index = 0; $index -lt $first.Count; $index++) {
    if ([Math]::Abs($first[$index] - $second[$index]) -gt 0.02) { throw "Fixed-step replay diverged across render rates at component $index." }
}
Write-Host 'Fixed-step replay matched across 30 and 144 render FPS (0.02 tolerance).'
$surfaceFirst = Get-Content (Join-Path $outputDirectory 'fps-30.surfaces.json') | ConvertFrom-Json
$surfaceSecond = Get-Content (Join-Path $outputDirectory 'fps-144.surfaces.json') | ConvertFrom-Json
if ($surfaceFirst.Count -ne 8400 -or $surfaceSecond.Count -ne $surfaceFirst.Count) { throw 'Surface replay traces are incomplete.' }
for ($index = 0; $index -lt $surfaceFirst.Count; $index++) {
    if ([Math]::Abs($surfaceFirst[$index] - $surfaceSecond[$index]) -gt 0.02) { throw "Surface replay diverged across render rates at component $index." }
}
Write-Host 'Surface replay matched across 30 and 144 render FPS (0.02 tolerance).'

if ($Visual) {
    $outputPath = Join-Path $outputDirectory 'visual'
    $output = & $GodotPath --path $PSScriptRoot --fixed-fps 60 res://scenes/verification/vehicle_checks.tscn --quit-after 12000 -- "--vehicle-output=$outputPath" 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Vehicle integration passed:')) { throw 'Rendered vehicle checks failed.' }
}
Write-Host "Vehicle verification artifacts: $outputDirectory"
