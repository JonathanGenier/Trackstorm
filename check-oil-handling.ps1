param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Oil handling build failed.' }
}
$output = Join-Path $PSScriptRoot ('.godot/oil-handling/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = @('--path', ('"' + $PSScriptRoot + '"'), '--fixed-fps', '60', 'res://scenes/verification/handling_playtest.tscn', '--', '--oil-playtest', '--handling-flat', ('"--handling-output=' + $output + '"'))
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'stdout.log') -RedirectStandardError (Join-Path $output 'stderr.log')
$results = [System.Collections.Generic.List[object]]::new()
function Send-Command([string]$Json) {
    $pending = Join-Path $output 'input.pending.json'
    [System.IO.File]::WriteAllText($pending, $Json)
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    while ($true) {
        try {
            [System.IO.File]::Move($pending, (Join-Path $output 'input.json'), $true)
            return
        }
        catch [System.IO.IOException], [System.UnauthorizedAccessException] {
            if ([DateTime]::UtcNow -ge $deadline) { throw }
            Start-Sleep -Milliseconds 20
        }
    }
}
function Command-Completed([string]$Path, [string]$Json) {
    try { return (Test-Path -LiteralPath $Path) -and [System.IO.File]::ReadAllText($Path) -eq $Json }
    catch [System.IO.IOException] { return $false }
}
function Invoke-Trial([string]$Name, [hashtable]$Command, [int]$ExpectedMask = -1) {
    $Command.trial = $Name
    $json = $Command | ConvertTo-Json -Compress -Depth 10
    Send-Command $json
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $completed = Join-Path $output 'completed-command.json'
    do {
        if ($process.HasExited) { throw "Godot exited during $Name. See $output" }
        if ([DateTime]::UtcNow -gt $deadline) { throw "Oil handling trial timed out: $Name" }
        Start-Sleep -Milliseconds 100
    } until (Command-Completed $completed $json)
    $trace = @(Get-Content -LiteralPath (Join-Path $output 'trace.json') -Raw | ConvertFrom-Json)
    Copy-Item -LiteralPath (Join-Path $output 'trace.json') -Destination (Join-Path $output "$Name.json")
    if ($Visual) { Copy-Item -LiteralPath (Join-Path $output 'view.png') -Destination (Join-Path $output "$Name.png") }
    $mask = 0
    foreach ($sample in $trace) {
        $mask = $mask -bor $sample.oilContact
        if ([double]::IsNaN($sample.speed) -or $sample.speed -gt 100 -or [Math]::Abs($sample.yaw) -gt 8.01 -or $sample.up -lt 0.5) { throw "Unstable Oil handling in $Name" }
    }
    if ($ExpectedMask -ge 0 -and $mask -ne $ExpectedMask) { throw "$Name contact mask $mask differs from expected $ExpectedMask" }
    $last = $trace[-1]
    $row = [ordered]@{ trial = $Name; frames = $trace.Count; contacted = $mask; speed = $last.speed; yaw = $last.yaw; steering = $last.steering; remaining = $last.oilWheels; peakLateralSpeed = ($trace | ForEach-Object { [Math]::Abs($_.lateral) } | Measure-Object -Maximum).Maximum }
    $results.Add($row)
    Write-Host ($row | ConvertTo-Json -Compress)
    return ,$trace
}
try {
    # Real wheel rays on a flat native collider; circles isolate tires without synthetic hit callbacks.
    $track = 1.19 * (2.601105 / 1.8954) / 2
    $axle = 2.601105 / 2
    $cases = @(
        @('front-left', @(-$track, 0, -$axle, 0.25), 1), @('front-right', @($track, 0, -$axle, 0.25), 2),
        @('rear-left', @(-$track, 0, $axle, 0.25), 4), @('rear-right', @($track, 0, $axle, 0.25), 8),
        # Keep the intended tires inside the smallest organic edge, with the opposite axle/side outside.
        @('front-only', @(0, 0, -$axle, 1.1), 3), @('rear-only', @(0, 0, $axle, 1.1), 12),
        @('left-only', @(-$track, 0, 0, 1.8), 5), @('right-only', @($track, 0, 0, 1.8), 10),
        @('all-wheels', @(0, 0, 0, 3), 15))
    foreach ($case in $cases) {
        $null = Invoke-Trial $case[0] @{ frames = 45; steer = 0; throttle = 0; brake = 0; spawn = @(0, 1.145, 0); yaw = 0; speed = 0; surface = 'Asphalt'; grade = 0; oilPatch = $case[1] } $case[2]
    }
    foreach ($speed in @(8, 20, 40)) {
        $null = Invoke-Trial "crossing-$speed" @{ frames = 150; steer = 0; throttle = 1; brake = 0; spawn = @(0, 1.145, 6); yaw = 0; speed = $speed; oilPatch = @(0, 0, 0, 3) } 15
    }
    foreach ($wet in @($false, $true)) {
        $name = if ($wet) { 'wet-corner' } else { 'dry-corner' }
        $patch = if ($wet) { @(0, 0, 0, 3) } else { $null }
        $null = Invoke-Trial $name @{ frames = 120; steer = 0.65; throttle = 1; brake = 0; spawn = @(0, 1.145, 0); yaw = 0; speed = 20; oilPatch = $patch }
        if ($wet) {
            $counter = Invoke-Trial 'countersteering' @{ frames = 120; steer = -0.65; throttle = 0.7; brake = 0 }
            if ($counter[-1].steering -ge 0) { throw 'Oil suppressed countersteering.' }
        }
    }
    $braking = Invoke-Trial 'wet-braking' @{ frames = 120; steer = 0; throttle = 0; brake = 1; spawn = @(0, 1.145, 0); yaw = 0; speed = 20; oilPatch = @(0, 0, 0, 3) }
    if ($braking[-1].speed -ge 20) { throw 'Oil suppressed braking.' }
    $null = Invoke-Trial 'wet-handbrake' @{ frames = 150; steer = 0.65; throttle = 0.7; brake = 0; handbrake = $true; spawn = @(0, 1.145, 0); yaw = 0; speed = 20; oilPatch = @(0, 0, 0, 3) }
    $jump = Invoke-Trial 'jump-and-landing' @{ frames = 240; steer = 0; throttle = 0.4; brake = 0; spawn = @(0, 2, 0); yaw = 0; speed = 8; vertical = 7; oilPatch = @(0, 0, -12, 3) }
    if (($jump | Where-Object { $_.grounded }).Count -eq 0 -or ($jump | Where-Object { -not $_.grounded }).Count -eq 0) { throw 'Jump fixture did not exercise both flight and landing.' }
    $recovery = Invoke-Trial 'continuous-overlap-recovery' @{ frames = 500; steer = 0; throttle = 0; brake = 0; spawn = @(0, 1.145, 0); yaw = 0; speed = 0; oilPatch = @(0, 0, 0, 3) } 15
    if ($recovery[-1].oilTicks -ne 0) { throw 'Continuous overlap refreshed contamination.' }
    $null = Invoke-Trial 'banked-contact' @{ frames = 90; steer = 0.3; throttle = 0.5; brake = 0; spawn = @(0, 1.145, 0); groundSpawn = $true; yaw = 0; speed = 5; grade = 8; oilPatch = @(0, 0, 0, 3, 0, [Math]::Cos(8 * [Math]::PI / 180), [Math]::Sin(8 * [Math]::PI / 180)) } 15
    $results | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'summary.json')
    Write-Host "Oil handling passed: $($results.Count) native trials. Evidence: $output"
}
finally {
    if (-not $process.HasExited) {
        Send-Command '{"quit":true}'
        if (-not $process.WaitForExit(3000)) { Stop-Process -Id $process.Id }
    }
}
if ((Get-Content -LiteralPath (Join-Path $output 'stderr.log') -Raw) -match 'ERROR:|WARNING:|Exception') { throw "Native runtime errors: $output" }
