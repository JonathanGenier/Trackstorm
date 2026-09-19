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
function Transition([string]$from, [string]$to, [string]$kind = 'release', [string]$story = 'TS-71') {
    return @{ from = $from; to = $to; kind = $kind; story = $story } | ConvertTo-Json -Compress
}
foreach ($pair in @(@('0.0.15','0.0.16'), @('0.1.0','0.1.1'), @('0.1.14','0.1.15'), @('0.2.0','0.2.1'))) {
    Assert-TrackstormVersionStep $pair[0] $pair[1]
    $checks++
}
foreach ($actual in @('0.1.4','0.1.6')) {
    Expect-Failure { Assert-TrackstormVersionStep '0.1.4' $actual } "expected '0.1.5'; actual '$actual'"
    $checks++
}
Expect-Failure { Assert-TrackstormVersionStep '0.1.5' '0.1.5' } "expected '0.1.6'"
Expect-Failure { Assert-TrackstormVersionStep '0.1.65534' '0.1.65534' } 'range exhausted'
$checks += 2
foreach ($value in @('', '0.1', '0.0.1.15', '0.1.-1', '0.01.1', '0.1.01', '0.1.65535', '0.65535.0', '0.1.1 ', "0.1.1`n", '0.1.1-beta', '0.1.999999999999', '1.1.0')) {
    Expect-Failure { Get-TrackstormVersion "<Project><PropertyGroup><TrackstormVersion>$value</TrackstormVersion></PropertyGroup></Project>" } 'Expected canonical'
    $checks++
}
Expect-Failure { Get-TrackstormVersion '<Project />' } 'exactly one'
Expect-Failure { Get-TrackstormVersion '<Project><PropertyGroup><TrackstormVersion>0.1.0</TrackstormVersion><TrackstormVersion>0.1.1</TrackstormVersion></PropertyGroup></Project>' } 'exactly one'
$checks += 2
foreach ($base in @('0.1.0','0.1.15')) {
    $auth = Transition $base '0.2.0'
    Assert-TrackstormVersionStep $base '0.2.0' -TransitionJson $auth -StoryBranch 'ts-71-jg'
    Expect-Failure { Assert-TrackstormVersionStep $base '0.2.0' } 'requires a new explicit'
    Expect-Failure { Assert-TrackstormVersionStep $base '0.2.0' -TransitionJson $auth -BaseTransitionJson $auth -StoryBranch 'ts-71-jg' } 'requires a new explicit'
    Expect-Failure { Assert-TrackstormVersionStep $base '0.2.0' -TransitionJson $auth -StoryBranch 'ts-99-jg' } 'branch Jira key'
    $checks += 4
}
foreach ($target in @('0.8.0','0.2.1')) {
    Expect-Failure { Assert-TrackstormVersionStep '0.1.15' $target -TransitionJson (Transition '0.1.15' $target) -StoryBranch 'ts-71-jg' } "expected '0.2.0'"
    $checks++
}
$old = Transition '0.1.14' '0.2.0'
Expect-Failure { Assert-TrackstormVersionStep '0.1.15' '0.2.0' -TransitionJson $old -StoryBranch 'ts-71-jg' } 'stale or mismatched'
Expect-Failure { Assert-TrackstormVersionStep '0.1.0' '0.1.1' -TransitionJson $old } 'Normal Stories must preserve'
Assert-TrackstormVersionStep '0.2.0' '0.2.1' -TransitionJson $old -BaseTransitionJson $old
$migration = Transition '0.0.1.0' '0.0.15' 'migration' 'TS-70'
Assert-TrackstormVersionStep '0.0.1.0' '0.0.15' -TransitionJson $migration -StoryBranch 'ts-70-jg'
Expect-Failure { Assert-TrackstormVersionStep '0.0.1.0' '0.0.15' } 'requires a new explicit'
Expect-Failure { Assert-TrackstormVersionStep '0.0.1.0' '0.0.15' -TransitionJson (Transition '0.0.1.0' '0.0.15') -StoryBranch 'ts-71-jg' } 'Only TS-70'
Expect-Failure { Assert-TrackstormVersionStep '0.0.1.1' '0.0.15' -TransitionJson $migration -StoryBranch 'ts-70-jg' } 'Expected canonical'
$checks += 7
$zeroRelease = Transition '0.0.15' '0.1.0'
Assert-TrackstormVersionStep '0.0.15' '0.1.0' -TransitionJson $zeroRelease -StoryBranch 'ts-71-jg'
Expect-Failure { Assert-TrackstormVersionStep '0.0.15' '0.1.0' } 'requires a new explicit'
Expect-Failure { Assert-TrackstormVersionStep '0.0.1.0' '0.1.0' -TransitionJson (Transition '0.0.1.0' '0.1.0' 'migration' 'TS-70') -StoryBranch 'ts-70-jg' } "expected '0.0.15'"
$checks += 3
$props = '<Project><PropertyGroup><TrackstormVersion>0.1.4</TrackstormVersion></PropertyGroup></Project>'
$updatedProps = Set-TrackstormVersion $props '0.1.5'
if ($updatedProps -cnotmatch '<TrackstormVersion>0\.1\.5</TrackstormVersion>' -or (Set-TrackstormVersion $updatedProps '0.1.5') -cne $updatedProps) {
    throw 'Canonical property synchronization must be deterministic and idempotent.'
}
$checks++
$preset = @'
application/file_version="0.0.0.14"
application/product_version="0.0.0.14"
application/company_name="Trackstorm Company"
'@
$synchronized = Set-TrackstormExportPresetVersion $preset '0.0.15'
Assert-TrackstormExportPresetVersion $synchronized '0.0.15'
if ($synchronized -notmatch 'application/company_name="Trackstorm Company"' -or (Set-TrackstormExportPresetVersion $synchronized '0.0.15') -cne $synchronized) {
    throw 'Export preset synchronization changed unrelated preferences or was not idempotent.'
}
$checks += 2
foreach ($drifted in @(
    $synchronized.Replace('application/file_version="0.0.15.0"', 'application/file_version="0.0.14.0"'),
    $synchronized.Replace('application/product_version="0.0.15.0"', 'application/product_version="0.0.14.0"'))) {
    Expect-Failure { Assert-TrackstormExportPresetVersion $drifted '0.0.15' } 'Tracked export preset drift'
    $checks++
}
foreach ($case in @(
    @('application/product_version="0.0.15.0"', 'export_presets.cfg'),
    @('application/file_version="0.0.15.0"', 'export_presets.cfg'),
    @("application/file_version=`"0.0.15.0`"`napplication/file_version=`"0.0.15.0`"`napplication/product_version=`"0.0.15.0`"", 'export_presets.cfg'),
    @("application/file_version=`"0.0.15.0`"`napplication/product_version=`"0.0.15.0`"`napplication/product_version=`"0.0.15.0`"", 'export_presets.cfg'),
    @("application/file_version=0.0.15.0`napplication/product_version=`"0.0.15.0`"", 'export_presets.cfg'),
    @("application/file_version=`"0.0.15.0`"`napplication/product_version=0.0.15.0", 'export_presets.cfg'),
    @("application/file_version=`"0.0.15`"`napplication/product_version=`"0.0.15.0`"", 'Windows numeric'),
    @("application/file_version=`"0.0.15.0`"`napplication/product_version=`"0.0.15`"", 'Windows numeric'))) {
    Expect-Failure { Assert-TrackstormExportPresetVersion $case[0] '0.0.15' } $case[1]
    $checks++
}
Write-Host "Version rules: $checks checks passed."
