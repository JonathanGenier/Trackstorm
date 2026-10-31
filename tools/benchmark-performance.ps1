param(
    [Parameter(Mandatory)][string]$GodotPath,
    [ValidateRange(60, 36000)][int]$Ticks = 1200,
    [ValidateRange(1, 10)][int]$Runs = 3,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label = 'sample',
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(360, 4320)][int]$Height = 720,
    [switch]$Headless,
    [switch]$NoBuild,
    [switch]$Exported
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$executable = (Get-Command $GodotPath -CommandType Application -ErrorAction Stop).Source
if (-not $NoBuild -and -not $Exported) {
    dotnet build (Join-Path $root 'Trackstorm.Client.csproj') -c Debug -warnaserror -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
}
$output = Join-Path $root ('.godot/performance/' + $Label + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$head = git -C $root rev-parse HEAD
$status = git -C $root status --porcelain=v1
$diff = git -C $root diff HEAD -- code scenes tools/benchmark-performance.ps1
[pscustomobject]@{ Head = $head; Status = @($status); Label = $Label; Ticks = $Ticks; Runs = $Runs;
    Executable = $executable; Exported = [bool]$Exported; Headless = [bool]$Headless;
    PowerMode = @(powercfg /getactivescheme); StartedUtc = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'run.json')
$diff | Set-Content (Join-Path $output 'source.diff')
for ($run = 1; $run -le $Runs; $run++) {
    $directory = Join-Path $output "run-$run"
    New-Item -ItemType Directory -Path $directory | Out-Null
    # ProcessStartInfo.ArgumentList preserves spaces without shell interpolation.
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $arguments = @('--resolution', "${Width}x${Height}")
    if (-not $Exported) { $arguments += @('--path', $root, 'res://scenes/verification/performance_benchmark.tscn') }
    $arguments += @('--', '--performance-benchmark', "--benchmark-ticks=$Ticks", "--benchmark-output=$directory")
    if ($Headless) { $arguments = @('--headless') + $arguments }
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        $timeout = [int]([Math]::Max(300, $Ticks / 60 * 20 + 180) * 1000)
        if (-not $process.WaitForExit($timeout)) { $process.Kill($true); throw "Benchmark timed out: $directory" }
        $log = $stdout.GetAwaiter().GetResult(); $errors = $stderr.GetAwaiter().GetResult()
        $log | Set-Content (Join-Path $directory 'runtime.log'); $errors | Set-Content (Join-Path $directory 'error.log')
        if ($process.ExitCode -ne 0 -or "$log`n$errors" -match 'ERROR:|WARNING:' -or $log -notmatch 'Performance benchmark completed') {
            throw "Benchmark failed; inspect $directory"
        }
        Write-Host $log
    }
    finally { $process.Dispose() }
}
Write-Host "Benchmark evidence: $output"
