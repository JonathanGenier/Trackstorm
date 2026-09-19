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

function Set-TrackstormVersion {
    param([Parameter(Mandatory)][string]$Xml, [Parameter(Mandatory)][string]$Version)
    $null = Get-TrackstormVersion "<Project><PropertyGroup><TrackstormVersion>$Version</TrackstormVersion></PropertyGroup></Project>"
    [xml]$document = $Xml
    $nodes = @($document.SelectNodes('/Project/PropertyGroup/TrackstormVersion'))
    if ($nodes.Count -ne 1) { throw 'Expected exactly one TrackstormVersion in Directory.Build.props.' }
    $pattern = '(?s)(<TrackstormVersion>)[^<]*(</TrackstormVersion>)'
    if ([regex]::Matches($Xml, $pattern).Count -ne 1) { throw 'Expected exactly one serializable TrackstormVersion in Directory.Build.props.' }
    return [regex]::Replace($Xml, $pattern, ('${1}' + $Version + '${2}'))
}

function Get-TrackstormExportPresetField {
    param([Parameter(Mandatory)][string]$Preset, [Parameter(Mandatory)][string]$Field)
    # Count assignments separately so an additional malformed assignment cannot hide behind a valid one.
    $key = [regex]::Escape($Field)
    $assignments = [regex]::Matches($Preset, ('(?m)^[\t ]*' + $key + '[\t ]*='))
    $values = [regex]::Matches($Preset, ('(?m)^' + $key + '="([^"\r\n]*)"(?=\r?$)'))
    if ($assignments.Count -ne 1 -or $values.Count -ne 1) {
        throw "Expected exactly one quoted tracked $Field field in export_presets.cfg."
    }
    return $values[0].Groups[1].Value
}

function Get-TrackstormExportPresetVersions {
    param([Parameter(Mandatory)][string]$Preset)
    $result = @{}
    foreach ($field in @('application/file_version', 'application/product_version')) {
        $value = Get-TrackstormExportPresetField -Preset $Preset -Field $field
        if ($value -cnotmatch '\A0\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.0\z' -or [int]$Matches[1] -gt 65534 -or [int]$Matches[2] -gt 65534) {
            throw "Expected $field to use canonical Windows numeric MAJOR.RELEASE.PR.0; actual '$value'."
        }
        $result[$field] = $value
    }
    return $result
}

function Assert-TrackstormExportPresetVersion {
    param([Parameter(Mandatory)][string]$Preset, [Parameter(Mandatory)][string]$CanonicalVersion)
    $null = Get-TrackstormVersion "<Project><PropertyGroup><TrackstormVersion>$CanonicalVersion</TrackstormVersion></PropertyGroup></Project>"
    $expected = "$CanonicalVersion.0"
    $versions = Get-TrackstormExportPresetVersions $Preset
    foreach ($field in @('application/file_version', 'application/product_version')) {
        if ($versions[$field] -cne $expected) {
            throw "Tracked export preset drift: expected $field '$expected'; actual '$($versions[$field])'."
        }
    }
    foreach ($identity in @(@('application/company_name', 'Thantrick'), @('application/product_name', 'Trackstorm'))) {
        $value = Get-TrackstormExportPresetField -Preset $Preset -Field $identity[0]
        if ($value -cne $identity[1]) { throw "Expected fixed export identity $($identity[0]) '$($identity[1])'; actual '$value'." }
    }
    Write-Host "Trackstorm export preset versions: expected '$expected'; actual file/product '$expected'."
}

function Set-TrackstormExportPresetVersion {
    param([Parameter(Mandatory)][string]$Preset, [Parameter(Mandatory)][string]$CanonicalVersion)
    $null = Get-TrackstormVersion "<Project><PropertyGroup><TrackstormVersion>$CanonicalVersion</TrackstormVersion></PropertyGroup></Project>"
    # Require one quoted field of each kind before replacing so missing or ambiguous presets fail closed.
    foreach ($field in @('application/file_version', 'application/product_version')) {
        $null = Get-TrackstormExportPresetField -Preset $Preset -Field $field
        $pattern = '(?m)^' + [regex]::Escape($field) + '="[^"\r\n]*"(?=\r?$)'
        $Preset = [regex]::Replace($Preset, $pattern, "$field=`"$CanonicalVersion.0`"")
    }
    Assert-TrackstormExportPresetVersion -Preset $Preset -CanonicalVersion $CanonicalVersion
    return $Preset
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
    $baselineCorrection = $changed -and $BaseVersion -ceq $Actual
    if ($migration -or $releaseChange -or $baselineCorrection) {
        if (-not $changed -or -not $TransitionJson) { throw 'Release/migration transition requires a new explicit Story authorization.' }
        $transition = ConvertFrom-Json -InputObject $TransitionJson -AsHashtable
        if (($transition.Keys | Sort-Object) -join ',' -cne 'from,kind,story,to') { throw 'Transition authorization requires exactly from, kind, story and to.' }
        if ($transition.story -cnotmatch '\ATS-[1-9][0-9]*\z' -or $StoryBranch -notmatch ('(?:^|/)' + [regex]::Escape($transition.story) + '(?:-|$)')) {
            throw 'Transition Story must match the assigned branch Jira key.'
        }
        if ($transition.from -cne $BaseVersion -or $transition.to -cne $Actual) { throw "Transition authorization is stale or mismatched: expected from '$BaseVersion' to '$Actual'." }
        if ($baselineCorrection) {
            $baseAuthorization = if ($BaseTransitionJson) { ConvertFrom-Json -InputObject $BaseTransitionJson -AsHashtable } else { @{} }
            if ($baseAuthorization.story -ceq 'TS-94') { throw 'TS-94 baseline correction authorization has already been consumed by main.' }
            if ($transition.kind -cne 'baseline-correction' -or $transition.story -cne 'TS-94' -or $BaseVersion -cne '0.1.0') {
                throw 'Only TS-94 authorizes the 0.1.0 baseline correction.'
            }
            $expected = '0.1.0'
        } elseif ($migration) {
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
