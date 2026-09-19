param(
    [ValidateSet('normal', 'release', 'migration', 'baseline-correction')][string]$Kind = 'normal',
    [string]$BaseRef = 'origin/main',
    [string]$StoryBranch = $env:GITHUB_HEAD_REF)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/version-rules.ps1"
if (-not $StoryBranch) { $StoryBranch = & git branch --show-current }
& git merge-base --is-ancestor $BaseRef HEAD
if ($LASTEXITCODE -ne 0) { throw "Story branch is stale: synchronize with current main ($BaseRef), then rerun synchronization." }
$baseXml = (& git show "${BaseRef}:Directory.Build.props") -join "`n"
if ($LASTEXITCODE -ne 0) { throw "Cannot read Directory.Build.props from current main ($BaseRef)." }
$baseVersion = Get-TrackstormVersion $baseXml -AllowMigrationBase
$transitionPath = '.github/version-transition.json'
$baseFiles = & git ls-tree --name-only $BaseRef -- $transitionPath
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect main transition authorization.' }
$baseTransition = if ($baseFiles) { (& git show "${BaseRef}:$transitionPath") -join "`n" } else { '' }
$transitionFile = Join-Path "$PSScriptRoot/.." $transitionPath
$transition = if (Test-Path -LiteralPath $transitionFile) { Get-Content -Raw -LiteralPath $transitionFile } else { '' }
$parts = $baseVersion.Split('.')
$expected = switch ($Kind) {
    'baseline-correction' { $baseVersion }
    'migration' { '0.0.15' }
    'release' {
        if ($parts.Count -ne 3) { throw 'A release transition requires a canonical three-component main version.' }
        $release = [int]$parts[1] + 1
        if ($release -gt 65534) { throw 'Release range exhausted; requires reviewed policy change.' }
        "0.$release.0"
    }
    default {
        if ($parts.Count -ne 3) { throw 'A normal Story requires a canonical three-component main version.' }
        $revision = [int]$parts[2] + 1
        if ($revision -gt 65534) { throw 'Revision range exhausted; requires a dedicated release Story.' }
        "0.$($parts[1]).$revision"
    }
}
Assert-TrackstormVersionStep -BaseVersion $baseVersion -Actual $expected -TransitionJson $transition -BaseTransitionJson $baseTransition -StoryBranch $StoryBranch
$propsPath = Join-Path "$PSScriptRoot/.." 'Directory.Build.props'
$props = Get-Content -Raw -LiteralPath $propsPath
$updatedProps = Set-TrackstormVersion -Xml $props -Version $expected
$presetPath = Join-Path "$PSScriptRoot/.." 'export_presets.cfg'
$preset = Get-Content -Raw -LiteralPath $presetPath
$updatedPreset = Set-TrackstormExportPresetVersion -Preset $preset -CanonicalVersion $expected
if ($updatedProps -cne $props) { [IO.File]::WriteAllText($propsPath, $updatedProps) }
if ($updatedPreset -cne $preset) { [IO.File]::WriteAllText($presetPath, $updatedPreset) }
Write-Host "Synchronized canonical Story version and tracked export preset to '$expected'."
& "$PSScriptRoot/check-version.ps1" -BaseRef $BaseRef -StoryBranch $StoryBranch
