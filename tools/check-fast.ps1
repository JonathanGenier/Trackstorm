param(
    [ValidateSet("Auto", "Core", "Client", "Transport", "Docs")]
    [string]$Area = "Auto"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Invoke-Check {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    Write-Host "`n>> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Get-ChangedPaths {
    $base = "origin/main"
    git rev-parse --verify $base *> $null
    if ($LASTEXITCODE -ne 0) {
        $base = "main"
        git rev-parse --verify $base *> $null
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Cannot resolve origin/main or main. Fetch main or pass -Area explicitly."
    }

    $paths = @(git diff --name-only "$base...HEAD")
    if ($LASTEXITCODE -ne 0) {
        throw "Cannot determine changed paths against $base."
    }

    return $paths
}

if ($Area -eq "Auto") {
    $paths = @(Get-ChangedPaths)

    if ($paths.Count -eq 0) {
        Write-Host "No committed changes against main. Nothing to check."
        exit 0
    }

    $hasCore = $paths | Where-Object { $_ -like "code/Core/*" -or $_ -like "code/Tests/*" }
    $hasTransport = $paths | Where-Object {
        $_ -like "code/TransportTests/*" -or
        $_ -like "code/Client/Networking/*" -or
        $_ -like "code/Client/Online/*"
    }
    $hasClient = $paths | Where-Object {
        $_ -like "code/Client/*" -or
        $_ -like "scenes/*" -or
        $_ -like "assets/*" -or
        $_ -eq "project.godot" -or
        $_ -eq "Trackstorm.Client.csproj"
    }
    $hasVersion = $paths | Where-Object {
        $_ -eq "Directory.Build.props" -or
        $_ -eq "export_presets.cfg" -or
        $_ -like "tools/*version*.ps1" -or
        $_ -eq ".github/version-transition.json"
    }
    $hasMedia = $paths | Where-Object {
        $_ -like "assets/frontend/*" -or
        $_ -like "tools/*frontend-media*.ps1"
    }

    if ($hasVersion) {
        Invoke-Check "Version rule regression tests" { & "$root/tools/test-version.ps1" }
    }

    if ($hasMedia) {
        Invoke-Check "Frontend media verifier regression tests" { & "$root/tools/test-frontend-media.ps1" }
        Invoke-Check "Frontend media materialization and checksums" { & "$root/tools/check-frontend-media.ps1" }
    }

    if ($hasCore) {
        Invoke-Check "Core tests" {
            dotnet test "$root/code/Tests/Trackstorm.Core.Tests.csproj" -c Release
        }
    }

    if ($hasTransport) {
        Invoke-Check "Transport tests" {
            dotnet test "$root/code/TransportTests/Trackstorm.Transport.Tests.csproj" -c Release --filter 'TestCategory!=Native'
        }
    }

    if ($hasClient -or ($paths | Where-Object { $_ -like "*.cs" -or $_ -like "*.csproj" -or $_ -like "*.props" })) {
        Invoke-Check "Client Debug build" {
            dotnet build "$root/Trackstorm.Client.csproj" -c Debug -warnaserror
        }
    }

    if (-not ($hasCore -or $hasTransport -or $hasClient -or $hasVersion -or $hasMedia)) {
        Write-Host "Only documentation/workflow or unclassified non-production files changed; no .NET iteration check selected."
    }

    Write-Host "`nFast targeted checks passed. Run ./check.ps1 before final Story handoff."
    exit 0
}

switch ($Area) {
    "Core" {
        Invoke-Check "Core tests" {
            dotnet test "$root/code/Tests/Trackstorm.Core.Tests.csproj" -c Release
        }
    }
    "Client" {
        Invoke-Check "Client Debug build" {
            dotnet build "$root/Trackstorm.Client.csproj" -c Debug -warnaserror
        }
    }
    "Transport" {
        Invoke-Check "Transport tests" {
            dotnet test "$root/code/TransportTests/Trackstorm.Transport.Tests.csproj" -c Release --filter 'TestCategory!=Native'
        }
    }
    "Docs" {
        Write-Host "Docs-only iteration selected; no build/test command required."
    }
}

Write-Host "`nFast targeted checks passed. Run ./check.ps1 before final Story handoff."
