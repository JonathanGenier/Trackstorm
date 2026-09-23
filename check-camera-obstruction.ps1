param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild,
    [switch]$Baseline,
    [switch]$Drive,
    [ValidateSet(30, 60, 144)][int]$Fps = 60
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Camera obstruction build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot ('.godot/camera-obstruction-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputPath = Join-Path $outputDirectory 'obstruction'
$scene = if ($Drive) { 'res://scenes/verification/camera_obstruction_drive.tscn' } else { 'res://scenes/verification/camera_obstruction_checks.tscn' }
$arguments = @('--path', $PSScriptRoot, '--resolution', '1280x720', '--fixed-fps', "$Fps", $scene, '--quit-after', '1200000')
if ($Visual) { $arguments += @('--write-movie', "$outputPath.avi") }
else { $arguments = @('--headless') + $arguments }
$userArguments = @("--obstruction-output=$outputPath", "--test-fps=$Fps")
if ($Baseline) { $userArguments += '--baseline' }
$output = & $GodotPath @arguments -- @userArguments 2>&1
$exitCode = $LASTEXITCODE
$output | Set-Content (Join-Path $outputDirectory 'obstruction.log')
$output | ForEach-Object { Write-Host $_ }
$marker = if ($Baseline) { 'Camera obstruction baseline reproduced:' } elseif ($Drive) { 'Camera obstruction drive passed:' } else { 'Camera obstruction checks passed.' }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match $marker)) { throw "Camera obstruction checks failed: $outputDirectory" }
Write-Host "Camera obstruction verification artifacts: $outputDirectory"
