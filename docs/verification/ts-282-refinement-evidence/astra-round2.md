# Astra Runtime Critique — Story Round 2

TS-282, `ts-282-pg`, 2026-10-07. Fresh independent critique of the authorized refinement after completed final verification. Jira TS-282 was read directly in full: natural inertia before aerial input, chosen-orientation hold after all aerial input is released, and restoration of crash rotation after non-wheel contact. No children or comments were present. The previous round's score and release-keeps-spinning requirement are historical and do not determine this verdict.

**Overall Score: 8.6 / 10**

**Quality Assessment: PASS (>=8.0)**

## Category Scores

| Category | Score | Runtime judgment |
| --- | --- | --- |
| Runtime Functionality | 8.8 | The three states and their transitions behaved correctly in independently operated trials, including repeated re-input and partial-axis release. |
| Controls / Responsiveness | 8.8 | First-step input starts rotation; first-neutral-step release arrests it. Re-input immediately resumes deliberate control. Alignment followed by release is straightforward. |
| Vehicle / Movement Feel | 8.6 | The selected pose stays stable while the ballistic trajectory continues. Release is a decisive arcade stop, matching the requested forgiving behavior. Contact restores meaningful, damaging tumble motion. |
| Gameplay / Fun | 8.4 | The observed align/release/land sequence provides useful control without making a bad selected pose automatically safe. Continuous human enjoyment and learning remain unverified. |
| Physics | 8.7 | Natural inherited spin remains exact before control. Held roof and side landings regain substantial spin on the first crash-latched contact step, then recover through existing behavior. No stuck hold or instability appeared. |
| Animation / Motion | 8.5 | Rendered poses and traces agree: held tilt stays tilted, the inverted vehicle stays inverted until contact, and successful alignment lands upright. This evaluates vehicle motion, not general art or animation production. |
| Runtime Stability | 8.5 | All independent valid segments completed without feature exceptions, invalid states or loss of control. Environmental warnings are separated below. |
| Runtime Integration | 8.5 | Native synthetic input, airborne control, support/contact transitions, damage and recovery worked together. Inspected current native/network logs support the broader integrations without replacing direct playtest observations. |

The overall score is a qualitative assessment of the exercised refinement, not a test-count reward or arithmetic average. No meaningful in-scope runtime correction was exposed. The immediate hold is intentionally more forgiving than conserved player-created spin; it did not turn into automatic righting or suppress the independently observed crashes.

## What Was Exercised

**VERIFIED — independently operated:** Installed Godot 4.7.2 .NET, rendered production-practice adapter through `handling_playtest.tscn -- --inertia-playtest --handling-flat`, production defaults. Own process PID 26008 was stopped after the trials. Commands were bounded native synthetic keyboard/analog input, with subsequent commands selected from observed results. Evidence filenames below live under `.godot/ts-282/evidence/` and use the `r2-astra-` prefix; JSON contains actual per-tick traces and matching PNGs are rendered captures.

- **Natural inertia:** `r2-astra-natural.json`, 35 neutral frames from a 40 m launch. Angular velocity **(2, -1, 3) rad/s** remained exact. Horizontal velocity stayed **12 m/s**; vertical velocity evolved under gravity. Airborne neutrality alone did not initiate hold.
- **Immediate control:** `r2-astra-roll.json`, 22 keyboard-roll frames. First sampled angular speed was **0.08084496 rad/s**, growing to **3.2328134 rad/s** with no contacts. Uprightness reached **0.72631**.
- **Release and hold:** `r2-astra-hold.json`, 35 neutral frames. Angular speed was **zero from the first frame through the last**; uprightness remained **0.72631**. Z position changed from the previous segment's -4.20 m to -11.00 m while gravity continued changing vertical velocity. The directly inspected `r2-astra-hold.png` shows the chosen tilted pose, not a world-up correction.
- **Re-input, alignment and landing:** `r2-astra-align.json`, 22 opposite-roll frames, brought uprightness to **1.0**. `r2-astra-land.json`, 180 neutral frames, arrested spin immediately and then landed with **1000 HP**, uprightness **1.0** and normal ground motion. The actual landing PNG was directly inspected. No second counter-pulse was necessary to prevent continued aerial rotation after release.
- **Held bad pose into roof crash:** `r2-astra-roof-input.json` used 10 synthetic analog pitch frames from a deliberately inverted launch. `r2-astra-roof-hold.json` then retained uprightness **-0.95724064**, exactly zero angular velocity and continuing descent/travel over 15 neutral frames. Its PNG was directly inspected and clearly shows the inverted truck. `r2-astra-roof-impact.json` continued for 180 neutral frames. On the first crash-latched contact step, tick **591**, crash age **0.016666668 s**, angular velocity was **(2.9677992, -1.2026494, 2.0970943) rad/s**, magnitude **3.82779 rad/s**. Thus collision rotation was already present before delayed recovery could explain it. The truck eventually regained tire support with uprightness **0.9994457** and **933.6667 HP**.
- **Mixed axes and partial release:** `r2-astra-mixed.json` applied analog pitch and yaw together for 20 frames. `r2-astra-partial-release.json` released pitch while maintaining yaw for 20 more frames; angular speed continued at roughly **1.36 rad/s**, and uprightness changed from **0.9779301** to **0.8612074**. Releasing one axis did not prematurely freeze the maneuver. `r2-astra-all-release.json` then released all axes for 40 frames: exactly zero angular velocity and unchanged uprightness **0.8612074**, with continued linear travel and descent.
- **Held side pose into crash:** `r2-astra-side-input.json` applied 12 analog pitch frames from a near-sideways launch. `r2-astra-side-hold.json` held uprightness **-0.029157788** and zero spin for 20 neutral frames. `r2-astra-side-impact.json` continued 220 neutral frames. At first crash-latched contact, tick **907**, crash age **0.016666668 s**, angular velocity was **(-0.5562354, 1.4042374, -2.527497) rad/s**, magnitude **2.94441 rad/s**. The truck recovered onto tires with uprightness approximately **1.0**, **912.08344 HP** and no active crash timer. Hold did not cancel impact motion.

**VERIFIED — inspected current completed evidence, not independently rerun in this critique:** `refinement-final-check-air-control.ps1.log` records 46 native production-adapter cases, released keyboard/analog axes, continued linear travel, and held wheels/roof/side/nose contact transitions. Its network held-contact cases record roof/side/nose post-contact peaks **2.418 / 2.310 / 3.503 rad/s**. `refinement-final-check-network-vehicles.ps1.log` records host/client integration, camera checks, client steady correction maximum **0.02894 m** and **zero snaps**. `refinement-final-check-reconnect.ps1.log` records three resyncs including **125 seconds offline**, state continuity and native body reuse over real UDP. The current verification report records all 18 completed runtime gates and broader real-map, collision and multi-vehicle coverage.

**INFERRED:** The directly observed state transitions plus completed runtime integrations support coherent behavior beyond the isolated practice fixture. They do not prove visually imperceptible reconciliation during every collision; current impaired collision verification reports a maximum contact correction of approximately **1.4533 m**. That aggregate result does not by itself establish a new hold-control defect.

## Runtime / Operational Limitations

- This is segmented agent playtesting with actual native simulation, per-tick traces and inspected rendered captures. Continuous human trick-driving feel, rapid physical finger timing and long-session enjoyment are **UNVERIFIED**.
- No physical gamepad was operated. Analog trials use native synthetic device events; controller ergonomics and noisy real hardware remain **UNVERIFIED**.
- Independently operated trials used the isolated flat fixture and one vehicle. Real-map approaches, bumper/nose impacts, multi-vehicle collisions, network and reconnect breadth were covered by inspected completed verification evidence rather than fresh independent sessions here.
- Internet/authenticated EOS, second-PC and exported-build acceptance are **UNVERIFIED**. The local networking harness uses explicit test seams and does not establish those outcomes.
- The fixture has an observation camera. Production chase-camera feel during continuous tricks was not independently observed. Audio was not listened to. No general UI, art, audio, camera or performance score is assigned. Bounded screenshots/traces are not a continuous frame-time benchmark.
- `r2-astra-runtime.err` contains sandbox-environment warnings opening the user log and reading the system certificate store. It contains no feature exception from these trials. Rendering and command execution completed normally; those environment warnings are not attributed to TS-282 handling.
- Contact impulses and existing supported damping/recovery legitimately dissipate motion; preservation of natural inertia is not a claim of energy conservation through collision.

No corrective round or implementation change is recommended on this evidence. This reviewer stops here and returns the critique to the parent for the already-authorized delivery workflow. No production/test edit, commit, push, PR mutation, Jira mutation or merge was performed by this reviewer.
