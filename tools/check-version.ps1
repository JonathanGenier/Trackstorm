param([string]$BaseRef = 'origin/main', [string]$StoryBranch = $env:GITHUB_HEAD_REF)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/version-rules.ps1"
if (-not $StoryBranch) { $StoryBranch = & git branch --show-current }
& git merge-base --is-ancestor $BaseRef HEAD
if ($LASTEXITCODE -ne 0) { throw "Story branch is stale: synchronize with current main ($BaseRef), then recalculate the version." }
$baseXml = & git show "${BaseRef}:Directory.Build.props"
if ($LASTEXITCODE -ne 0) { throw "Cannot read Directory.Build.props from current main ($BaseRef)." }
$baseVersion = Get-TrackstormVersion ($baseXml -join "`n") -AllowMigrationBase
$actual = Get-TrackstormVersion (Get-Content -Raw -LiteralPath "$PSScriptRoot/../Directory.Build.props")
$transitionPath = '.github/version-transition.json'
$baseFiles = & git ls-tree --name-only $BaseRef -- $transitionPath
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect main transition authorization.' }
$baseTransition = if ($baseFiles) { (& git show "${BaseRef}:$transitionPath") -join "`n" } else { '' }
$transitionFile = Join-Path "$PSScriptRoot/.." $transitionPath
$transition = if (Test-Path -LiteralPath $transitionFile) { Get-Content -Raw -LiteralPath $transitionFile } else { '' }
Assert-TrackstormVersionStep -BaseVersion $baseVersion -Actual $actual -TransitionJson $transition -BaseTransitionJson $baseTransition -StoryBranch $StoryBranch
