param (
    [string]$GodotPath,
    [string]$ExportPath,
    [switch]$Visual,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path $PSScriptRoot ('.godot/statistic-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
if ($ExportPath) {
    $executable = $ExportPath
    $arguments = @('--quit-after', '2400', '--', '--statistics-check', "--statistics-output=$outputDirectory")
} else {
    if (-not $GodotPath) { throw 'Supply GodotPath or ExportPath.' }
    if (-not $NoBuild) {
        dotnet build Trackstorm.sln -c Debug -warnaserror
        if ($LASTEXITCODE -ne 0) { throw 'Statistic verification build failed.' }
    }
    $executable = $GodotPath
    $arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/statistic_checks.tscn', '--quit-after', '2400', '--', "--statistics-output=$outputDirectory")
}
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$runtimeLog = Join-Path $outputDirectory 'runtime.log'
$arguments = @('--log-file', $runtimeLog) + $arguments
$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = $executable
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
$process = [System.Diagnostics.Process]::Start($start)
try {
    if (-not $process.WaitForExit(60000)) {
        $process.Kill()
        throw 'Statistic integration timed out.'
    }
    $exitCode = $process.ExitCode
} finally {
    $process.Dispose()
}
# Windows GUI exports need an explicit log file; they need not have a console stream.
$log = if (Test-Path -LiteralPath $runtimeLog) { Get-Content -LiteralPath $runtimeLog } else { @('Statistic runtime log missing.') }
$log | Set-Content (Join-Path $outputDirectory 'run.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Statistic integration passed:')) {
    throw "Statistic integration failed. See $outputDirectory"
}
Write-Host "Statistic verification artifacts: $outputDirectory"
