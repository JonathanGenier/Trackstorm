param(
    [Parameter(Mandatory, Position = 0)]
    [string]$StoryKey,

    [string]$Title = "Story verification"
)

$ErrorActionPreference = "Stop"

if ($StoryKey -notmatch '^TS-(\d+)$') {
    throw "StoryKey must use the form TS-123."
}

$number = $Matches[1]
$root = Split-Path -Parent $PSScriptRoot
$verificationDir = Join-Path $root "docs/verification"
$reportName = "ts-$number.md"
$reportPath = Join-Path $verificationDir $reportName
$indexPath = Join-Path $verificationDir "README.md"

if (-not (Test-Path $reportPath)) {
    $template = @"
# TS-$number verification

## Implemented

- TODO: record materially implemented behavior or systems.

## Verification

- TODO: record each command/check actually run and its result.

## Runtime / manual / native evidence

- TODO: record directly observed runtime/playtest/native evidence, or state that none was applicable/run.

## Assumptions and limitations

- TODO: record assumptions and limitations.

## Unresolved risks

- TODO: record unresolved risks, or state none identified.

## Explicitly unverified

- TODO: record anything not verified. Never convert this placeholder into a claimed result without running/observing it.
"@
    Set-Content -Path $reportPath -Value $template -Encoding utf8
    Write-Host "Created $reportName"
}
else {
    Write-Host "$reportName already exists; leaving it unchanged."
}

$index = Get-Content -Path $indexPath -Raw
$link = "($reportName)"
if ($index -notmatch [regex]::Escape($link)) {
    $entry = "| $Title | [TS-$number verification evidence]($reportName) |"
    $tableHeader = "| Evidence | Reports |"
    $separator = "| --- | --- |"

    $headerIndex = $index.IndexOf($tableHeader, [System.StringComparison]::Ordinal)
    if ($headerIndex -lt 0) {
        throw "Could not find verification index table header in $indexPath."
    }

    $separatorIndex = $index.IndexOf($separator, $headerIndex, [System.StringComparison]::Ordinal)
    if ($separatorIndex -lt 0) {
        throw "Could not find verification index table separator in $indexPath."
    }

    $insertAt = $separatorIndex + $separator.Length
    $index = $index.Insert($insertAt, "`n$entry")
    Set-Content -Path $indexPath -Value $index -Encoding utf8
    Write-Host "Added TS-$number to docs/verification/README.md"
}
else {
    Write-Host "Verification index already links $reportName."
}
