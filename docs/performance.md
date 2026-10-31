# Repeatable performance capture

Performance is measured against a named build and controlled workload. Earlier informal FPS observations are not a regression baseline.

## Human PC baseline

1. Record CPU, GPU/driver, RAM, Windows version, uptime, build SHA/version, executable path and Debug/editor/export mode. Record monitor refresh rate, renderer, resolution, window/fullscreen mode, graphics settings, VSync, frame cap, power mode, AC/battery state and any monitoring/recording tools. Save the effective Developer Options configuration.
2. Before reboot, run the scenarios below three times. Stop downloads, installers and unnecessary background apps first; record what remains. Keep the same power mode, driver, resolution, camera and settings. Do not change several factors at once.
3. Reboot manually, let startup activity settle, and repeat on the **same executable** with the same settings. Record temperatures, GPU utilization/clocks and CPU utilization if available. Reboot results isolate an environment change, not a code improvement.
4. Warm up the map for 30 seconds before each timed capture. Record every trial, rather than selecting the fastest. Use a 120-second capture for idle, driving and combat, followed by a 15-minute sustained run. Keep overlays/recording either enabled in all trials or disabled in all trials.
5. Idle: New Map, Clear Blue, one human host at the same grid spawn, chase camera facing the same direction, no menus. **Normal New Map hosting also creates four practice targets: five total vehicles.** Record both human and practice counts; keep them constant. The automated scene below separately isolates one car. Driving: follow the same two oval laps, then the same infield/tunnel route. Combat: use the same item weights, positions, target count and trigger durations; repeat Machine Gun, Missile/Salvo, shield deployment/contact, and crate pickups. Record collisions, deaths and respawns. For sustained play, repeat this cycle and record memory at 1, 5, 10 and 15 minutes and after returning to the menu.
6. Repeat combat with 2–4 human players on matching builds where available (6–8 total vehicles with New Map's four practice targets). Record host/client role, total vehicle count, connection type and impairment. Local practice cars and localhost peers do not establish remote network performance.

Use one row per trial: build SHA, machine, uptime/reboot condition, scenario, mode, resolution, settings, VSync/cap, duration, average FPS, median/p95/p99 frame ms, frames over 33.3/50 ms, CPU/GPU observations, memory start/end and notes. Preserve raw captures when available. A before/after comparison must hold every condition except the investigated change constant.

## Automated fixed workload

Run from the repository root:

```powershell
./tools/benchmark-performance.ps1 -GodotPath <Godot-.NET-exe> -Label baseline
# After the specific change, use the same command parameters:
./tools/benchmark-performance.ps1 -GodotPath <Godot-.NET-exe> -Label candidate
```

Defaults: three fresh processes, 1280×720, VSync disabled, uncapped, 60 Hz physics, 180 warmup ticks before each phase and 1,200 measured physics ticks (20 simulation seconds) per phase. `-Ticks 3600` gives 60 seconds per phase; `-Runs`, `-Width` and `-Height` are explicit controls. Short `-Ticks 300` runs are screening measurements, not long-session evidence. Do not compile, import assets or run another benchmark concurrently. Keep background applications consistent. The benchmark uses explicit forced offscreen rendering once per process frame so hidden/minimized windows still perform rendering; its throughput is **not presented/displayed FPS** and excludes display synchronization.

The opt-in verification scene runs these phases in a fixed order:

- Production map, materials, environment and shadows at a fixed camera.
- Same map/camera with all 27 pickup stacks.
- CPU-only accepted pickup publications with unchanged availability: 12 batches of 1,000 calls, first two discarded; publication construction is outside the timer.
- Synthetic 27-crate claim waves every 30 physics ticks, followed by 780 ticks for expiry and a cleanup sample. These are presentation publications, not actual inventory grants.
- Production host arena: one car idle, then full-throttle straight driving from the spawn (including its resulting bank/wall contact), then three cars and four cars. The third practice driver uses the application's native terrain-placement callback.
- Host Machine Gun use with a new trigger press for each magazine and held input, followed by continued four-car simulation. Actual published round counts must be nonzero.
- Add the pond-side practice target to reach the normal application's five-vehicle roster. These staged workloads also differ in vehicle positions and elapsed simulation time; they do not isolate roster size or benchmark a historical build.
- Arena teardown and memory/node counters.

The scene uses production Core authority, native collision/suspension queries, map and presentation. It uses fixture match startup, explicit item grants and local practice cars; it is not the full menu/lobby application or a human driving test. Longer tick settings change the resulting route and effects, so compare only identical tick counts. Physics is fixed-step; rendering, native simulation and resource caches can still introduce trial variation.

Compare actual vehicle and published-round counts as well as parameters. Identical commands can still produce different native combat outcomes; label such whole-scene comparisons inconclusive. Keep a focused CPU measurement separate from any claim of overall FPS improvement.

Evidence lives under ignored `.godot/performance/<label>-<timestamp>/`: invocation context, source diff, environment metadata, runtime/error logs, per-phase JSON statistics and rendered screenshots. The runner fails on runtime warnings/errors, missing completion, invalid workload or timeout. `-Headless` measures a different workload with no rendered throughput claim. `-NoBuild` requires the caller to have built the current Debug source.

## Editor, Debug and export

The ordinary runner launches the .NET editor binary as a standalone **Debug game**, without the editor UI/debugger attached. It does not measure an editor-embedded game session.

Build an export to an ignored directory using the existing preset, then run the same scene:

```powershell
# Create the output directory first. Use the pinned editor and installed matching templates.
& <Godot-.NET-exe> --headless --path . --export-release 'Windows Desktop' .godot/performance-export/Trackstorm.exe
./tools/benchmark-performance.ps1 -GodotPath .godot/performance-export/Trackstorm.exe -Exported -Label export
```

Keep export settings, source, resolution and scenario parameters identical. Record the exported executable and external Client assembly SHA-256 separately; the executable hash alone does not identify C# changes. Export runtime counters can be unavailable or zero; zero is not proof of no allocation. Test the normal exported application and human route separately.

`-Exported` invokes the opt-in `--performance-benchmark` application entry point. Stock Release templates reject command-line scene-path overrides. This switch exits normal startup into the verification scene; an ordinary launch keeps the production startup path.

When swapping source snapshots for A/B testing, force a rebuild (`dotnet build Trackstorm.Client.csproj -c Debug -t:Rebuild -warnaserror -m:1 -nr:false`) and record the resulting DLL hash. Restoring a file can preserve an old timestamp and make an incremental build reuse the wrong implementation. `-NoBuild` does not validate binary/source correspondence.

## Profiling interpretation

The benchmark records monotonic wall-clock intervals between process callbacks, reciprocal mean interval throughput, nearest-rank frame percentiles, stutter counts, Godot process/physics monitors, measured production arena advance CPU time, draw calls, total process CPU core equivalents, GC counts, managed bytes, process private bytes, engine/video bytes, nodes, resources and orphans. Engine monitors may be delayed and are not independent GPU timings. Process CPU includes all threads; it does not identify the main thread or prove GPU saturation.

Use Godot's Debugger **Profiler**, **Monitors** and, where the renderer supports it, **Visual Profiler** for a separate attributed capture. Profiling itself adds overhead. The built-in script profiler does not profile C# methods; use the harness's explicit timers or a managed/native profiler for method attribution. See [Godot profiler](https://docs.godotengine.org/en/stable/tutorials/scripting/debug/the_profiler.html), [performance monitors](https://docs.godotengine.org/en/stable/classes/class_performance.html) and [debugger/visual profiler](https://docs.godotengine.org/en/stable/tutorials/scripting/debug/debugger_panel.html). Keep profiler captures separate from uninstrumented throughput comparisons.

Compare repeated-trial ranges before attributing a small FPS change. Rising private bytes can reflect caches or GC timing; inspect warmed-up resource/node counts, repeated ownership cycles and post-teardown state before claiming a leak. Do not force GC inside measured windows.

For a separate render-timing capture, enable `RenderingServer.ViewportSetMeasureRenderTime` and record its CPU/GPU timers plus `GetFrameSetupTimeCpu`. Rendering CPU excludes script and physics work; initialization and screenshot stalls must be separated from steady-state analysis. See [RenderingServer timing APIs](https://docs.godotengine.org/en/stable/classes/class_renderingserver.html#class-renderingserver-method-viewport-get-measured-render-time-cpu). For an uncapped human comparison, `--disable-vsync --max-fps 0` requests those settings, but driver-level VSync can still override the application; verify it separately ([Godot command-line reference](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html)). Never use `--fixed-fps` as a real-time performance cap.

## Complementary native workloads

- `check-world-wall.ps1 -TerrainStability -ProductionMap -Impaired -Profile`: repeated sixteen-wall deployment, settled fitting/ray counts and cleanup over two local UDP arenas. Its FPS is headless unless rendered explicitly, and is not game FPS.
- `check-world-wall.ps1 -Heavy -Impaired`: vehicle contact/pressure and wall behavior.
- `check-machine-gun.ps1 -Impaired`, `check-machine-gun-vfx.ps1`, `check-salvo.ps1`: real weapon authority/rays and bounded simultaneous effects.
- `check-carnival-smash.ps1`: 60 seconds of claim pressure and expiry, including paused expiry.
- `check-item-spawns.ps1 -Impaired`, `check-pickup-drive.ps1 -Impaired`: eight-peer consistency and actual native moving pickups.
- `check-death-respawn.ps1 -Impaired`, `check-reconnect.ps1`, `check-post-match.ps1 -Cycles 3`: lifecycle, recovery and ownership cycles.

Supply `-GodotPath <exe>` to each. Their assertions and caps differ from the benchmark; use them for functional/resource evidence, not interchangeable FPS comparisons. Tests on independent physical PCs, reboot controls, thermal/power throttling, physical controls, audio perception and remote EOS remain explicitly human-owned when not directly observed.
