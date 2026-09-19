function Get-TrackstormVersion {
    param([Parameter(Mandatory)][string]$Xml, [switch]$AllowMigrationBase)
    [xml]$document = $Xml
    $nodes = @($document.SelectNodes('/Project/PropertyGroup/TrackstormVersion'))
    if ($nodes.Count -ne 1) { throw 'Expected exactly one TrackstormVersion in Directory.Build.props.' }
    $value = $nodes[0].InnerText
    # Only TS-70's exact established predecessor is accepted as a migration source.
    if ($AllowMigrationBase -and $value -ceq '0.0.1.0') { return $value }
    if ($value -cnotmatch '\A0\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\z' -or [int]$Matches[1] -gt 65534 -or [int]$Matches[2] -gt 65534) {
        throw "Expected canonical 0.RELEASE.PR (RELEASE = 0..65534; PR = 0..65534); actual '$value'."
    }
    return $value
}

function Assert-TrackstormVersionStep {
    param([Parameter(Mandatory)][string]$BaseVersion, [Parameter(Mandatory)][string]$Actual,
        [string]$TransitionJson = '', [string]$BaseTransitionJson = '', [string]$StoryBranch = '')
    $null = Get-TrackstormVersion "<Project><PropertyGroup><TrackstormVersion>$Actual</TrackstormVersion></PropertyGroup></Project>"
    $null = Get-TrackstormVersion "<Project><PropertyGroup><TrackstormVersion>$BaseVersion</TrackstormVersion></PropertyGroup></Project>" -AllowMigrationBase
    $changed = $TransitionJson.Trim() -cne $BaseTransitionJson.Trim()
    $migration = $BaseVersion -ceq '0.0.1.0'
    $parts = $BaseVersion.Split('.')
    $releaseChange = -not $migration -and $Actual.Split('.')[1] -cne $parts[1]
    if ($migration -or $releaseChange) {
        if (-not $changed -or -not $TransitionJson) { throw 'Release/migration transition requires a new explicit Story authorization.' }
        $transition = ConvertFrom-Json -InputObject $TransitionJson -AsHashtable
        if (($transition.Keys | Sort-Object) -join ',' -cne 'from,kind,story,to') { throw 'Transition authorization requires exactly from, kind, story and to.' }
        if ($transition.story -cnotmatch '\ATS-[1-9][0-9]*\z' -or $StoryBranch -notmatch ('(?:^|/)' + [regex]::Escape($transition.story) + '(?:-|$)')) {
            throw 'Transition Story must match the assigned branch Jira key.'
        }
        if ($transition.from -cne $BaseVersion -or $transition.to -cne $Actual) { throw "Transition authorization is stale or mismatched: expected from '$BaseVersion' to '$Actual'." }
        if ($migration) {
            if ($transition.kind -cne 'migration' -or $transition.story -cne 'TS-70') { throw 'Only TS-70 authorizes the four-to-three-component migration.' }
            $expected = '0.0.15'
        } else {
            if ($transition.kind -cne 'release') { throw 'A dedicated release Story requires kind release.' }
            $nextRelease = [int]$parts[1] + 1
            if ($nextRelease -gt 65534) { throw 'Release range exhausted; requires reviewed policy change.' }
            $expected = "0.$nextRelease.0"
        }
    } else {
        if ($changed) { throw 'Normal Stories must preserve the last transition authorization unchanged.' }
        $revision = [int]$parts[2] + 1
        if ($revision -gt 65534) { throw 'Revision range exhausted; requires a dedicated release Story.' }
        $expected = "0.$($parts[1]).$revision"
    }
    if ($Actual -cne $expected) {
        throw "Trackstorm Story version: expected '$expected'; actual '$Actual'; current main '$BaseVersion'. Synchronize main and recalculate."
    }
    Write-Host "Trackstorm Story version: expected '$expected'; actual '$Actual'."
}
