param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [ValidateRange(2, 8)]
    [int]$Players = 2,
    [int]$Latency = 0,
    [int]$Jitter = 0,
    [float]$Loss = 0,
    [switch]$Visual,
    [switch]$PrototypeMap,
    [switch]$IsolateHost,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if ($IsolateHost -and (-not $IsWindows -or [Environment]::ProcessorCount -lt 4 -or [Environment]::ProcessorCount -gt 62)) {
    throw 'Host CPU isolation requires Windows with 4-62 logical processors.'
}
$clientAffinity = if ($IsolateHost) { (1L -shl [Environment]::ProcessorCount) - 2 } else { 0 }
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Network vehicle build failed.' }
}
$networkOutput = Join-Path $PSScriptRoot ('.godot/network-vehicle-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $networkOutput -Force | Out-Null
$reservation = [System.Net.Sockets.UdpClient]::new([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Loopback, 0))
$port = $reservation.Client.LocalEndPoint.Port
$reservation.Dispose()
$processes = @{}
$visualArguments = @()
$visualExitCode = 0
try {
    for ($index = 0; $index -lt $Players; $index++) {
        $role = if ($index -eq 0) { 'host' } else { 'client' }
        $duration = if ($index -eq 0) { 24 } else { 16 }
        $output = Join-Path $networkOutput "player-$index"
        if ($Visual -and $index -eq 1) {
            $visualArguments = @('--path', $PSScriptRoot, 'res://scenes/verification/network_vehicle_checks.tscn', '--', "--network-check-$role=127.0.0.1:$port", "--network-check-players=$Players", "--network-check-seconds=$duration", "--network-check-latency=$Latency", "--network-check-jitter=$Jitter", "--network-check-loss=$($Loss.ToString([System.Globalization.CultureInfo]::InvariantCulture))", "--network-check-output=$output")
            if ($PrototypeMap) { $visualArguments += '--network-check-prototype' }
            continue
        }
        $arguments = @('--path', "`"$PSScriptRoot`"", 'res://scenes/verification/network_vehicle_checks.tscn', '--', "--network-check-$role=127.0.0.1:$port", "--network-check-players=$Players", "--network-check-seconds=$duration", "--network-check-latency=$Latency", "--network-check-jitter=$Jitter", "--network-check-loss=$($Loss.ToString([System.Globalization.CultureInfo]::InvariantCulture))", "`"--network-check-output=$output`"")
        if (-not $Visual -or $index -ne 1) { $arguments = @('--headless') + $arguments }
        if ($PrototypeMap) { $arguments += '--network-check-prototype' }
        $processes[$index] = Start-Process -FilePath $GodotPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput "$output.log" -RedirectStandardError "$output.errors.log"
        if ($IsolateHost) {
            # Diagnostic scheduling control only: never changes simulation or acceptance thresholds.
            $processes[$index].ProcessorAffinity = [IntPtr]$(if ($index -eq 0) { 1 } else { $clientAffinity })
        }
        if ($index -eq 0) { Start-Sleep -Milliseconds 400 }
    }
    if ($Visual) {
        $visualOutput = Join-Path $networkOutput 'player-1'
        if ($IsolateHost) {
            $quoted = $visualArguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
            $processes[1] = Start-Process -FilePath $GodotPath -ArgumentList $quoted -WindowStyle Hidden -PassThru -RedirectStandardOutput "$visualOutput.log" -RedirectStandardError "$visualOutput.errors.log"
            $processes[1].ProcessorAffinity = [IntPtr]$clientAffinity
            if (-not $processes[1].WaitForExit(90000)) { throw 'Rendered diagnostic peer timed out.' }
            $visualExitCode = $processes[1].ExitCode
        }
        else {
            & $GodotPath @visualArguments 1> "$visualOutput.log" 2> "$visualOutput.errors.log"
            $visualExitCode = $LASTEXITCODE
        }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while (@($processes.Values | Where-Object { -not $_.HasExited }).Count -gt 0 -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    for ($index = 0; $index -lt $Players; $index++) {
        $output = Join-Path $networkOutput "player-$index"
        $log = Get-Content -LiteralPath "$output.log" -Raw
        $errors = Get-Content -LiteralPath "$output.errors.log" -Raw
        Write-Host $log
        if ($errors) { Write-Host $errors }
        $processFailed = if ($Visual -and $index -eq 1) { $visualExitCode -ne 0 } else { -not $processes[$index].HasExited -or $processes[$index].ExitCode -ne 0 }
        if ($processFailed -or $errors -match 'ERROR:|WARNING:' -or $log -notmatch 'Network vehicle integration passed.') {
            throw "Network vehicle instance $index failed. Artifacts: $networkOutput"
        }
    }
}
finally {
    foreach ($process in $processes.Values) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id }
        $process.Dispose()
    }
}
Write-Host "Network vehicle verification artifacts: $networkOutput"
