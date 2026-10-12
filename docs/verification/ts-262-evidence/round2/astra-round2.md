# Astra Runtime Critique — Story Round 2

TS-262 · `ts-262-jg` · October 8, 2026

**Overall Score: 8.3 / 10**

**Quality Assessment: PASS (>=8.0)**

The approved car engagement behaves intentionally in the exercised runtime: the camera actually follows a displaced acquired car, small errors retain that car, and deliberate mouse/controller input releases it even at minimum sensitivity. Cover and intervening cars break engagement; passing missiles and other non-car objects do not steal it. Framed desired aim uses the car centre, while leaving engagement restores cursor-directed free fire. The retained 200 m sample meets the requested approximate 80% frame grouping without equating the frame with a damage collider.

This is a fresh judgment of the expanded Round 2 scope. It is not a carry-forward of the Round 1 score or a score for engineering effort/test volume. The result is strong within scripted native play, with limits on human feel and unexplained historical iteration observations stated below.

## Scope and category scores

The October 8 approved additions supersede the earlier friction-only/preserved-spread boundary: living visible rival-car acquisition and retention, bounded camera attraction, deliberate breakaway, engagement-only recenter hold, centre-directed accepted aim, and approximately 80% grouping at 200 m using fixed dispersion. Existing damage, falloff, range, cadence, ammunition, readiness and self-clearance remain relevant integrations. Production weapon/barrel/muzzle artwork, new firing/tracer/impact VFX and other weapon firing integrations remain excluded.

| Runtime category | Score | Observed basis |
| --- | ---: | --- |
| Runtime Functionality | 8.6 | Attraction, retained identity, centre desired aim, free fire and eligibility/occlusion transitions worked in the independent native run. Supporting full runtime evidence covers firing and lifecycle integration. |
| Controls / Responsiveness | 8.4 | Scripted small corrections retain engagement; deliberate mouse/controller breakaway and sustained-input non-reacquisition passed at 30/60/144 FPS and minimum/maximum gains. Physical ergonomics are not inferred from this. |
| Camera | 8.2 | Actual camera alignment converged toward the displaced target with neutral input; disabling attraction left an error. Captures show a usable central view. Prior unexplained camera/input observations limit confidence beyond exercised conditions. |
| UI / Target Readability | 8.0 | One acquired car remains framed through small movement and competing targets; non-cars retain the ordinary cursor. At 200 m the true unpadded frame is very small, especially at 90-degree FOV; it identifies engagement but does not make distant vehicle detail easy to read. |
| Multiplayer / Networking Experience | 8.3 | Independent three-peer impaired local UDP engagement completed; supporting integrated execution agrees on ordered shots and observer articulation. WAN latency and independent-device experience are unverified. |
| Runtime Integration / Stability | 8.3 | Corrected final runs complete without diagnostics across firing, settings, movement and lifecycle. Cover, release and resource behavior remain coherent in retained native evidence. Failed iterations remain part of the evidence, not retroactively passes. |
| Feature-Specific Grouping | 8.6 | Retained actual authoritative-ray measurement records 80.3125% inside the frame at 200.000 m across six orientation/FOV cases, with meaningful outside rounds and separately recorded car intersections. |

Overall is a holistic scoped score. Audio, subjective fun, final art/VFX polish, prolonged competitive balance and frame-time performance were not scored without adequate direct evidence.

## What was exercised

**VERIFIED — independent final-build scripted native run.** After the implementation agent completed the corrected comprehensive gate and full rendered verification, I ran:

```powershell
./check-weapon-aim.ps1 -GodotPath 'C:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' -NoBuild -Sticky -Visual -Impaired -Oval
```

The production-oval run passed **45 accuracy/retention checks** through three real local UDP peers, with **no warnings/errors**. [Full independent log](astra-df9f9a9a/aim.log).

- Displacing the acquired car produced **0.03216 → 0.00000 rad** camera error with attraction; disabled pull retained **0.04515 rad** error. This demonstrates actual camera motion, not merely reduced input sensitivity.
- The moving target retained its frame through **90 frames** of small mouse errors. Framed desired aim used its world-space body centre. Deliberate mouse breakaway restored free aim, and **40 actual unframed rounds** fired.
- Small controller corrections retained the car; full aim-away released it. Six paired cases covered **30/60/144 FPS**, gains **0.25/3**, FOV **50/90**, and recenter **3**. Retention settled to approximately **0.00003–0.00004 rad** error. Mouse and controller release succeeded at both gain extremes, and continuing deliberate input did not repeatedly reacquire the car.
- A car crossing behind the retained car did not steal its frame. An intervening car and solid world cover released engagement. A production missile crossing the engaged view did not replace the car; missile, mine, rock and pickup presentation probes did not acquire assistance. Suppression retired acquired identity and HUD.

**VERIFIED — direct inspection of native rendered captures.** Inspected the independent [moving-car capture](astra-df9f9a9a/sticky-moving-car.png), the retained actual 200 m [50-degree FOV](accuracy-yaw-0-fov-50.png) and [90-degree FOV](accuracy-yaw-0-fov-90.png) captures, and the latest full-run [rear fire](astra-df9f9a9a/full-mg-rear.png), [elevated-target fire](astra-df9f9a9a/full-mg-airborne.png), [driving](astra-df9f9a9a/full-07-driving.png) and [airborne shooter](astra-df9f9a9a/full-07a-airborne-shooter.png). Also inspected the earlier Round 2 oval moving-car and simultaneous-fire captures. Framing, target identity and rack/payload presentation remain understandable. The 200 m captures substantiate the small real frame rather than an enlarged benchmark rectangle. These are still-frame observations, not continuous animation or human play observations.

**INFERRED / corroborating recorded runtime — not independently rerun here.** Reviewed the final [full rendered oval log](aim-oval-final.log): **1,061 checks**, **580 native rays / 338 target hits**, **323 simultaneous sustained rounds**, **464 matching ordered remote outcomes**, all **152 Camera preference cases**, native driving, flight/landing, NOS switching, blackout/expiry, removal/new life, actual missile death and timed respawn. It is the corrected final full run, replacing the earlier 1,042-check run as the current integration evidence. The five directional firing probes use zero spread to isolate geometry; the simultaneous sustained phase uses the configured default spread. Their hit counts are not interchangeable accuracy claims.

Reviewed the retained [accuracy log](accuracy-final.log): six **2,400-round** samples at measured **200.000 m**, car yaw **0/45/90 degrees**, FOV **50/90**, fixed **0.42-degree half-angle** dispersion. Actual rays, not only visible tracers, yielded **11,565 inside / 2,835 outside of 14,400**, or **80.3125% / 19.6875%**; individual inside fractions were **79.00–81.50%**. Native vehicle intersections were separately counted and lower than frame inclusion. This supports the approved approximate grouping requirement, not guaranteed damage or an 80% hit quota. The measurement preceded the later input-release correction; applying it to the final build relies on the unchanged spread, settled centre-aim and measurement paths. I did not rerun that large sample independently.

The current Story report's native Machine Gun, shield/wall and reconnect evidence supports damage/falloff, cover, release/exhaustion and exact retained resources. Reconnect records **299/800 rounds, phase 0.25**, no replay and three resyncs including **125 seconds offline**. Those checks were not independently rerun for this critique. The repeated comprehensive gate is context, not experiential scoring evidence.

## Runtime / operational limitations and retained failures

- **Scripted operation:** input goes through Godot state and the production input adapter, but automatic desktop event dispatch is disabled in the final fixture. Targets follow authored positions/trajectories. This isolates runtime behavior; it does not establish physical mouse/controller comfort, human tracking skill, mixed physical-input behavior or competitive enjoyment. No audio listening, authenticated EOS, WAN, independent-machine or long-session testing is claimed.
- **Low-sensitivity correction:** the implementation agent inferred the earlier release defect from sensitivity-gained input. Its attempted before-fix runtime stopped earlier at the intervening-car fixture, so it was not a demonstrated before/after failing release reproduction. Corrected release behavior is directly exercised in this independent run. The [before-fix log](low-gain-regression-before.log) remains a failed iteration.
- **Unexpected camera movement:** a pre-isolation full replay failed forward-hit coverage after the camera changed yaw/pitch without scripted movement at that stage. The fixture previously admitted desktop events; isolation was added, and final full/independent runs pass. The exact cause of that prior movement is **not proven**. [Failed replay](oval-input-iteration.log). This is neither erased nor asserted to be a production defect that has been fixed.
- **Earlier Round 2 fixture failures:** the Story report retains early render-follow timing assertions, a missing restored second-slot Missile, a damage-comparison rounding bound, and focused-mode shutdown warnings. The [shutdown log](sticky-shutdown-iteration.log) remains a diagnostic failure even though current focused teardown is clean. These do not establish current player-facing failures, and correcting fixtures does not retroactively change their results.
- **Round 1 camera comparison:** the earlier rendered CameraDistance authority comparison remains unexplained historical evidence. Current rendered settings coverage passes; no baseline-main reproduction or causal resolution is claimed.
- **Distance and geometry:** the frame is a projected body envelope, not a collision shape. Its small size at 200 m limits visual detail, and fixed angular spread naturally changes grouping with distance/orientation. The benchmark uses held unobstructed poses and enlarged fixture magazines; it does not prove the same grouping while targets turn, cross cover or move under network delay.
- **Presentation boundary:** the labelled placeholder box has no production barrel/muzzle. Future production geometry requires its own clearance/alignment review. Current observations do not rate excluded art/VFX work or prove continuous animation smoothness and performance.

## Round boundary

Independent runtime source: `.godot/aim-checks/df9f9a9ab33d4608b69419646012dfa0`; its complete log and moving-car capture are preserved in `astra-df9f9a9a/`. The four `full-*` supporting captures in that directory were copied from the implementation agent's final `.godot/aim-checks/2ee2f5fb05a4492a9a572cdf26cf0fb3` run and inspected here; they are not represented as another independent run.

No source/test changes, fixes, polish, commits, pushes or corrective Jira work were performed by this reviewer. **Round 2 ends here. Stop for explicit human direction.** PASS advises the human; it does not authorize another round, acceptance or merge.
