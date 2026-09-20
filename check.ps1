$ErrorActionPreference = "Stop"

& "$PSScriptRoot/tools/test-version.ps1"

function Invoke-Check {
    param (
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    Write-Host "`n>> $Name"
    & $Action

    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Invoke-Check "Frontend media verifier regression tests" {
    & "$PSScriptRoot/tools/test-frontend-media.ps1"
}

Invoke-Check "Frontend media materialization and checksums" {
    & "$PSScriptRoot/tools/check-frontend-media.ps1"
}

Invoke-Check "Restore" {
    dotnet restore Trackstorm.sln
}

Invoke-Check "Debug build and analyzers" {
    dotnet build Trackstorm.Client.csproj -c Debug --no-restore -warnaserror
}

Invoke-Check "Release build and analyzers" {
    dotnet build Trackstorm.sln -c Release --no-restore -warnaserror
}

Invoke-Check "Core tests" {
    dotnet test code/Tests/Trackstorm.Core.Tests.csproj -c Release --no-build
}

Invoke-Check "Transport conversion tests" {
    dotnet test code/TransportTests/Trackstorm.Transport.Tests.csproj -c Release --no-build --filter 'TestCategory!=Native'
}

Write-Host "`nAll checks passed."
