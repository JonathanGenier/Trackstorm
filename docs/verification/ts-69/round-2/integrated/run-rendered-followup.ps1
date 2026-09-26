$ErrorActionPreference='Stop'
$godot='C:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$evidence='docs/verification/ts-69/round-2/integrated'
foreach($name in @('rendered-unobserved-3','rendered-unobserved-4')) {
    $log=Join-Path $evidence ($name+'.log')
    try { ./check-network-vehicles.ps1 -GodotPath $godot -NoBuild -Visual -Latency 30 *> $log; $result='PASS' } catch { $_ | Out-String | Add-Content $log; $result='FAIL' }
    $line=Get-Content $log | Where-Object {$_ -match '^Network vehicle verification artifacts: '} | Select-Object -Last 1
    if($line) { Copy-Item -LiteralPath $line.Substring('Network vehicle verification artifacts: '.Length) -Destination (Join-Path $evidence $name) -Recurse }
    "$name`: $result"
}
$log=Join-Path $evidence 'final-entry-rendered.log'
./check-match-start.ps1 -GodotPath $godot -NoBuild -Visual *> $log
$line=Get-Content $log | Where-Object {$_ -match '^Match start verification artifacts: '} | Select-Object -Last 1
if($line) { Copy-Item -LiteralPath $line.Substring('Match start verification artifacts: '.Length) -Destination (Join-Path $evidence 'final-entry-rendered') -Recurse }
'final-entry-rendered: PASS'
