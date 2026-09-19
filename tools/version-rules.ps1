function Get-TrackstormVersion {
    param([Parameter(Mandatory)][string]$Xml, [switch]$AllowUninitialized)
    [xml]$document = $Xml
    $nodes = @($document.SelectNodes('/Project/PropertyGroup/TrackstormVersion'))
    if ($nodes.Count -eq 0 -and $AllowUninitialized) { return $null }
    if ($nodes.Count -ne 1) { throw 'Expected exactly one TrackstormVersion in Directory.Build.props.' }
    $value = $nodes[0].InnerText
    if ($value -cnotmatch '\A0\.0\.1\.(0|[1-9][0-9]{0,4})\z' -or [int]$Matches[1] -gt 65534) {
        throw "Expected canonical 0.0.1.N (N = 0..65534); actual '$value'."
    }
    return $value
}

function Assert-TrackstormVersionStep {
    param([AllowNull()][string]$BaseVersion, [Parameter(Mandatory)][string]$Actual)
    # Only establishment against a base with no canonical property starts at revision zero.
    $expected = '0.0.1.0'
    if ($BaseVersion) {
        $null = Get-TrackstormVersion "<Project><PropertyGroup><TrackstormVersion>$BaseVersion</TrackstormVersion></PropertyGroup></Project>"
        $revision = [int]($BaseVersion.Split('.')[-1]) + 1
        if ($revision -gt 65534) { throw "Version range exhausted: base '$BaseVersion'; expected a reviewed release change; actual '$Actual'." }
        $expected = "0.0.1.$revision"
    }
    if ($Actual -cne $expected) {
        throw "Trackstorm Story version: expected '$expected'; actual '$Actual'; current main '$BaseVersion'. Increment only the fourth component by exactly +1 after synchronizing main."
    }
    Write-Host "Trackstorm Story version: expected '$expected'; actual '$Actual'."
}
