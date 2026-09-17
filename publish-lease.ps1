$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    dotnet publish code/LeaseService/Trackstorm.LeaseService.csproj -c Release -o Releases/LeaseService --self-contained false -warnaserror
    if ($LASTEXITCODE -ne 0) { throw "Authority lease service publish failed." }
    Write-Host "Prepared Releases/LeaseService. Configure hosting with docs/authority-lease-service.md."
}
finally {
    Pop-Location
}
