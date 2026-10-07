# Astra Runtime Critique — Story Round 1

**Story:** TS-283, branch `ts-283-pg`

**Overall Score: 8.1 / 10**

**Quality Assessment: PASS (>=8.0)**

The exercised result provides a controlled, visibly progressive aerial release and a readable, recoverable rear-quarter disruption. The car continues rotating briefly after release, then becomes dependable through descent; fresh input remains responsive. PIT contact bends the target's line and has consequences for the striker without making ordinary side contact spin-prone. The PIT is deliberately modest: the high-strength fixtures produce a short yaw kick and recovery, not a dramatic spin. That satisfies the requested recoverable destabilization in the exercised setting. The score reflects the observed result, with multiplayer and adapter limitations reducing confidence; it is not based on test volume or implementation effort.

## Category Scores

| Material runtime category | Score | Judgment |
| --- | --- | --- |
| Aerial movement and release feel | 8.5 | Short, continuous settling tail; no abrupt freeze or accumulating late drift in exercised descents. |
| Controls / responsiveness | 8.4 | Pitch/yaw/roll and renewed input after settling respond coherently; deliberate landing remains achievable. |
| PIT movement / impact quality | 8.0 | Readable rear-quarter yaw and lost line, bounded recovery, increasing peak disruption with strength; restrained rather than spectacular. |
| Physics / repeated-contact stability | 8.3 | Stable rubbing and following; impacts restore tumble; no unbounded spin observed. |
| Multiplayer / runtime integration | 7.7 | Current impaired runs converge cleanly, but correction spikes and damage-adapter differences limit polish/confidence. |
| Runtime stability | 8.3 | All review executions completed without engine warnings, exceptions or native shutdown failure. |

Overall is a holistic judgment, not a rounded category average. Audio, general graphics, camera polish and physical-device ergonomics were not scored.

## What Was Exercised

**VERIFIED directly by this independent reviewer, after the implementation's final verification, using existing Debug binaries without rebuilding:**

- `check-pit-collisions.ps1 -Visual -NoBuild`: all 18 scenarios across both production adapters. Low, medium, high and glancing rear-quarter contact; matched speed; 900-frame rubbing and following; heavy target and striker using the Car hull. Reviewed current rendered native/network high-contact captures, native high-recovery and sustained-rubbing captures, and per-tick traces. Log: `.godot/ts-283/astra-pit.log`; traces/captures: `.godot/ts-283/pit/`.
- Target peak yaw, low/medium/high: native **0.1103 / 0.9381 / 1.5345 rad/s**; swept adapter **0.0424 / 1.0441 / 1.5671 rad/s**. High net yaw travel was **0.1742 / 0.2052 rad** (about 10.0 / 11.8 degrees). Rendered tire paths visibly curve and then straighten. The target leaves its original line; the striker also changes course. High striker peak yaw was **0.2425 / 0.2938 rad/s**. Greater impact does not guarantee greater final lateral displacement because contact duration and recovery differ.
- Sustained native rubbing recorded 1,800 contact observations with peak yaw **0.0040 rad/s**; native following 1,440 contacts with peak yaw **0.0104**, maximum contact travel error **0.0003 m**. Swept-adapter following recorded 618 contacts, peak yaw **0.0010**, maximum travel error **0.0036 m**. Swept matched/rubbing contacts separated after only two observations: this is not equivalent evidence of sustained swept side pushing.
- Custom rendered synthetic-keyboard playtest through `handling_playtest.tscn -- --settle-playtest --handling-flat`, both native and swept adapters. Exercised untouched natural jump; pitch correction/release; yaw correction/release; barrel roll and early/late release; an added eight-frame opposite yaw command after settling and another release; 480-frame descent; and a separate wheels-down approach/landing. Driver: `.godot/ts-283/astra-run-settle.ps1`; all segments, traces, captures and summaries: `.godot/ts-283/astra-settle/`; execution log: `.godot/ts-283/astra-settle.log`.
- Natural angular speed stayed **3.741657 rad/s** through all 60 observed frames. Pitch release went from **2.4684** before release to **2.0895** on the first neutral frame and **0.000112 rad/s** after 60. Roll went from **3.24** to **2.7426**, **1.1919** after six frames, then approximately **0.000054** after the next 60. Viewed early/late roll captures show continued rotation before settling. Renewed input reached **1.2169 rad/s** after eight frames and settled again on release. Both long descents and deliberate landing sequences ended grounded, upright, at **1000 HP**. Viewed native landing and swept-adapter long-descent landing captures.
- `check-air-control.ps1 -Visual -NoBuild`: all 48 production-adapter scenarios passed, including synthetic keyboard/pad inputs, repeated landings, sustained roll, correction, roof/side/nose/underside contact after assistance. Reviewed the roof-impact outcome capture and operational trace results. Assistance cancellation was asserted on every non-wheel contact. Post-contact native peaks for roof/side/nose/underside were **5.459 / 5.185 / 6.573 / 1.919 rad/s**; swept adapter **2.175 / 2.319 / 3.672 / 0.022 rad/s**. A centered underside hit need not manufacture a dramatic tumble. Log: `.godot/ts-283/astra-air.log`.
- `check-environment-collision-network.ps1 -NoBuild`: two real local UDP peers under **30 ms delay / 5 ms jitter / 2% loss**, scraping, crash/head-on, inverted recovery and three alternating-target PIT rounds. All passed; PIT peak yaw **1.0917 rad/s**, maximum corrections **0.1961 / 0.3366 / 0.1980 m**, authoritative damage matched across peers and positions converged. The separate head-on phase reached **1.5243 m** correction; invisible reconciliation is not established. Log: `.godot/ts-283/astra-udp-pit.log`.
- `check-following-contact-network.ps1 -Visual -NoBuild`: four UDP worlds, two following pairs, the same impairment. **529 close-following frames, zero stopped/backward steps, correction p95 0.030029 m, maximum 0.056641 m, minimum visual gap 5.326 m**. Inspected the rendered result and preserved the trace/capture as `.godot/ts-283/astra-following.json` and `.png`; log `.godot/ts-283/astra-following.log`.

## Runtime / Operational Limitations

- **VERIFIED adapter difference:** the high PIT fixture ends with native target/striker HP approximately **951.33 / 951.33**, while the swept adapter ends **1000 / 950**. Medium was approximately **994.12 / 994.12** versus **1000 / 990**. Both participants receive physical consequences, but damage is not symmetric between adapters. Real UDP checks establish per-vehicle agreement across peers, not equal damage between participants. **INFERRED:** separate raw contact observations can explain this difference; no main-baseline runtime comparison was performed, so this critique does not classify it as a newly introduced regression.
- The implementation's recorded initial four-world following run had one near-stopped raw predicted step during a **0.58484 m** correction; its unchanged rerun and this independent rendered run passed. The prior failure remains credible evidence of intermittent schedule sensitivity. It is not erased by successful reruns; its on-screen perceptibility was not directly observed by this reviewer.
- **UNVERIFIED:** actual production Van geometry and Car/Van or Van/Car selection. Heavy mass/wheelbase Car-hull fixtures are useful but do not establish Van acceptance. Van is absent from current production integration.
- Observation used controlled synthetic input segments, native execution, sampled rendered images and per-tick traces. It was not human free-driving or continuous human video observation. Physical controllers, tactile ergonomics and subjective enjoyment over a full race remain **UNVERIFIED**.
- Network execution used local UDP worlds under simulated impairment, not remote physical machines or authenticated EOS. Human-versus-human PIT tactics, broad surface/angle exploration, and long competitive sessions remain **UNVERIFIED**.
- Existing final verification reported reconnect and broader integration gates; this reviewer read that evidence but did not independently rerun reconnect, the complete build/test suite, or every unrelated native gate. Those results are implementation evidence, not fresh critique observations.
- Native and swept adapters differ in collision/tumble details; successful cases do not imply identical trajectories. Frame-rate/performance profiling and audio listening were not undertaken.

The formal critique is complete. No implementation changes, tuning changes, corrective issues or additional polish round were made. Stop for explicit human direction before further improvement work, per `docs/critique.md` and the user's request.
