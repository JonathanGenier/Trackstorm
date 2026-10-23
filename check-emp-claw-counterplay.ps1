param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/check-emp-claw-flight.ps1" -GodotPath $GodotPath -Visual:$Visual -NoBuild:$NoBuild -Counterplay
