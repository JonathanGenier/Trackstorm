# TS-323 executed commands

Commands ran from the repository root. `$godot` is the pinned Godot 4.7.2 .NET console executable recorded in [commands.json](commands.json). That manifest preserves exact absolute arguments, UTC intervals, exit codes, source SHA and timeout flags. This table does not include the first five reused pilot checks or turn the interrupted October 31 Twin Fang run into a result. A later successful focused run does not erase an earlier failed run.

| Check | Command | Exit | Seconds | Output |
| --- | --- | --- | --- | --- |
| settings | `./check-settings.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 6.23 | [stdout](settings.log), [stderr](settings.stderr.log) |
| hud | `./check-hud.ps1 -GodotPath $godot -NoBuild` | 0 | 34.64 | [stdout](hud.log), [stderr](hud.stderr.log) |
| input | `./check-input.ps1 -GodotPath $godot -NoBuild` | 0 | 23.39 | [stdout](input.log), [stderr](input.stderr.log) |
| vehicle | `./check-vehicle.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 104.56 | [stdout](vehicle.log), [stderr](vehicle.stderr.log) |
| integrated-driving | `./check-integrated-driving.ps1 -GodotPath $godot -NoBuild` | 0 | 86.71 | [stdout](integrated-driving.log), [stderr](integrated-driving.stderr.log) |
| camera | `./check-camera.ps1 -GodotPath $godot -NoBuild` | 0 | 6.29 | [stdout](camera.log), [stderr](camera.stderr.log) |
| aim | `./check-weapon-aim.ps1 -GodotPath $godot -NoBuild -Visual` | 1 | 111.66 | [stdout](aim.log), [stderr](aim.stderr.log) |
| aim-camera-settings | `./check-weapon-aim.ps1 -GodotPath $godot -NoBuild -CameraSettings` | 0 | 132.88 | [stdout](aim-camera-settings.log), [stderr](aim-camera-settings.stderr.log) |
| aim-magnetic | `./check-weapon-aim.ps1 -GodotPath $godot -NoBuild -Magnetic -Impaired` | 0 | 69.34 | [stdout](aim-magnetic.log), [stderr](aim-magnetic.stderr.log) |
| pickup-drive | `./check-pickup-drive.ps1 -GodotPath $godot -NoBuild -Impaired` | 0 | 316.38 | [stdout](pickup-drive.log), [stderr](pickup-drive.stderr.log) |
| carnival-smash | `./check-carnival-smash.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 92.13 | [stdout](carnival-smash.log), [stderr](carnival-smash.stderr.log) |
| items | `./check-items.ps1 -GodotPath $godot -NoBuild -Impaired` | 1 | 25.66 | [stdout](items.log), [stderr](items.stderr.log) |
| aim-freedom-repro | `./check-weapon-aim.ps1 -GodotPath $godot -NoBuild -Freedom -Visual` | 1 | 52.9 | [stdout](aim-freedom-repro.log), [stderr](aim-freedom-repro.stderr.log) |
| aim-driving | `./check-weapon-aim.ps1 -GodotPath $godot -NoBuild -Driving -Visual` | 0 | 45.3 | [stdout](aim-driving.log), [stderr](aim-driving.stderr.log) |
| aim-missile | `./check-weapon-aim.ps1 -GodotPath $godot -NoBuild -Missile -Impaired` | 0 | 100.78 | [stdout](aim-missile.log), [stderr](aim-missile.stderr.log) |
| twin-fang | `./check-twin-fang.ps1 -GodotPath $godot -NoBuild -Motion` | 0 | 53.4 | [stdout](twin-fang.log), [stderr](twin-fang.stderr.log) |
| machine-gun | `./check-machine-gun.ps1 -GodotPath $godot -NoBuild -Impaired` | 0 | 49.31 | [stdout](machine-gun.log), [stderr](machine-gun.stderr.log) |
| machine-gun-vfx | `./check-machine-gun-vfx.ps1 -GodotPath $godot -NoBuild -Motion` | 0 | 30 | [stdout](machine-gun-vfx.log), [stderr](machine-gun-vfx.stderr.log) |
| machine-gun-impact | `./check-machine-gun-impact.ps1 -GodotPath $godot -NoBuild` | 0 | 39.31 | [stdout](machine-gun-impact.log), [stderr](machine-gun-impact.stderr.log) |
| missile-presentation | `./check-missile-presentation.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 64.85 | [stdout](missile-presentation.log), [stderr](missile-presentation.stderr.log) |
| missile-terrain | `./check-missile-terrain.ps1 -GodotPath $godot -NoBuild` | 0 | 205.84 | [stdout](missile-terrain.log), [stderr](missile-terrain.stderr.log) |
| missile-explosion | `./check-missile-explosion.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 87.4 | [stdout](missile-explosion.log), [stderr](missile-explosion.stderr.log) |
| salvo-catapult | `./check-salvo-catapult.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 70.71 | [stdout](salvo-catapult.log), [stderr](salvo-catapult.stderr.log) |
| salvo-explosion | `./check-salvo-explosion.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 151.07 | [stdout](salvo-explosion.log), [stderr](salvo-explosion.stderr.log) |
| oil-jack | `./check-oil-jack.ps1 -GodotPath $godot -NoBuild -Visual -Impaired` | 0 | 221.69 | [stdout](oil-jack.log), [stderr](oil-jack.stderr.log) |
| oil-tires | `./check-oil-tires.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 35.07 | [stdout](oil-tires.log), [stderr](oil-tires.stderr.log) |
| oil-handling | `./check-oil-handling.ps1 -GodotPath $godot -NoBuild` | 0 | 6.85 | [stdout](oil-handling.log), [stderr](oil-handling.stderr.log) |
| mine | `./check-mine.ps1 -GodotPath $godot -NoBuild -Visual -Impaired` | 1 | 9.56 | [stdout](mine.log), [stderr](mine.stderr.log) |
| shield-presentation | `./check-shield-presentation.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 77.08 | [stdout](shield-presentation.log), [stderr](shield-presentation.stderr.log) |
| shield-world | `./check-world-wall.ps1 -GodotPath $godot -NoBuild -Impaired` | 0 | 126.69 | [stdout](shield-world.log), [stderr](shield-world.stderr.log) |
| shield-terrain | `./check-world-wall.ps1 -GodotPath $godot -NoBuild -TerrainStability -ProductionMap -Impaired -Profile` | 0 | 135.09 | [stdout](shield-terrain.log), [stderr](shield-terrain.stderr.log) |
| shield-heavy | `./check-world-wall.ps1 -GodotPath $godot -NoBuild -Heavy -Impaired` | 0 | 73.33 | [stdout](shield-heavy.log), [stderr](shield-heavy.stderr.log) |
| shield-interception | `./check-shield-interception.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 77.01 | [stdout](shield-interception.log), [stderr](shield-interception.stderr.log) |
| emp-presentation | `./check-emp-claw.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 103.48 | [stdout](emp-presentation.log), [stderr](emp-presentation.stderr.log) |
| emp-devtools | `./check-developer-options.ps1 -GodotPath $godot -NoBuild -EmpClawOnly` | 0 | 8.63 | [stdout](emp-devtools.log), [stderr](emp-devtools.stderr.log) |
| emp-flight | `./check-emp-claw-flight.ps1 -GodotPath $godot -NoBuild` | 0 | 80.16 | [stdout](emp-flight.log), [stderr](emp-flight.stderr.log) |
| emp-attachments | `./check-emp-claw-attachments.ps1 -GodotPath $godot -NoBuild` | 0 | 99.18 | [stdout](emp-attachments.log), [stderr](emp-attachments.stderr.log) |
| emp-counterplay | `./check-emp-claw-counterplay.ps1 -GodotPath $godot -NoBuild -Visual` | 1 | 88.81 | [stdout](emp-counterplay.log), [stderr](emp-counterplay.stderr.log) |
| wrench-arm | `./check-wrench-arm.ps1 -GodotPath $godot -NoBuild -Visual -Impaired` | 0 | 107.4 | [stdout](wrench-arm.log), [stderr](wrench-arm.stderr.log) |
| boost-exhaust | `./check-boost-exhaust.ps1 -GodotPath $godot -NoBuild` | 0 | 41.75 | [stdout](boost-exhaust.log), [stderr](boost-exhaust.stderr.log) |
| boost-camera | `./check-boost-camera.ps1 -GodotPath $godot -NoBuild` | 0 | 15.65 | [stdout](boost-camera.log), [stderr](boost-camera.stderr.log) |
| audio | `./check-audio.ps1 -GodotPath $godot -NoBuild` | 0 | 13.04 | [stdout](audio.log), [stderr](audio.stderr.log) |
| post-match | `./check-post-match.ps1 -GodotPath $godot -NoBuild -Impaired -Cycles 3` | 0 | 79.74 | [stdout](post-match.log), [stderr](post-match.stderr.log) |
| reconnect-shield | `./check-reconnect.ps1 -GodotPath $godot -NoBuild -Shield` | 0 | 150.97 | [stdout](reconnect-shield.log), [stderr](reconnect-shield.stderr.log) |
| reconnect-oil | `./check-reconnect.ps1 -GodotPath $godot -NoBuild -OilFiring` | 0 | 81.11 | [stdout](reconnect-oil.log), [stderr](reconnect-oil.stderr.log) |
| reconnect-emp | `./check-reconnect.ps1 -GodotPath $godot -NoBuild -EmpClaw` | 0 | 53.36 | [stdout](reconnect-emp.log), [stderr](reconnect-emp.stderr.log) |
| network-vehicles-four | `./check-network-vehicles.ps1 -GodotPath $godot -NoBuild -Players 4 -Latency 30 -Jitter 5 -Loss 2` | 0 | 36.79 | [stdout](network-vehicles-four.log), [stderr](network-vehicles-four.stderr.log) |
| performance | `./tools/benchmark-performance.ps1 -GodotPath $godot -NoBuild -Ticks 1200 -Runs 3 -Label ts323-full` | 0 | 829.55 | [stdout](performance.log), [stderr](performance.stderr.log) |
| network-soak | `./check-network-soak.ps1 -GodotPath $godot -Cycles 3` | 0 | 736.13 | [stdout](network-soak.log), [stderr](network-soak.stderr.log) |
| salvo-native | `./check-salvo.ps1 -GodotPath $godot -NoBuild -Visual -Impaired` | 0 | 408.25 | [stdout](salvo-native.log), [stderr](salvo-native.stderr.log) |
| items-unimpaired | `./check-items.ps1 -GodotPath $godot -NoBuild` | 1 | 17.52 | [stdout](items-unimpaired.log), [stderr](items-unimpaired.stderr.log) |
| emp-counterplay-repro | `./check-emp-claw-counterplay.ps1 -GodotPath $godot -NoBuild -Visual` | 0 | 88.02 | [stdout](emp-counterplay-repro.log), [stderr](emp-counterplay-repro.stderr.log) |
| check-full | `./check.ps1` | 0 | 101 | [stdout](check-full.log), [stderr](check-full.stderr.log) |
| version-full | `./tools/check-version.ps1` | 1 | 0.83 | [stdout](version-full.log), [stderr](version-full.stderr.log) |
