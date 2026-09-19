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
application/company_name="Thantrick"
application/product_name="Trackstorm"
application/file_description="Preserve this unrelated preference"
'@
$synchronized = Set-TrackstormExportPresetVersion $preset '0.0.15'
Assert-TrackstormExportPresetVersion $synchronized '0.0.15'
if ($synchronized -cne $preset.Replace('0.0.0.14', '0.0.15.0') -or (Set-TrackstormExportPresetVersion $synchronized '0.0.15') -cne $synchronized) {
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
$baseline = Transition '0.1.0' '0.1.0' 'baseline-correction' 'TS-94'
Assert-TrackstormVersionStep '0.1.0' '0.1.0' -TransitionJson $baseline -StoryBranch 'ts-94-jg'
Expect-Failure { Assert-TrackstormVersionStep '0.1.0' '0.1.0' -StoryBranch 'ts-94-jg' } "expected '0.1.1'"
Expect-Failure { Assert-TrackstormVersionStep '0.1.0' '0.1.0' -TransitionJson $baseline -BaseTransitionJson $baseline -StoryBranch 'ts-94-jg' } "expected '0.1.1'"
Expect-Failure { Assert-TrackstormVersionStep '0.1.0' '0.1.0' -TransitionJson ($baseline | ConvertFrom-Json | ConvertTo-Json) -BaseTransitionJson $baseline -StoryBranch 'ts-94-jg' } 'already been consumed'
foreach ($branch in @('ts-95-jg', 'ts-940-jg', 'other-ts-94-jg', '')) {
    Expect-Failure { Assert-TrackstormVersionStep '0.1.0' '0.1.0' -TransitionJson $baseline -StoryBranch $branch } 'branch Jira key'
    $checks++
}
foreach ($invalid in @(
    (Transition '0.1.0' '0.1.0' 'release' 'TS-94'),
    (Transition '0.1.0' '0.1.0' 'baseline-correction' 'TS-95'))) {
    Expect-Failure { Assert-TrackstormVersionStep '0.1.0' '0.1.0' -TransitionJson $invalid -StoryBranch (($invalid | ConvertFrom-Json).story + '-jg') } 'Only TS-94'
    $checks++
}
Expect-Failure { Assert-TrackstormVersionStep '0.1.1' '0.1.1' -TransitionJson (Transition '0.1.1' '0.1.1' 'baseline-correction' 'TS-94') -StoryBranch 'ts-94-jg' } 'Only TS-94'
Expect-Failure { Assert-TrackstormVersionStep '0.1.0' '0.1.1' -TransitionJson $baseline -StoryBranch 'ts-94-jg' } 'Normal Stories must preserve'
Assert-TrackstormVersionStep '0.1.0' '0.1.1' -TransitionJson $baseline -BaseTransitionJson $baseline -StoryBranch 'ts-95-jg'
Assert-TrackstormVersionStep '0.1.1' '0.1.2' -TransitionJson $baseline -BaseTransitionJson $baseline -StoryBranch 'ts-96-jg'
$checks += 8

# Reject every required metadata field independently, including malformed duplicates.
$validPreset = Set-TrackstormExportPresetVersion $preset '0.1.0'
foreach ($field in @('application/file_version', 'application/product_version', 'application/company_name', 'application/product_name')) {
    $value = Get-TrackstormExportPresetField $validPreset $field
    $line = "$field=`"$value`""
    foreach ($invalidPreset in @(
        $validPreset.Replace($line, ''),
        $validPreset.Replace($line, "$field=`"`""),
        $validPreset.Replace($line, "$field=`"wrong`""),
        $validPreset.Replace($line, "$field=`"$($value.ToLowerInvariant()) stale`""),
        "$validPreset`n$line",
        "$validPreset`n$field=unquoted",
        "$validPreset`n  $field = `"$value`"")) {
        Expect-Failure { Assert-TrackstormExportPresetVersion $invalidPreset '0.1.0' } ''
        $checks++
    }
}
foreach ($field in @('application/company_name', 'application/product_name')) {
    $value = Get-TrackstormExportPresetField $validPreset $field
    $wrongCase = $validPreset.Replace($value, $value.ToLowerInvariant())
    Expect-Failure { Assert-TrackstormExportPresetVersion $wrongCase '0.1.0' } 'fixed export identity'
    Expect-Failure { Set-TrackstormExportPresetVersion $wrongCase '0.1.1' } 'fixed export identity'
    $checks += 2
}

# Exercise the actual sync/check entry points in an isolated Git repository.
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('trackstorm-version-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$fixture/tools", "$fixture/.github" -Force | Out-Null
foreach ($script in @('version-rules.ps1', 'sync-story-version.ps1', 'check-version.ps1')) {
    Copy-Item -LiteralPath "$PSScriptRoot/$script" -Destination "$fixture/tools/$script"
}
function Invoke-FixtureGit {
    & git @args
    if ($LASTEXITCODE -ne 0) { throw "Fixture git command failed: $args" }
}
Push-Location $fixture
try {
    Invoke-FixtureGit init --quiet
    Invoke-FixtureGit config core.autocrlf false
    $fixtureProps = $props.Replace('0.1.4', '0.1.0')
    [IO.File]::WriteAllText("$fixture/Directory.Build.props", $fixtureProps)
    [IO.File]::WriteAllText("$fixture/export_presets.cfg", $validPreset)
    Invoke-FixtureGit add .
    Invoke-FixtureGit -c user.name=VersionTests -c user.email=version-tests@example.invalid commit --quiet -m baseline
    Invoke-FixtureGit branch fixture-main

    [IO.File]::WriteAllText("$fixture/.github/version-transition.json", $baseline)
    & ./tools/sync-story-version.ps1 -Kind baseline-correction -BaseRef fixture-main -StoryBranch ts-94-jg
    & ./tools/sync-story-version.ps1 -Kind baseline-correction -BaseRef fixture-main -StoryBranch ts-94-jg
    if ((Get-Content -Raw Directory.Build.props) -cne $fixtureProps -or (Get-Content -Raw export_presets.cfg) -cne $validPreset) {
        throw 'TS-94 must retain exactly the canonical 0.1.0 baseline and four required metadata values.'
    }
    Invoke-FixtureGit add .
    Invoke-FixtureGit -c user.name=VersionTests -c user.email=version-tests@example.invalid commit --quiet -m correction
    Invoke-FixtureGit branch -f fixture-main
    Expect-Failure { & ./tools/sync-story-version.ps1 -Kind baseline-correction -BaseRef fixture-main -StoryBranch ts-94-jg } "expected '0.1.1'"
    $checks += 2

    foreach ($case in @(@('normal', '0.1.1', 'ts-95-jg'), @('release', '0.2.0', 'ts-96-jg'))) {
        if ($case[0] -eq 'release') {
            [IO.File]::WriteAllText("$fixture/.github/version-transition.json", (Transition '0.1.1' '0.2.0' 'release' 'TS-96'))
        }
        & ./tools/sync-story-version.ps1 -Kind $case[0] -BaseRef fixture-main -StoryBranch $case[2]
        $actualProps = Get-Content -Raw Directory.Build.props
        $actualPreset = Get-Content -Raw export_presets.cfg
        if ((Get-TrackstormVersion $actualProps) -cne $case[1] -or $actualPreset -cne $validPreset.Replace('0.1.0.0', "$($case[1]).0")) {
            throw 'Synchronization must update both versions and preserve fixed identity and unrelated settings.'
        }
        & ./tools/sync-story-version.ps1 -Kind $case[0] -BaseRef fixture-main -StoryBranch $case[2]
        if ((Get-Content -Raw Directory.Build.props) -cne $actualProps -or (Get-Content -Raw export_presets.cfg) -cne $actualPreset) {
            throw 'Repeated synchronization must be byte-for-byte idempotent.'
        }
        Invoke-FixtureGit add .
        Invoke-FixtureGit -c user.name=VersionTests -c user.email=version-tests@example.invalid commit --quiet -m $case[0]
        Invoke-FixtureGit branch -f fixture-main
        $checks += 2
    }
} finally {
    Pop-Location
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolvedFixture -Leaf) -notlike 'trackstorm-version-*') {
        throw 'Refusing to remove a fixture outside the temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
Write-Host "Version rules: $checks checks passed."
