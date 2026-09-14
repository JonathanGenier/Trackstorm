param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Authenticate
)
$ErrorActionPreference = 'Stop'
for ($cycle = 1; $cycle -le 3; $cycle++) {
    $arguments = @('--headless', '--path', $PSScriptRoot, 'res://scenes/verification/eos_checks.tscn', '--quit-after', '30000', '--')
    if ($Authenticate) { $arguments += '--eos-authenticate' }
    $output = & $GodotPath @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match 'EOS integration passed:')) {
        throw "EOS process cycle $cycle failed."
    }
}
