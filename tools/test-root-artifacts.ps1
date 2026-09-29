$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$rootPrefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$check = Join-Path $PSScriptRoot "check-root-artifacts.ps1"
$fixture = [System.IO.Path]::GetFullPath((Join-Path $root "ts260-root-artifact-fixture"))
$fixtureFile = Join-Path $fixture "payload.txt"

if (-not $fixture.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Fixture path escaped the repository root."
}
if (Test-Path -LiteralPath $fixture) {
    throw "Fixture path already exists: '$fixture'."
}

& $check

try {
    [System.IO.Directory]::CreateDirectory($fixture) | Out-Null
    [System.IO.File]::WriteAllText($fixtureFile, "TS-260 root artifact guard fixture")

    $failedAsExpected = $false
    try {
        & $check *> $null
    }
    catch {
        $failedAsExpected = $_.Exception.Message -like "*Repository-root artifact check failed*"
    }

    if (-not $failedAsExpected) {
        throw "Root artifact check did not reject the unexpected fixture directory."
    }
    if (-not (Test-Path -LiteralPath $fixtureFile -PathType Leaf)) {
        throw "Root artifact check deleted or altered the forensic fixture."
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureFile -PathType Leaf) {
        [System.IO.File]::Delete($fixtureFile)
    }
    if (Test-Path -LiteralPath $fixture -PathType Container) {
        [System.IO.Directory]::Delete($fixture, $false)
    }
}

& $check
Write-Host "Root artifact guard regression checks passed."
