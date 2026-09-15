param (
    [Parameter(Mandatory)][string]$Executable,
    [string]$Name = 'Trackstorm 0.0.0.5 remote check',
    [ValidateRange(2, 8)][int]$Players = 2,
    [switch]$HostPlayer,
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $PSScriptRoot }
$testExecutable = (Resolve-Path -LiteralPath $Executable).Path
$testOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$role = if ($HostPlayer) { 'host' } else { 'client' }
$resultPath = Join-Path $testOutput "eos-$role-result.json"
$logPath = Join-Path $testOutput "eos-$role.log"
$errorPath = Join-Path $testOutput "eos-$role-errors.log"
$arguments = @('--headless', '--', '--eos-multiplayer-check', "`"--eos-test-name=$Name`"", "--eos-test-players=$Players", "`"--eos-test-output=$resultPath`"")
if ($HostPlayer) { $arguments += '--eos-test-host' }
Write-Host "Running real EOS $role check. Waiting up to 15 minutes for $Players distinct devices in '$Name'."
$process = Start-Process -FilePath $testExecutable -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $testExecutable) -WindowStyle Hidden -PassThru -RedirectStandardOutput $logPath -RedirectStandardError $errorPath
try {
    $process.WaitForExit()
    Get-Content -LiteralPath $logPath
    if ($process.ExitCode -ne 0) { throw "EOS $role test failed. Send $resultPath and both log files for diagnosis." }
    Write-Host "EOS $role check passed. Result: $resultPath"
}
finally {
    $process.Dispose()
}
