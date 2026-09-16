param (
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Migration build failed.' }
}
$outputDirectory = Join-Path $PSScriptRoot ('.godot/migration-process-checks/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$reservations = @(0..2 | ForEach-Object { [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0)) })
$ports = ($reservations | ForEach-Object { $_.Client.LocalEndPoint.Port }) -join ','
$reservations | ForEach-Object { $_.Dispose() }
$processes = @()
function Wait-Evidence([string]$file) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while (-not (Test-Path -LiteralPath (Join-Path $outputDirectory $file))) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out waiting for $file; inspect $outputDirectory" }
        Start-Sleep -Milliseconds 100
    }
}
try {
    foreach ($role in 0..2) {
        $arguments = @('--headless', '--path', ('"' + $PSScriptRoot + '"'), 'res://scenes/verification/migration_process_checks.tscn', '--', "--migration-role=$role", "--migration-ports=$ports", ('"--migration-output=' + $outputDirectory + '"'))
        $processes += Start-Process -FilePath $GodotPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $outputDirectory "$role.log") -RedirectStandardError (Join-Path $outputDirectory "$role.err")
        Wait-Evidence "$role-started.json"
        if ($role -eq 1) { Wait-Evidence '1-joined.json' }
    }
    foreach ($role in 0..2) { Wait-Evidence "$role-ready.json" }
    Stop-Process -Id $processes[0].Id -Force
    Wait-Evidence '1-passed.json'
    Wait-Evidence '2-passed.json'
    foreach ($process in $processes[1..2]) {
        if (-not $process.WaitForExit(10000)) { throw 'Survivor did not exit cleanly.' }
        if ($process.ExitCode -ne 0) { throw 'Survivor failed.' }
    }
    foreach ($role in 0..2) {
        if ((Get-Content (Join-Path $outputDirectory "$role.err") -Raw) -match 'ERROR:|WARNING:|Exception') { throw 'Native migration emitted a diagnostic.' }
    }
    Write-Host "Separate-process migration passed: host process terminated; two independent native survivors resumed epoch 2. Evidence: $outputDirectory"
} finally {
    foreach ($process in $processes) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}
