param([Parameter(Mandatory)][string]$GodotPath, [switch]$NoBuild, [switch]$Visual)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Repeated collision verification build failed.' }
}
$arguments = @('--path', $PSScriptRoot, '--fixed-fps', '60')
if (-not $Visual) { $arguments += '--headless' }
$cases = @(
    @('res://scenes/verification/repeated_vehicle_contact_checks.tscn'),
    @('res://scenes/verification/rock_collision_checks.tscn', '--', 'speed=3', 'angle=0', 'initial-speed=0', 'throttle=0.12', 'distance=6', 'label=ts280-slow'),
    @('res://scenes/verification/rock_collision_checks.tscn', '--', 'speed=3', 'angle=0', 'initial-speed=0', 'throttle=0.12', 'distance=0', 'height=8', 'vertical-speed=-8', 'label=ts280-landing'),
    @('res://scenes/verification/rock_collision_checks.tscn', '--', 'speed=3', 'angle=0', 'initial-speed=0', 'throttle=0.12', 'distance=5.5', 'label=ts280-close'),
    @('res://scenes/verification/rock_collision_checks.tscn', '--', 'speed=3', 'angle=0', 'initial-speed=0', 'throttle=0.12', 'embed=0.05', 'label=ts280-embedded')
)
foreach ($scenario in $cases) {
    $output = & $GodotPath @arguments @scenario 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or $output -match 'ERROR:|WARNING:|FAIL:') { throw 'Repeated collision check failed.' }
}
