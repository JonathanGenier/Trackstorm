param([string]$BaseRef = 'origin/main')
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/version-rules.ps1"

$baseXml = & git show "${BaseRef}:Directory.Build.props"
if ($LASTEXITCODE -ne 0) { throw "Cannot read Directory.Build.props from current main ($BaseRef)." }
$baseVersion = Get-TrackstormVersion ($baseXml -join "`n") -AllowUninitialized
$actualXml = Get-Content -Raw -LiteralPath "$PSScriptRoot/../Directory.Build.props"
[xml]$actualDocument = $actualXml
$actualNodes = @($actualDocument.SelectNodes('/Project/PropertyGroup/TrackstormVersion'))
$actual = if ($actualNodes.Count -eq 1) { $actualNodes[0].InnerText } else { '<missing or duplicated>' }
Assert-TrackstormVersionStep -BaseVersion $baseVersion -Actual $actual
$null = Get-TrackstormVersion $actualXml
& git merge-base --is-ancestor $BaseRef HEAD
if ($LASTEXITCODE -ne 0) { throw "Version '$actual' is stale: synchronize this Story branch with current main ($BaseRef), then rerun the expected/actual check." }
