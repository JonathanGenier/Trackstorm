$ErrorActionPreference = "Stop"

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

Invoke-Check "Restore" {
    dotnet restore Trackstorm.sln
}

Invoke-Check "Formatting verification" {
    dotnet format Trackstorm.sln --verify-no-changes --no-restore
}

Invoke-Check "Debug build and analyzers" {
    dotnet build Trackstorm.sln -c Debug --no-restore -warnaserror
}

Invoke-Check "Debug Core tests" {
    dotnet test code/Tests/Trackstorm.Core.Tests.csproj -c Debug --no-build
}

Invoke-Check "Release build and analyzers" {
    dotnet build Trackstorm.sln -c Release --no-restore -warnaserror
}

Invoke-Check "Release Core tests" {
    dotnet test code/Tests/Trackstorm.Core.Tests.csproj -c Release --no-build
}

Write-Host "`nAll checks passed."
