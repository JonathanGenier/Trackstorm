$ErrorActionPreference = "Stop"

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("trackstorm-workflow-tools-" + [guid]::NewGuid().ToString("N"))

try {
    $toolsDir = Join-Path $tempRoot "tools"
    $verificationDir = Join-Path $tempRoot "docs/verification"
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
    New-Item -ItemType Directory -Path $verificationDir -Force | Out-Null

    Copy-Item "$PSScriptRoot/new-verification-report.ps1" (Join-Path $toolsDir "new-verification-report.ps1")
    Copy-Item "$PSScriptRoot/check-fast.ps1" (Join-Path $toolsDir "check-fast.ps1")

    @"
# Historical verification evidence

| Evidence | Reports |
| --- | --- |
| Existing | [Existing](existing.md) |
"@ | Set-Content -Path (Join-Path $verificationDir "README.md") -Encoding utf8

    & (Join-Path $toolsDir "new-verification-report.ps1") TS-999 -Title "Workflow helper regression"

    $reportPath = Join-Path $verificationDir "ts-999.md"
    $indexPath = Join-Path $verificationDir "README.md"

    Assert-True (Test-Path $reportPath) "Verification report scaffolder did not create ts-999.md."

    $report = Get-Content $reportPath -Raw
    Assert-True ($report -match "TODO: record each command/check actually run") "Verification report scaffold lost its evidence placeholder."
    Assert-True ($report -match "Never convert this placeholder into a claimed result") "Verification report scaffold lost its anti-fabrication reminder."

    $index = Get-Content $indexPath -Raw
    Assert-True ($index -match "\[TS-999 verification evidence\]\(ts-999\.md\)") "Verification index was not updated."

    & (Join-Path $toolsDir "new-verification-report.ps1") TS-999 -Title "Workflow helper regression"

    $index = Get-Content $indexPath -Raw
    $linkCount = ([regex]::Matches($index, "\(ts-999\.md\)")).Count
    Assert-True ($linkCount -eq 1) "Verification scaffolder is not idempotent; found $linkCount index links."

    & (Join-Path $toolsDir "check-fast.ps1") -Area Docs

    Write-Host "Workflow helper regression tests passed."
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
}
