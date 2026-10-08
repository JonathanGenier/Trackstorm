# Astra Runtime Critique — Story Round 2

**Story:** TS-283, corrective result on `ts-283-pg`

**Overall Score: 8.2 / 10**

**Quality Assessment: PASS (>=8.0)**

The corrected result preserves believable world-contact motion while another vehicle is touching the car. The rendered barrier pair remains constrained beside the wall, with measured tangential slowdown and angular response retained. The tipped terrain pair keeps its impact-generated pitch motion and then recovers. Ordinary rear-quarter contact still gives a readable, recoverable yaw kick; routine rubbing remains stable. Aerial release retains its short progressive settling tail, renewed input remains effective, and deliberate landings remain predictable in the exercised sequences.

This is an independent runtime judgment of the current result, not a replacement for the engineering review. Round 1 did not exercise simultaneous world/vehicle contact; its passing score did not establish correctness of that missing scenario. Round 2 directly covers that interaction. The score is holistic, not a rounded category average or a reward for additional tests.

## Category Scores

| Material runtime category | Score | Observed judgment |
| --- | --- | --- |
| Mixed vehicle/world physics | 8.4 | World slowdown and angular motion survive the combined contact; wall constraint does not turn into an artificial PIT spin. |
| PIT movement / impact quality | 8.0 | Distinct rear-quarter disruption with recovery and stronger peak response for stronger hits; restrained rather than dramatic. |
| Repeated-contact stability | 8.3 | Native sustained rubbing/following and impaired multi-world following remained continuous in these runs. |
| Aerial movement / control | 8.5 | Natural inertia, progressive release, renewed input and full-health landings remain coherent. |
| Multiplayer / runtime integration | 7.7 | Current UDP runs converge, with measurable correction spikes and known adapter differences still limiting polish. |
| Runtime stability | 8.1 | No failures or engine warnings in fresh critique runs; broader recorded fixture/performance sensitivity remains a limitation. |

## What Was Exercised

All commands below used the current completed Debug binaries, Godot 4.7.2 .NET console, and `-NoBuild` where supported. Runtime checks were executed sequentially. No implementation, tuning, tracked tests or tooling were changed by this reviewer.

### Mixed contact — directly VERIFIED

Ran `check-mixed-contacts.ps1 -Visual -NoBuild`, all four cases passed. Inspected the fresh barrier first/20th-boundary and terrain-impact captures, plus per-tick raw/captured-state traces. Each case ran 300 physics frames.

| Case | Mixed boundaries | World-contact boundaries | Maximum tangential loss | Maximum angular change | Batch error |
| --- | ---: | ---: | ---: | ---: | ---: |
| Barrier push | 300 | 300 | 0.491340 m/s | 0.177313 rad/s | 0 |
| Barrier touch | 299 | 299 | 0.002749 m/s | 0.001265 rad/s | 0 |
| Terrain touch | 3 | 3 | 0.101708 m/s | 0.575507 rad/s | 0 |
| Environment-only control | 0 | 60 | 0.080555 m/s | 0.066808 rad/s | 0 |

The batch comparison checks the complete native result followed by the existing residual pair solve at the same boundary. Low-closing touching occurred in both touch cases. In terrain trace frame 6, vehicle 1 retained native pitch velocity **2.4687343 rad/s** and forward velocity **-14.552345 m/s** exactly in the captured batch; the partner retained its corresponding response too. This directly addresses the earlier lost-torque/slowdown symptom.

The barrier pair stayed alongside the wall in the sampled renders, without visible launch or penetration. Push-case peak target yaw was only **0.0102 rad/s**. That bounded response is appropriate for the constrained geometry, not evidence that every wall contact must exhibit the open-space PIT effect. The tipped terrain pair retained physical pitch motion and ended upright in the trace. Tangential losses above are boundary measurements, not a claim that the total five-second speed reduction is exclusively wall friction.

Evidence: `.godot/ts-283/round2/astra-mixed.log`; preserved current captures/traces in `.godot/ts-283/round2/astra-mixed/`.

### PIT and repeated contact — directly VERIFIED

Ran `check-pit-collisions.ps1 -Visual -NoBuild`: all **18** scenarios passed across native and swept adapters. Exercised low/medium/high and glancing rear-quarter contact, matched speed, rubbing, following, heavy target and heavy striker. Inspected fresh native/swept high-contact captures, native recovery and sustained-rubbing captures; examined traces and damage outcomes.

- Native target peak yaw, low/medium/high: **0.1103 / 0.9381 / 1.5345 rad/s**. Swept: **0.0424 / 1.0441 / 1.5671 rad/s**. These match the earlier pure-pair behavior.
- High net yaw travel: native **0.1742 rad**, swept **0.2052 rad** (about 10.0 / 11.8 degrees). Tire paths show a curved line followed by recovery. This remains a recoverable swerve, not a guaranteed full spin.
- Native rubbing: **1,800** contact observations, peak yaw **0.0040 rad/s**. Native following: **1,440** contacts, peak yaw **0.0104**, maximum contact travel error **0.0003 m**. Swept following: **618** contacts, peak yaw **0.0010**, travel error **0.0036 m**. No renewed repeated-contact instability appeared in these fixtures.
- Swept matched/rubbing contact lasted only two observations before separation. It does not establish sustained swept side pushing equivalent to the native case.

Evidence: `.godot/ts-283/round2/astra-pit.log` and preserved `.godot/ts-283/round2/astra-pit/`.

### Aerial regression — directly VERIFIED

Ran the rendered synthetic-keyboard driver `.godot/ts-283/round2/astra-run-settle.ps1` through both production adapters. Exercised natural jump, pitch/yaw corrections and releases, barrel roll and early/late release, renewed opposite yaw input after settling, a second release, 480-frame descent, and a separate deliberate landing. Inspected fresh early/late roll captures, native landing and swept long-descent landing.

- Untouched spin remained **3.741657 rad/s** for 60 observed frames.
- Pitch release: **2.4684** before release, **2.0895** on the first neutral step, approximately **0.000112 rad/s** after 60 neutral steps.
- Roll release: **3.24** before release, **2.7426** first neutral step, **1.1919** after six, approximately **0.000054** after the next 60. Rendered samples retain visible orientation continuation before settling.
- Renewed input reached approximately **1.2169 rad/s** after eight frames and settled again. Both adapters finished the long descent and separate landing grounded, upright and at **1000 HP**.

Evidence: `.godot/ts-283/round2/astra-settle.log`; segment inputs, traces, summaries and captures in `.godot/ts-283/round2/astra-settle/`. This round did not independently rerun all 48 aerial-impact cases; their completed final-gate results are implementation evidence.

### Multiplayer integration — directly VERIFIED

- `check-environment-collision-network.ps1 -NoBuild`: real two-peer local UDP with **30 ms delay / 5 ms jitter / 2% loss**. Scraping, crash/head-on, inverted recovery and three alternating-target PIT rounds passed. PIT peak yaw **1.0917 rad/s**; maximum corrections **0.1750 / 0.3053 / 0.1759 m**; authoritative per-vehicle damage agreed and positions converged. The separate head-on phase reached **1.4533 m** correction. This is not a claim of invisible reconciliation.
- `check-following-contact-network.ps1 -Visual -NoBuild`: four UDP worlds, two following pairs, same impairment. **528 close-following frames; zero stopped/backward steps; correction p95 0.030151 m; maximum 0.140750 m; minimum visual gap 5.329 m**. Inspected the rendered following capture and preserved its trace.

Evidence: `.godot/ts-283/round2/astra-udp-pit.log`, `astra-following.log`, `astra-following.json`, `astra-following.png`.

## Runtime / Operational Limitations

- **VERIFIED, retained adapter difference:** high PIT again ended with native target/striker approximately **951.33 / 951.33 HP**, swept **1000 / 950 HP**. Physical response occurs for both participants, but damage is not equivalent between adapters. Peer agreement establishes authoritative consistency, not equal participant damage. A main-baseline runtime comparison remains **UNVERIFIED**, so this is not classified as a newly introduced regression.
- The earlier following run's near-stopped predicted step during a **0.58484 m** correction remains credible recorded evidence of intermittent schedule sensitivity. Fresh passing runs do not erase it. This reviewer did not directly observe its on-screen perceptibility.
- The completed corrective verification recorded repeated-rock fixture GCHandle errors and contact-step means above the unchanged 8 ms budget before its final passing run and cleanup. Those historical failures remain disclosed. They did not occur in this review's executions, but this critique did not repeat that full rock suite or perform sustained performance profiling; it does not establish that every load schedule is free of stalls.
- **UNVERIFIED directly in critique:** larger native mixed-contact chains, saturated contact reports and accepted/queued-effect corner cases. Their new deterministic coverage and final verification are reported implementation evidence, not fresh experiential observations. The four native mixed fixtures cover two-car barrier/terrain contact and a world-only control, not every movable obstacle or competitive pile-up.
- Actual production Van geometry and Car/Van or Van/Car play remain **UNVERIFIED**. Heavy mass/wheelbase fixtures use the Car hull.
- Observation used scripted synthetic input, native execution, sampled rendered images and per-tick traces. It was not human free-driving, continuous human video observation or physical-controller play. Audio, full-race enjoyment and device ergonomics were not scored.
- UDP used local worlds with simulated impairment, not remote physical machines or authenticated EOS. Reconnect, rock UDP and broader final gates were read in the verification report but were not independently rerun for this critique.
- Native and swept adapters do not produce identical trajectories. The current evidence supports the exercised behavior, not universal equivalence.

The formal Round 2 critique is complete. No corrective implementation, tuning, Jira/PR change or further polish round was performed. Stop for explicit human direction before additional improvement work, as required by `docs/critique.md` and the current request. This verdict does not authorize merging.
