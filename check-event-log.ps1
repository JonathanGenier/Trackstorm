param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [string]$ReleaseExecutable,
    [switch]$Visual,
    [switch]$NoBuild,
    [string]$Resolution = '1280x720'
)

$ErrorActionPreference = 'Stop'
$eventLogOutput = Join-Path $PSScriptRoot ('.godot/event-log-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $eventLogOutput -Force | Out-Null
if (-not $NoBuild -and -not $ReleaseExecutable) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Event Log build failed.' }
}

$eventLogExecutable = if ($ReleaseExecutable) { (Resolve-Path -LiteralPath $ReleaseExecutable).Path } else { (Resolve-Path -LiteralPath $GodotPath).Path }
$eventLogEngineLog = Join-Path $eventLogOutput 'engine.log'
$eventLogArguments = @('--quit-after', '2400', '--resolution', $Resolution, '--log-file', $eventLogEngineLog)
if ($ReleaseExecutable) {
    $eventLogArguments += @('--', '--event-log-check', "--event-log-size=$Resolution")
} else {
    $eventLogArguments = @('--path', $PSScriptRoot, 'res://scenes/verification/event_log_checks.tscn') + $eventLogArguments + @('--', "--event-log-size=$Resolution")
}
if (-not $Visual) { $eventLogArguments = @('--headless') + $eventLogArguments }
if ($ReleaseExecutable) {
    # Console-free Windows exports return immediately from direct PowerShell invocation.
    $eventLogQuotedArguments = $eventLogArguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }
    $eventLogProcess = Start-Process -FilePath $eventLogExecutable -ArgumentList $eventLogQuotedArguments -WindowStyle Hidden -PassThru -Wait
    $eventLogExit = $eventLogProcess.ExitCode
    $eventLogResult = @()
} else {
    $eventLogResult = & $eventLogExecutable @eventLogArguments 2>&1
    $eventLogExit = $LASTEXITCODE
}
if (Test-Path -LiteralPath $eventLogEngineLog) {
    $eventLogResult = @($eventLogResult) + @(Get-Content -LiteralPath $eventLogEngineLog)
}
$eventLogResult | Set-Content (Join-Path $eventLogOutput 'runtime.log')
$eventLogResult | ForEach-Object { Write-Host $_ }
if ($eventLogExit -ne 0 -or ($eventLogResult -match 'ERROR:|WARNING:') -or -not ($eventLogResult -match 'Event Log integration passed:')) {
    throw 'Event Log native verification failed or did not finish.'
}
Write-Host "Event Log verification artifacts: $eventLogOutput"
