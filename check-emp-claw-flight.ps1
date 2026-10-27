param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild, [switch]$ProductionOnly, [switch]$LongFlight, [switch]$Attachments, [switch]$Counterplay)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'EMP flight build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/emp_claw_flight_checks.tscn', '--resolution', '1280x720', '--quit-after', '24000')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if (@($ProductionOnly, $LongFlight, $Attachments, $Counterplay).Where({ $_ }).Count -gt 1) { throw 'Select only one focused flight mode.' }
if ($ProductionOnly) { $arguments += @('--', '--emp-production') }
if ($LongFlight) { $arguments += @('--', '--emp-long-flight') }
if ($Attachments) { $arguments += @('--', '--emp-attachments') }
if ($Counterplay) { $arguments += @('--', '--emp-counterplay') }
$output = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$output | Write-Output
$marker = if ($Counterplay) { 'EMP counterplay integration passed:' } elseif ($Attachments) { 'EMP attachment integration passed:' } elseif ($ProductionOnly) { 'EMP production terrain passed:' } else { 'EMP flight integration passed:' }
if ($code -ne 0 -or $output -match 'ERROR:|WARNING:' -or -not ($output -match $marker)) {
    throw 'EMP flight integration verification failed.'
}
