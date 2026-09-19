param([string]$GodotPath, [string]$ExportPath)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/tools/version-rules.ps1"
$expected = Get-TrackstormVersion (Get-Content -Raw "$PSScriptRoot/Directory.Build.props")
$outputDirectory = Join-Path $PSScriptRoot ('.godot/build-version-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $outputDirectory | Out-Null
$logPath = Join-Path $outputDirectory 'runtime.log'
if ($ExportPath) {
    $executable = (Resolve-Path -LiteralPath $ExportPath).Path
    $resource = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
    if ($resource.FileVersion -ne "$expected.0" -or $resource.ProductVersion -ne "$expected.0") {
        throw "Export resource version mismatch: expected '$expected.0'; actual '$($resource.FileVersion)' / '$($resource.ProductVersion)'."
    }
    $arguments = @('--headless','--log-file',$logPath,'--','--version-check')
} else {
    if (-not $GodotPath) { throw 'Supply GodotPath or ExportPath.' }
    $executable = (Resolve-Path -LiteralPath $GodotPath).Path
    $arguments = @('--headless','--path',$PSScriptRoot,'--log-file',$logPath,'--','--version-check')
}
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $executable
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
try {
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Version runtime check timed out.' }
    $exitCode = $process.ExitCode
} finally { $process.Dispose() }
$log = Get-Content -Raw -LiteralPath $logPath
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or $log -notmatch [regex]::Escape("Trackstorm version: $expected;")) {
    throw "Version runtime verification failed: $logPath`n$log"
}
if ($ExportPath -and $log -notmatch [regex]::Escape("Godot metadata: $expected; exported: True")) {
    throw "Exported Godot version mismatch: $logPath"
}
Write-Host "Build version check passed: $expected ($logPath)"
