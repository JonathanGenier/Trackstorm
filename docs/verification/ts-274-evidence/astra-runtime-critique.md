# Astra Runtime Critique — Story Round 1

**TS-274 — Overall Score: 8.1 / 10**
**Quality Assessment: PASS (>=8.0)**

Independent runtime assessment after final verification on `ts-274-pg`. The exercised collision behavior is predictable, stable under sustained pressure, and recoverable by reversing. The collision optimization materially improves the demonstrated bank-contact processing problem without an observed loss of impact consequences. This score evaluates the scoped runtime result; it does not certify that the user's exact original trapping episode has been reproduced and eliminated.

## Category Scores

| Relevant category | Score | Runtime basis |
| --- | ---: | --- |
| Runtime Functionality | 8.2 | Bank, pillar, perimeter and authored rock contacts register; repeated low-speed bank impacts and recovery work. |
| Controls / Responsiveness | 8.3 | Brake/reverse consistently leaves the initial bank contact; sustained steering does not induce a recurring shake in the exercised bank cases. |
| Vehicle / Movement Feel and Physics | 8.2 | Impact speed loss and damage remain, small settling motion is bounded, and reverse retains motion into a subsequent obstacle rather than bypassing collision. |
| Multiplayer / Networking Experience | 8.0 | Independently executed two-process traffic check passes, with no hard snaps or prediction holds. Rendered remote collision smoothness was not directly watched. |
| Observed Performance | 7.5 | Recorded original-versus-baked contact timings show a substantial improvement. Whole-client timing still has outliers; the independent headless network run had a 749 ms early frame. |
| Runtime Stability | 8.7 | No runtime errors in independent rendered sessions; both independently launched network processes complete successfully. |
| Runtime Integration | 8.1 | Actual map contacts, damage, destruction and recovery remain coherent; broader adapter/rock/terrain coverage is supported by inspected verification evidence. |

Overall is a scope-weighted experiential judgment, not a test-count score or an arithmetic promise that every category passes individually. Camera, art, audio and general game fun are not separately scored: the fixture's fixed inspection camera and controlled inputs cannot establish those wider qualities.

## What Was Exercised

**VERIFIED — independently launched and exercised rendered runtime.** Used the actual-map handling scene with `--world-collision-playtest --handling-network`, commanded bounded throttle/steering/brake segments through its existing protocol, inspected per-tick motion, and viewed six rendered captures covering bank pressure, angled impact, low-speed escape, pillar, perimeter and rock. This is input/observe playtesting, not continuous human keyboard/controller play.

- From inside the tunnel at 12 m/s, collided with the steep dirt face and then held full throttle/steering for **600 ticks**. All 600 ticks reported contact. Y stayed between **1.1555 and 1.2153 m**, peak commanded vertical speed was **0.1312 m/s**, and largest position step was **1.741 mm**. HP stayed **948.864** after the initial impact. No repeated jumping or violent correction appeared in this trace. Reverse/steering cleared to approximately **(-0.013, 1.142, 11.028)** with no final contact. [Pressure capture](astra-bank-pressure.png).
- A **35 m/s angled** approach registered 158 contact frames in 180 ticks, settled near **(6.390, 1.186, -2.398)** and retained impact damage to **900 HP**. Reverse crossed the tunnel and struck the opposite bank, producing a short rise to **1.773 m** and peak vertical speed **3.526 m/s**. This is a subsequent impact, not evidence of persistent entrapment or repeated oscillation.
- Two **3 m/s** approaches, reset to the same close approach, produced the same contact endpoint and HP (**995.349**). Both reversed more than **3 m** clear of the contact region. An intervening 180-tick full-steering pressure segment remained stable. [Escape capture](astra-low-reverse.png).
- **35 m/s pillar** impact stopped with **900 HP**. Reverse left the pillar and then contacted the opposite tunnel side, with further damage. **35 m/s perimeter** impact stopped with **900 HP**; reverse cleared from Z about **97.36 to 81.79**.
- **12 m/s authored rock** approach registered contact and damage, then continued through normal destructible behavior; subsequent reverse was free of contact. This complements the retained intact-hull suite rather than replacing it.
- Preserved independent summaries: [first session](astra-playtest-summary.log), [remaining objects](astra-second-summary.log), [corrected low-speed repeats](astra-low-summary.log), [motion measurements](astra-motion-metrics.json). Raw traces/captures remain in ignored `.godot/ts-274/astra-playtest-results`, `astra-second-results` and `astra-low-results`.

**VERIFIED — independently executed two-process integration.** Ran `check-network-vehicles.ps1 -Players 2 -NoBuild` against the installed Godot .NET executable. Both native processes completed with bidirectional application traffic and camera-input fixture checks. Client recorded **333 snapshots**, **0 hard snaps**, **0 prediction-limited frames**, steady correction p99 **0.2160 m**, and maximum **0.5806 m**. No readiness timeout occurred. [Run log](astra-network.log), [client metrics](astra-network-client.json).

The same run recorded frame p95 **23.966 ms**, p99 **49.470 ms**, maximum **749.320 ms**, with the largest frame at scenario time **0.067 s**. Simulation-step p95 was **6.657 ms**, maximum **73.745 ms**. Thus the passing network verdict does not mean uniform frame pacing. The early outlier's cause is unestablished; it is not attributed to a production networking or terrain defect by this critique.

**VERIFIED — inspected existing operational evidence, not independently rerun.** Reviewed original and baked impaired-UDP bank logs: the original medium-speed steering case failed its contact-cost gate (mean **16.389 ms**, p95 **52.592 ms**); baked geometry completed all three speeds (medium mean **6.313 ms**, p95 **18.727 ms**). The final report and 128-case matrix supply broader speeds, orientations, targets, both adapters and repeated-contact coverage; established intact-rock, terrain, crash and native transport results complement the independently exercised cases. I do not claim to have personally played every matrix case.

**INFERRED.** Lower native sweep cost should reduce the apparent frozen/unstable experience during bank pressure. The operational comparison supports that performance mechanism, but cannot establish that it was the sole cause of the reported physical trapping.

## Runtime / Operational Limitations

- **UNVERIFIED:** the exact original persistent physical trap/jumping episode. Neither the implementation baseline reproduction nor this critique establishes its precise triggering sequence. The PASS applies to the resulting behavior actually exercised, with this residual acceptance uncertainty explicitly retained.
- Independent rendered tests used the hosted collision adapter; practice-path breadth relies on the existing final matrix. Input commands are scripted, captures are discrete, audio was not listened to, and no physical-controller ergonomics or full frontend match playthrough was evaluated.
- The first independently attempted low-speed full-steering approach turned away and had **zero contacts**. It is not collision evidence. A closer straight low-speed approach subsequently produced the repeated contacts reported above. The inherited medium-speed recontact command also exited toward the portal without contact.
- A Windows command-file `Move-Item` race interrupted the first session after angled reverse. Its own Godot process was stopped in cleanup; remaining segments were run in separate sessions using a replacement file handoff. No game runtime error was recorded. Interrupted segments are not represented as completed.
- Native UDP bank coverage uses two worlds in one process; the independent two-process run covers application vehicle traffic but does not independently reproduce the exact tunnel collision on remote rendered clients. No Internet/EOS, remote-machine, exported-build or long-duration soak assessment was performed.
- Final verification includes two documented failures: stale map inventory expectations in destructible-environment and map-budget checks. These are not passes. Evidence identifies unchanged dressing/inventory expectations rather than an observed new collision malfunction; no complete green repository/runtime verification claim is made here.
- Original/baked performance measurements are host-dependent and were inspected from prior final-verification evidence. This critique did not rerun native profiling or infer CPU cost from the rendered position traces.
- This is a runtime assessment, not an independent engineering/PR review. No production or test sources were changed and no corrective work was implemented.

Formal critique is complete. Any implementation improvement or another critique round requires the human's explicit authorization under `docs/critique.md`.
