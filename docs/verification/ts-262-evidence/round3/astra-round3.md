# Astra Runtime Critique — Story Round 3

TS-262 · `ts-262-jg` · October 9, 2026

**Overall Score: 8.2 / 10**

**Quality Assessment: PASS (>=8.0)**

The revised assistance produces a substantial operational improvement during the exercised driving manoeuvre while preserving deliberate placement and release. With the same throttle/steering and authored rival trajectory, the independent run kept the car engaged throughout the measured turn, greatly reduced tracking error, and increased actual native intersections. Fine adjustments remained off-centre in authoritative shots; slow release, switching, dismissal/rearming and normal chase return worked.

**The first independent run nevertheless failed its overall verification command:** all 17 behavior checks passed, then Godot reported **two leaked ObjectDB instances at exit**, and the wrapper exited 1. One unchanged verbose replay completed without that warning. The failed run remains **FAIL**, and the leak's identity, cause and production relevance remain unresolved. The experiential PASS is not a claim that every execution passed or that shutdown is proven clean.

The score evaluates the observed scoped feature, not test volume, effort or the fact that this is the final round. It does **not** establish that the user's reported human difficulty driving and shooting is resolved. Scripted tracking improvement is directly demonstrated; human workload, comfort and enjoyment remain unverified.

## Scope and category scores

Reviewed the current repository routing, critique policy, weapon-aiming contract and the approved October 9 changes in the Story verification report. This round permits forgiving near-car acquisition, initial centre attraction, target/lens motion and shooter-heading compensation, fine off-centre placement in desired/accepted fire, deliberate slow/strong release, dismissal/rearming and normal free aim/chase return. Mandatory centre-directed firing while framed is superseded. Driving physics, host safety, 0.42-degree dispersion, damage/resources and range remain unchanged. Production art, barrel/muzzle geometry, VFX and other weapon firing integrations remain excluded.

| Relevant runtime category | Score | Judgment |
| --- | ---: | --- |
| Runtime Functionality | 8.6 | Near-car acquisition, deliberate placement, actual shots, switching, dismissal/rearming and chase return worked in two independently executed focused runs. |
| Controls / Responsiveness | 8.4 | Assistance reduces the need for corrective camera input during the scripted turn while fine placement survives neutral hold. Both slow and strong departures release. Physical ergonomics are not established. |
| Camera / Driving-Aim Coordination | 8.3 | Measured tracking error falls substantially without changing the driven path. The camera keeps the rival in the aiming region while chassis heading changes, then returns behind the vehicle after release. Continuous human motion comfort remains unverified. |
| UI / Visual Coherence within Scope | 8.0 | Inspected captures keep engagement readable and show an articulated payload under the fixed rack. At 200 m the real body frame remains tiny, especially at wide FOV; it does not expose useful distant vehicle detail. |
| Multiplayer / Runtime Integration | 8.2 | Own runs use three impaired local UDP peers and actual accepted firing. Retained integrated evidence covers remote shot agreement, settings and lifecycle; it is distinguished from independent execution below. |
| Runtime Stability | 7.2 | Gameplay scenarios completed, but the independent shutdown leak and unresolved earlier intermittent observations prevent a clean stability judgment. The single clean diagnostic replay is not a demonstrated fix. |

Overall is a holistic scoped assessment, not an average rounded to the threshold. Audio, subjective fun, continuous animation/performance and competitive balance were not scored without adequate evidence.

## What was exercised

### VERIFIED — independent rendered native execution

After final implementation verification, ran the current build without rebuilding or modifying it:

```powershell
./check-weapon-aim.ps1 -GodotPath 'C:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Freedom -Visual -Impaired -Oval
```

The three-peer production-oval run completed **17 behavioral assertions**, then failed the wrapper because of the shutdown warning. [Complete first-run log](astra-4444c9d7/aim.log).

| Same authored driving comparison | Assistance disabled | Assistance enabled |
| --- | ---: | ---: |
| Peak speed | 18.89 m/s | 18.89 m/s |
| Travel | 16.03 m | 16.03 m |
| Heading change | 0.949 rad | 0.949 rad |
| Actual rays / native target hits | 240 / 59 | 240 / 240 |
| Retained measured frames | 0 / 180 | 180 / 180 |
| Mean centre error | 0.39778 rad | 0.00141 rad |

This directly demonstrates improved tracking in this manoeuvre, without claiming a universal hit rate or reduced human workload. Rival motion is fixture-authored; shooter throttle, steering and movement use native production input/physics. The comparison is near-range, not the separate 200 m grouping benchmark.

The same run acquired a car from a genuine cursor miss and converged to its centre. Fine input produced an offset of **0.01198 rad**, preserved at **0.01198 rad** after the neutral hold. **40 actual authoritative rays** averaged **0.01433 rad** off body centre and passed the accepted-placement check: the visible frame no longer forces centre fire. Deliberate aim-away released after driving, a sweep selected the chosen second car, slow outward input released beyond the frame, stopping beside the dismissed car did not pull aim back, returning toward it rearmed acquisition, and neutral release restored normal chase yaw with the logical cursor centred.

Made **one bounded unchanged diagnostic replay**, directly invoking the same Godot scene and freedom/impaired/oval arguments with `--verbose`, the same 30-FPS cap and quit bound. It completed all 17 behavior checks, exited 0 and contained no `WARNING:`/`ERROR:` or leak report. It again preserved fine-placement shots and measured assisted **241/241** intersections, **180/180** retained frames and **0.00141 rad** error, versus unassisted **240/62** and **0.39997 rad**. [Complete diagnostic log](astra-4444c9d7/diagnostic-verbose.log). No third attempt was made; the diagnostic did not identify the previously leaked objects.

### VERIFIED — direct capture inspection

Inspected this critique's [post-drive engagement](astra-4444c9d7/freedom-driving.png) and [fine placement](astra-4444c9d7/freedom-fine-placement.png), plus the implementation run's corresponding captures, [close-side fire](mg-close-side.png), and actual 200 m [50-degree FOV](accuracy-yaw-0-fov-50.png) / [90-degree FOV](accuracy-yaw-0-fov-90.png) captures. The post-drive frame shows the target near the central aiming region despite a substantial chassis turn; the fine-placement view keeps the frame on the selected car. The driving PNG is captured after the measured movement and its speedometer reads zero: it is not evidence of motion by itself. Runtime diagnostics provide the movement comparison.

These are native still-frame observations, not continuous video or direct human play. The existing labelled placeholder remains visible and can overlap the target region; final weapon art/VFX are not assessed or requested here.

### INFERRED / corroborating recorded runtime — not independently rerun here

Reviewed the final [full rendered integration log](aim-oval-final.log): **1,076 checks**, **588 firing rays / 341 target hits**, actual HP loss in all five firing directions, **323 simultaneous sustained rounds**, **469 matching ordered observer outcomes**, all **152 Camera preference cases**, mouse/controller release across frame rates/gains, cover/non-car exclusion, free fire, readiness/self-clearance, expiry/blackout, switching, flight/NOS and actual death/respawn. Its broad scenario coverage supports integration beyond the focused independent run. Zero-spread directional probes isolate geometry; their hit counts are not default-spread accuracy claims.

The final [accuracy log](accuracy-final.log) records **14,400 actual authoritative rounds** at **200.000 m**, with **11,566 inside / 2,834 outside** the actual unexpanded target-depth frame: **80.3194% / 19.6806%**. Six yaw/FOV cases range from **78.92% to 81.29%** inside; native car collisions are separately counted. This supports the settled-centre benchmark at fixed 0.42-degree spread. Deliberately off-centre placement is allowed to change that grouping. These held unobstructed target poses and enlarged fixture magazines do not establish moving-target or combat balance results.

The recorded camera/obstruction and [reconnect](reconnect-final.log) runs support surrounding behavior; reconnect retains **299/800 rounds and phase 0.25**, no shot replay and a 125-second offline resync. As recorded in the Story report, these surrounding-system runs preceded the final dismissed-target life/device bookkeeping change. Final integrated aiming runs exercise the completed build. The comprehensive gate is context, not a substitute for experiential evidence.

## Runtime / operational limitations and unresolved observations

- **Current independent shutdown failure:** `WARNING: 2 ObjectDB instances were leaked at exit` occurred after all gameplay assertions. No crash or gameplay symptom accompanied it in the recorded run. The single verbose replay did not reproduce it, so the objects and lifetime are unknown. Neither a fixture-only cause nor repeated-session memory growth is established. The first command remains failed and should remain visible in delivery evidence.
- **Retained implementation iterations:** [activation](activation-iteration.log) and [capture-stall](capture-stall-iteration.log) runs produced only five assisted rays despite target retention; [rear aim](rear-aim-iteration.log) lost expected accepted aim after camera movement; [damage](damage-iteration.log) counted intersections without passing the HP-decrease assertion. Fixture activation, render waits, focus restoration, capture scheduling and diagnostics subsequently changed. Their exact original causes were not conclusively demonstrated. Later successful runs do not erase these failures or prove that all were harmless fixture effects.
- **Safety and isolation:** the existing host input-silence gate still releases held fire during long delivery gaps. PNG scheduling changes did not weaken it. The fixture disables desktop input dispatch and restores scripted held input after focus notifications. Physical input/focus behavior and the player's recovery experience after a real stall are **UNVERIFIED** here.
- **Human objective:** the user explicitly found Round 2 insufficient. The new measured comparison supplies stronger evidence for this round, but it cannot override that feedback or certify a better human driving/shooting experience. Hand coordination, cognitive workload, target acquisition comfort, controller ergonomics and enjoyment remain **UNVERIFIED**.
- **Network/media coverage:** three arenas/sockets run on one Windows machine under simulated 30 ms delay, 5 ms jitter and 2% loss. Independent-device Internet sessions, authenticated EOS, continuous video/audio observation, frame-time profiling and prolonged play remain **UNVERIFIED**.
- **Scope limits:** production barrel/muzzle clearance, final art/VFX, universal moving-target accuracy and competitive balance are not established. The distant frame is a projected body envelope, not a damage collider or a promised 80% hit rate. Earlier Round 1/2 unresolved observations remain historical evidence, not retroactively resolved by Round 3.

## Final round boundary

Independent first-run source: `.godot/aim-checks/4444c9d7b4be4214b7b96266c90730e7`. Diagnostic source: `.godot/aim-checks/astra-round3-verbose-20261009`. Both complete logs and first-run captures are retained in `astra-4444c9d7/` beside this report.

No source/test changes, fixes, polish, commits, pushes, PR or Jira changes were performed by this reviewer. **This is the third and final permitted Story critique round. STOP for explicit human direction.** There is no automatic fourth round. This PASS advises the human; it does not authorize acceptance, further implementation or merge, and it does not turn the failed independent verification command into a pass.
