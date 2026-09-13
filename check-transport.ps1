param (
    [Parameter(Mandatory)]
    [string]$GodotPath
)

$ErrorActionPreference = "Stop"
dotnet test code/TransportTests/Trackstorm.Transport.Tests.csproj -c Debug --logger 'console;verbosity=normal'
if ($LASTEXITCODE -ne 0) { throw "Native transport tests failed." }

$output = & $GodotPath --headless --path $PSScriptRoot res://scenes/verification/transport_checks.tscn --quit-after 20000 2>&1
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'Transport Godot integration passed:')) {
    throw "Godot transport integration failed or reported runtime diagnostics."
}
