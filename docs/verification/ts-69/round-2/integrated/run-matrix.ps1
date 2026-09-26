$ErrorActionPreference = 'Stop'
$godot = 'C:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$evidence = 'docs/verification/ts-69/round-2/integrated'
New-Item -ItemType Directory -Force $evidence | Out-Null
./check.ps1 *> (Join-Path $evidence 'final-check-after-cleanup.log')
'final-check-after-cleanup: PASS'
./check-transport.ps1 -GodotPath $godot *> (Join-Path $evidence 'final-transport-monotonic.log')
'final-transport-monotonic: PASS'
./check-surfaces.ps1 -GodotPath $godot -NoBuild *> (Join-Path $evidence 'final-surfaces-cleanup-1.log')
'final-surfaces-cleanup-1: PASS'
./check-surfaces.ps1 -GodotPath $godot -NoBuild *> (Join-Path $evidence 'final-surfaces-cleanup-2.log')
'final-surfaces-cleanup-2: PASS'
./check-gdunit.ps1 -GodotPath $godot *> (Join-Path $evidence 'final-gdunit.log')
'final-gdunit: PASS'
$cases = @(
    @{ Name='final-zero'; Arguments=@{} },
    @{ Name='final-60'; Arguments=@{Latency=30} },
    @{ Name='final-100'; Arguments=@{Latency=50;Jitter=10} },
    @{ Name='final-loss'; Arguments=@{Loss=2} },
    @{ Name='final-eight-1'; Arguments=@{Players=8;Latency=30;Jitter=10;Loss=2} },
    @{ Name='final-eight-2'; Arguments=@{Players=8;Latency=30;Jitter=10;Loss=2} },
    @{ Name='final-eight-3'; Arguments=@{Players=8;Latency=30;Jitter=10;Loss=2} },
    @{ Name='final-rendered-1'; Arguments=@{Latency=30;Visual=$true} },
    @{ Name='final-rendered-2'; Arguments=@{Latency=30;Visual=$true} },
    @{ Name='capture-overhead'; Arguments=@{Latency=30;Visual=$true;CaptureDuringDriving=$true} },
    @{ Name='final-overload-64'; Arguments=@{Latency=50;Jitter=10;CatchUpSteps=64} }
)
foreach($case in $cases) {
    $log = Join-Path $evidence ($case.Name + '.log')
    $arguments = $case.Arguments
    try {
        & ./check-network-vehicles.ps1 -GodotPath $godot -NoBuild @arguments *> $log
        $outcome = 'PASS'
    } catch {
        $_ | Out-String | Add-Content -LiteralPath $log
        $outcome = 'FAIL'
    }
    $artifactLine = Get-Content -LiteralPath $log | Where-Object {$_ -match '^Network vehicle verification artifacts: ' } | Select-Object -Last 1
    if($artifactLine) {
        $source = $artifactLine.Substring('Network vehicle verification artifacts: '.Length)
        Copy-Item -LiteralPath $source -Destination (Join-Path $evidence $case.Name) -Recurse
    }
    "$($case.Name): $outcome"
}
