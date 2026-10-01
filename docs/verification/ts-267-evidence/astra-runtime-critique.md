# Astra Runtime Critique — Story Round 1

TS-267 · 2026-10-01 · branch `ts-267-pg`

**Overall Score: 7.0 / 10**
**Quality Assessment: FAIL (<8.0)**

The exercised rock contact is coherent and recoverable, and the evidence establishes a substantial reduction in collision cost. It does not yet establish resolution of the Story's reported multiplayer trapping failure: the original hulls also separate in the reproduced scenarios. An additional destruction integration run emitted unresolved runtime exceptions. Those uncertainties prevent a robust, polished acceptance judgment despite the successful focused trials. The score evaluates runtime outcomes and confidence in those outcomes, not implementation effort or test count.

## Category Scores

| Material category | Score | Runtime basis |
| --- | --- | --- |
| Runtime Functionality | 7.0 | Impact, sustained pressure, two re-contact cycles and reverse separation work in the direct trial; resolution of the reported persistent trap remains unverified. |
| Physics | 7.5 | Small measured penetration, bounded angular response and clean recovery in both adapters; original problematic multiplayer geometry/state is not reproduced. |
| Controls / Responsiveness | 7.5 | Scripted reverse predictably disengages on each attempt; continuous human steering/controller feel was not assessed. |
| Observed Performance | 8.3 | Collision timing improves substantially in matched recorded trials; independently run contact stays below the harness mean budget. Whole-client sustained frame pacing remains unmeasured. |
| Runtime Stability | 6.5 | This review's focused run is error-free, but the final destruction run contains two managed-handle errors with unresolved session impact. |
| Runtime Integration | 6.5 | Actual-map pressure/reverse evidence is coherent; destruction lifecycle failures remain unexplained. |
| Multiplayer / Networking Experience | 7.0 | Recorded impaired UDP two-world convergence is encouraging; the human multiplayer failure context and separate-machine behavior were not recreated. |

The overall score is a judgment of the integrated result, not an arithmetic average. Audio, UI, general art direction and unrelated camera behavior are not scored.

## What Was Exercised

**VERIFIED — directly executed for this critique:** rendered Godot 4.7.2 .NET, OpenGL Compatibility, GTX 1070, 1152×648, fixed 60 Hz:

```powershell
& $Godot --path . --resolution 1152x648 --fixed-fps 60 res://scenes/verification/rock_collision_checks.tscn -- rock=RockCluster speed=12 angle=0 label=astra
```

Both adapters ran 705 ticks at default rock scale 2, including pressure, neutral, reverse, repeated contact and final separation. Exit code 0; `failures=0`; no error or warning in the run log.

| Adapter | Contact frames | Re-contact frames | Final contacts | Maximum penetration | Contact mean / p95 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Network | 284 | 73 | 0 | 0.0002 m | 4.466 / 8.547 ms |
| Practice | 334 | 81 | 0 | 0.0130 m | 0.321 / 0.351 ms |

I directly viewed both adapters' pressure images at tick 170 and the network separation image at tick 700. The vehicle remains visibly outside the rock mass and later clearly separated; these viewpoints do not prove exact mesh/collider fit at every face. I inspected the resulting traces at first contact, pressure, each reverse boundary and final separation. Both have zero contacts at ticks 239, 389 and 700, and reverse speed near 11 m/s at tick 700. No persistent embedding is demonstrated in this trial. Outputs are in `.godot/ts-267/astra/`, with console log `.godot/ts-267/astra-runtime.log`.

**VERIFIED — directly inspected recorded evidence, not rerun here:** I viewed [actual-map pressure](map-pressure.png), [actual-map separation](map-separated.png) and [glancing contact](glancing-contact.png), and inspected actual-map pressure/reverse traces. Those show the authored-boulder encounter stopping under pressure and subsequently clearing it. I read the final UDP, original-hull gate, hull-audit and destruction logs, along with the [Story verification report](../ts-267.md). The final UDP p95 values are 11.131–16.136 ms versus the original-hull gate's 48.419 ms failure. The destruction log records `Handle is not initialized` twice during fixture/resource recreation, including `BoostExhaust._Ready`, alongside the obsolete coverage assertion.

**INFERRED:** lowering hull complexity addresses the reproduced collision-cost defect. The measured improvement is persuasive, but it does not establish that the same change cures the reported persistent trap. The broader 90-case matrix, slopes and UDP results are implementation verification evidence; I did not independently rerun those complete suites.

## Runtime / Operational Limitations

- No reproduction of the original persistent multiplayer trapping failure, before or after the change. Passing recovery in cases that also pass with original hulls cannot establish its root cause or cure.
- No continuous human input, separate-machine/Internet/EOS session, exported build, long session or whole-client frame-time capture. A rendered scripted run and selected still frames do not establish subjective smoothness throughout gameplay.
- Practice callback timing excludes native rigid-body solving outside that callback. Fixed-FPS execution and wall-clock callback measurements are not a full frame-pacing measurement.
- Collider simplification has measured inward/outward plane deviations up to 0.19632/0.10029 source units, multiplied by placement scale. All relevant faces, gaps and map placements were not visually exercised; no universal collider-fit claim is made.
- Destruction exceptions were reviewed from recorded output, not reproduced or diagnosed in this critique. They are not established as new regressions or as harmless pre-existing failures.

## Recommendations

### 1. Recreate the reported multiplayer failure before claiming resolution

- **Problem:** the central persistent-trapping outcome remains unverified.
- **Evidence:** original and simplified hulls both separate in the tested recovery scenarios; Jira describes repeatedly encountered human multiplayer trapping, jitter and lag.
- **Severity:** High.
- **Impact:** the measured performance improvement could ship while the originally reported player lockup remains.
- **Suggested Improvement:** capture the affected authored rock/placement, vehicle pose, impact/input history and multiplayer state from a failing session; replay it against original and simplified hulls, then verify sustained pressure, steering/reverse escape and re-contact with frame-time and penetration traces. If reproduction remains unavailable, explicitly seek human acceptance of the narrower demonstrated performance result rather than declare the trapping criterion satisfied.
- **Scope:** In Scope.
- **Corrective Work Type:** Existing Task (TS-267 reproduction and verification; change production behavior only if the evidence establishes a remaining defect).

### 2. Establish the destruction lifecycle errors' session impact

- **Problem:** applicable final integration execution emitted native/managed resource-lifecycle errors.
- **Evidence:** [final destruction log](ts-267-final-destruction.log), two `System.InvalidOperationException: Handle is not initialized` errors during resource/fixture recreation. The separate obsolete coverage assertion does not explain them away.
- **Severity:** Medium.
- **Impact:** clean repeated-use/session stability cannot be asserted from that run; player-visible consequences and relationship to this change are unknown.
- **Suggested Improvement:** reproduce fixture/vehicle recreation in a focused run and compare unchanged baseline with this branch. Verify whether normal session respawn/reload triggers it. Resolve a demonstrated in-scope regression, or present baseline evidence and seek explicit disposition of a separate defect before treating this integration result as acceptable.
- **Scope:** In Scope for diagnosis and verification. An unrelated exhaust/resource-lifecycle redesign would be an **Out-of-Scope Recommendation** requiring separate authorization.
- **Corrective Work Type:** Existing Task for diagnosis; New Corrective Task only if an unrelated production defect is confirmed and separately authorized.

## Recommended Next Round

If authorized, prioritize the actual multiplayer trapping reproduction and destruction lifecycle diagnosis, then rerun only affected verification plus the required final checks. Exercise the resulting real-map case and repeat this runtime critique. Do not add speculative physics changes merely to satisfy the score.

**Human gate:** per `docs/critique.md`, “After presenting any formal critique, STOP and wait for explicit human instruction.” No fixes, corrective issues or further critique round were started. Human acceptance or explicit corrective-work authorization is required next.
