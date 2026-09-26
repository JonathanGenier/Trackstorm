$ErrorActionPreference = 'Stop'
$godot = 'C:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$evidence = 'docs/verification/ts-69/round-2/integrated'
New-Item -ItemType Directory -Force $evidence | Out-Null
function Run-Check([string]$name, [scriptblock]$action) {
    $log = Join-Path $evidence ($name + '.log')
    try {
        $global:LASTEXITCODE = 0
        & $action *> $log
        if($LASTEXITCODE -ne 0) { throw "Exit code $LASTEXITCODE" }
        "$name`: PASS"
    } catch {
        $_ | Out-String | Add-Content -LiteralPath $log
        "$name`: FAIL"
    }
}
Run-Check 'final-check' { ./check.ps1 }
Run-Check 'final-transport' { ./check-transport.ps1 -GodotPath $godot }
Run-Check 'final-surfaces' { ./check-surfaces.ps1 -GodotPath $godot -NoBuild }
Run-Check 'final-water' { ./check-water.ps1 -GodotPath $godot -NoBuild }
Run-Check 'final-boundary' { ./check-boundary.ps1 -GodotPath $godot -NoBuild }
Run-Check 'final-oob-network' { ./check-death-respawn.ps1 -GodotPath $godot -NoBuild -Impaired -OutOfBounds }
Run-Check 'final-contact' { ./check-environment-collision-network.ps1 -GodotPath $godot -NoBuild }
Run-Check 'final-lobby' { ./check-lobby.ps1 -GodotPath $godot }
Run-Check 'final-entry' { ./check-match-start.ps1 -GodotPath $godot -NoBuild }
Run-Check 'final-items' { ./check-items.ps1 -GodotPath $godot -NoBuild -Impaired }
Run-Check 'final-oil' { ./check-oil.ps1 -GodotPath $godot -NoBuild -Impaired }
Run-Check 'final-nitro' { ./check-nitro.ps1 -GodotPath $godot -NoBuild -Impaired }
Run-Check 'final-pickups' { ./check-pickup-drive.ps1 -GodotPath $godot -NoBuild }
Run-Check 'final-spawns' { ./check-item-spawns.ps1 -GodotPath $godot -NoBuild -Impaired -Oval }
Run-Check 'final-death' { ./check-death-respawn.ps1 -GodotPath $godot -NoBuild -Impaired }
Run-Check 'final-match' { ./check-match.ps1 -GodotPath $godot -NoBuild -Impaired }
Run-Check 'final-reconnect' { ./check-reconnect.ps1 -GodotPath $godot -NoBuild }
Run-Check 'final-migration-two' { ./check-migration.ps1 -GodotPath $godot -NoBuild -Players 2 }
Run-Check 'final-migration-three' { ./check-migration.ps1 -GodotPath $godot -NoBuild -Players 3 }
Run-Check 'final-process-migration-two' { ./check-migration-processes.ps1 -GodotPath $godot -NoBuild -Players 2 }
Run-Check 'final-process-migration-three' { ./check-migration-processes.ps1 -GodotPath $godot -NoBuild -Players 3 }
Run-Check 'final-statistics' { ./check-statistics.ps1 -GodotPath $godot }
Run-Check 'final-menu' { ./check-menu.ps1 -GodotPath $godot }
Run-Check 'final-post-match' { ./check-post-match.ps1 -GodotPath $godot }
Run-Check 'final-eos' { ./check-eos.ps1 -GodotPath $godot }
Run-Check 'final-eos-p2p' { ./check-eos.ps1 -GodotPath $godot -P2p }
