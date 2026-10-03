# Astra Runtime Critique — Story Round 1

**Overall Score: 7.8 / 10**
**Quality Assessment: FAIL (<8.0)**

TS-280, `ts-280-pg`, 2026-10-03. This is the independent formal runtime critique of the completed implementation, after integrated verification. The exact pass threshold is 8.0; 7.8 is not rounded up. No implementation corrections were made during this critique.

The required contact behavior is convincing in the exercised scenarios: slow pressure remains quiet, reverse input releases the truck, landing retains a meaningful impact and permits continued driving, and impaired multiplayer following advances continuously. Debris clears consistently at its deadline. However, the integrated destruction run independently reproduced resource-lifecycle exceptions during repeated vehicle creation. That current operational failure prevents a highly polished runtime-stability assessment, even though the destruction assertions themselves completed successfully. This score does not reward test counts or judge source architecture.

## Category Scores

| Material runtime category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.5 / 10 | Required collision and expiry outcomes worked in the direct scenarios. |
| Controls / Responsiveness | 8.3 / 10 | Sustained pressure, reverse escape and post-impact acceleration remained predictable through bounded logical inputs. |
| Vehicle / Movement Feel | 8.3 / 10 | Quiet low-speed contact and recoverable weighted impact; no loss of control in the observed drive-away. |
| Physics | 8.4 / 10 | Stable held contact; native embedded/drop cases recovered; brief landing penetration remained bounded. |
| Multiplayer / Networking Experience | 8.4 / 10 | Four impaired UDP worlds followed without stopped/backward samples; inspected render showed separated vehicles. |
| Runtime Integration | 7.7 / 10 | Expiry and late admission agreed, but repeated integrated scene creation emitted two resource-handle exceptions. |
| Runtime Stability | 6.8 / 10 | Destruction script failed its diagnostic gate on the independently reproduced exceptions; no crash or persistent gameplay failure was observed. |

Overall is a judgment of the integrated runtime result, not an arithmetic category average. No audio, general art direction, UI or physical-controller score is assigned.

## What Was Exercised

- **VERIFIED — independent rendered multiplayer run.** Executed Godot `--path . res://scenes/verification/following_contact_network_checks.tscn -- astra-following`. Four real UDP worlds, two following pairs, 30 ms delay / 5 ms jitter / 2% loss: 529 close-contact frames, zero stopped frames, zero backward frames, correction p95 0.029542 m, maximum 0.125980 m, minimum visual center gap 5.312 m. Inspected the captured rendered frame and runtime trace. A center gap is not a bumper-clearance measurement. [Log](astra-following.log), [trace](astra-following.json), [view](astra-following.png).
- **VERIFIED — independent native contact matrix.** Executed `./check-repeated-collisions.ps1 -GodotPath <Godot 4.7.2 Mono console> -NoBuild`. Paired contact and all 40 slow/close/5-cm embedded/8-m drop cases passed. Paired contact lasted 513 frames, maximum travel error 0.004115 m and maximum step speed change 0.076719 m/s. Drop recovery included at most 0.0301 m overlap for one frame and peak angular speed 3.837 rad/s. These are native measurements, not continuous visual observation. [Log](astra-repeated.log).
- **VERIFIED — agent-driven rendered production-map playtest.** Launched `res://scenes/verification/handling_playtest.tscn -- --rock-playtest --handling-network` and issued bounded input commands using the existing playtest seam. A fresh 300-frame approach was followed by 240 held-contact frames: contact on every held frame, peak angular speed 0.002518 rad/s, final up 0.9999743, HP 1000. A 150-frame reversing segment cleared contact and moved approximately 10 m away. An 8-m drop with -8 m/s initial vertical velocity produced seven contact frames, peak angular speed 1.170686 rad/s and HP 947.9166; 360 frames of steering/throttle drove away upright (up 0.997867) with unchanged HP. Inspected approach, landing and drive-away renders. Initial exploratory pressure segment reached the perimeter; it is preserved but is not presented as the held-rock trial. The playtest error log was empty. Only the process launched for this critique was stopped. [Commands](astra-playtest-commands.jsonl), [measurements](astra-playtest-summary.jsonl), [approach](astra-approach-view.png), [landing](astra-drop-view.png), [drive-away](astra-driveaway-view.png), [runtime output](astra-playtest.log), [error output](astra-playtest-errors.log).
- **VERIFIED — independent integrated destruction run, with diagnostic failure.** Executed `./check-destructible-environment.ps1 -GodotPath <Godot 4.7.2 Mono console> -NoBuild`. Native destruction/recovery and three impaired UDP worlds completed their behavior assertions, including late join, all 157 rocks / 2,288 plants, exact 600-tick debris expiry and continued absence through 690 ticks. The executable reported `failures=0`, but the wrapper correctly exited 1 because two `SwapGCHandleForType` exceptions occurred. This run is **not a passing check**. It was not rerun to seek a clean result. [Complete log](astra-debris.log).
- **VERIFIED — observation of existing rendered evidence.** Personally inspected [599-tick](cleanup-599.png) and [600-tick](cleanup-600.png) production-map captures. The foreground fragments disappear while surrounding authored rocks remain. These captures were produced during implementation, not my playtest.
- **INFERRED — broader recovery and legitimate-impact coverage.** Reviewed the Story evidence for reconnect after 125 seconds, migration retaining the original deadline, separate-process host termination and head-on HP 924.113 for both participants. Those broader runs were performed by the implementation agent and were not repeated in this critique. The direct drop independently confirmed meaningful impact damage still occurs.

## Material Finding and Recommendation

**Problem:** Repeated integrated vehicle/scene creation still emits resource-handle lifecycle exceptions.

**Evidence:** The independent destruction run logged two `System.InvalidOperationException: Handle is not initialized` errors in `Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType`. Stacks traverse native resource loading / `MatchResourceLoader.LoadResource` and `CarRackPresentation._Ready` during `DestructibleEnvironmentChecks.Scenario`. See [astra-debris.log](astra-debris.log), errors beginning at lines 29 and 82. All behavior assertions subsequently completed, but the diagnostic gate failed. Related symptoms had already occurred during implementation; this is current reproduced evidence, not merely a historical caveat.

**Severity:** Medium.

**Impact:** Repeated runtime setup is not consistently clean. Resource/presentation initialization has an unresolved failure mode, reducing confidence in sustained or repeated-session integration. No crash, missing rack, corrupted collision state or player-visible degradation was established in this run; those consequences must not be asserted as observed.

**Suggested Improvement:** If authorized, isolate repeated native scene creation/resource teardown and compare unchanged main under the same sequence. Determine whether the lifetime fault belongs to the fixture, application resource ownership or engine, then correct the responsible lifecycle and verify repeated creation plus the complete destruction/expiry scenario without suppressing diagnostics. A single clean rerun would not resolve this recurrent symptom.

**Scope:** **Out-of-Scope Recommendation.** A resource-loader/presentation lifecycle correction is outside the three assigned TS-280 behavior changes unless the human explicitly expands scope. Its occurrence in this branch does not establish that TS-280 introduced it.

**Corrective Work Type:** New Corrective Task, only if the human authorizes investigation/correction. No issue was created.

**Recommended Next Round:** If another round is authorized, prioritize establishing and resolving this lifecycle failure, then rerun the affected integration and bounded contact observations. No collision-feel or cosmetic change is recommended from the observed scoped behavior.

## Runtime / Operational Limitations

- Direct work used Windows, Godot 4.7.2 Mono, GTX 1070 Compatibility/OpenGL and the current Debug build. Native runs were sequential.
- Inputs were bounded logical commands, not physical keyboard/controller play. Images are sampled frames; movement continuity judgments use time-series runtime evidence, not a watched full-motion recording. Audio was not heard or judged.
- Multiplayer used same-machine local UDP and simulated impairment, not authenticated EOS, Internet transport, independent devices or exported clients. Abrupt remote braking, highly adverse networks and mixed-build interoperability were not independently assessed.
- Long reconnect/migration and eight-car driving are supporting implementation evidence, not new direct critique runs. No ten-minute or indefinite soak was performed.
- The additional pickup-motion fixture fails identically on unchanged main at 3.0506 m outside its 3 m sphere, according to the recorded comparison. Its remaining motion sample remains unverified; this critique does not claim a clean comprehensive runtime suite.
- Earlier frame-budget variability is preserved in the Story report. This critique makes no broad frame-rate or performance guarantee.
- The reproduced resource exception's player-visible effect and root cause remain unverified. The score reflects this material uncertainty without attributing unsupported gameplay damage or regressions.

## Human Gate

Formal Round 1 is complete. Per `docs/critique.md`: **“After presenting any formal critique, STOP and wait for explicit human instruction.”** No recommendations, corrective issues or further critique round are authorized by this result. The human may accept this result as-is or authorize specific follow-up work.
