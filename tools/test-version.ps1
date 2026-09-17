$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/version-rules.ps1"
$checks = 0
function Expect-Failure([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action } catch {
        $failed = $true
        if ($_.Exception.Message -notlike "*$Message*") { throw }
    }
    if (-not $failed) { throw "Expected failure: $Message" }
}
Assert-TrackstormVersionStep '0.0.1.4' '0.0.1.5'
Assert-TrackstormVersionStep $null '0.0.1.0'
$checks += 2
foreach ($actual in @('0.0.1.4', '0.0.1.6', '1.0.1.5', '0.1.1.5', '0.0.2.5', '0.0.1.05', '<missing or duplicated>')) {
    Expect-Failure { Assert-TrackstormVersionStep '0.0.1.4' $actual } "expected '0.0.1.5'; actual '$actual'"
    $checks++
}
# A previously valid parallel Story becomes invalid once main advances.
Expect-Failure { Assert-TrackstormVersionStep '0.0.1.5' '0.0.1.5' } "expected '0.0.1.6'; actual '0.0.1.5'"
Expect-Failure { Assert-TrackstormVersionStep '0.0.1.65534' '0.0.1.65535' } 'range exhausted'
$checks += 2
foreach ($value in @('', '0.0.1', '0.0.1.-1', '0.0.1.01', '0.0.1.65535', '0.0.1.1 ', "0.0.1.1`n", "0.0.1.1`r`n", '0.0.1.1-beta', '0.0.1.999999999999', '0.0.2.1')) {
    Expect-Failure { Get-TrackstormVersion "<Project><PropertyGroup><TrackstormVersion>$value</TrackstormVersion></PropertyGroup></Project>" } 'Expected canonical'
    $checks++
}
Expect-Failure { Get-TrackstormVersion '<Project />' } 'exactly one'
Expect-Failure { Get-TrackstormVersion '<Project><PropertyGroup><TrackstormVersion>0.0.1.0</TrackstormVersion><TrackstormVersion>0.0.1.1</TrackstormVersion></PropertyGroup></Project>' } 'exactly one'
if ($null -ne (Get-TrackstormVersion '<Project />' -AllowUninitialized)) { throw 'Initialization must be explicit.' }
foreach ($value in @('0.0.1.0', '0.0.1.1', '0.0.1.65534')) {
    if ((Get-TrackstormVersion "<Project><PropertyGroup><TrackstormVersion>$value</TrackstormVersion></PropertyGroup></Project>") -cne $value) { throw 'Version changed during parsing.' }
    $checks++
}
$checks += 3
Write-Host "Version rules: $checks checks passed."
