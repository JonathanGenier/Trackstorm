param (
    [Parameter(Mandatory)] [string]$GodotPath,
    [switch]$NoBuild,
    [ValidateRange(3, 50)] [int]$Cycles = 50
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $NoBuild) {
        dotnet build Trackstorm.Client.csproj -c Debug -m:1 -nr:false -warnaserror
        if ($LASTEXITCODE -ne 0) { throw 'Soak runtime build failed.' }
        dotnet build code/TransportTests/Trackstorm.Transport.Tests.csproj -c Release -m:1 -nr:false -warnaserror
        if ($LASTEXITCODE -ne 0) { throw 'Soak test build failed.' }
    }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    # Select this explicit test by its exact name; ordinary native CI excludes the ten-minute run.
    dotnet test code/TransportTests/Trackstorm.Transport.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName=Trackstorm.Transport.Tests.NativeVehicleReplicationTests.TenMinuteEightPlayerImpairmentSoak' --logger 'console;verbosity=normal'
    $movementFailed = $LASTEXITCODE -ne 0
    ./check-post-match.ps1 -GodotPath $GodotPath -NoBuild -Impaired -Cycles $Cycles
    if ($movementFailed) { throw 'Eight-player impairment soak failed; independent native rematch checks completed.' }
    Write-Host "Network soak passed: $($clock.Elapsed.TotalSeconds.ToString('F2')) seconds; ten-minute UDP run and $Cycles native Finished/rematch cycles. Separate-PC EOS and gameplay voting are not covered."
}
finally { Pop-Location }
