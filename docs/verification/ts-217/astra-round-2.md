# Astra Runtime Critique — Story Round 2

TS-217 — Selection-driven Shield rear shield. Independent Astra review, 2026-10-01, after corrected implementation verification on `ts-217-jg` (Story version 0.2.18; main baseline 27d274e). This round assesses the approved selection/stow interaction and normal-loot availability. Round 1's first-use deployment and weight-zero assumptions are superseded; its score and runs are not evidence of acceptance for this correction.

**Overall Score: 8.1 / 10**

**Quality Assessment: PASS (>=8.0)**

The independently exercised corrected behavior is coherent: selection exposes the plate, switching away removes physical cover, and returning restores the damaged pool without refilling it. Repeated selection cycles converge on both peers. Selecting Machine Gun visibly leaves that car unshielded while its target retains rear protection. Covered shots and rear collision damage the shield alone; exposed angles damage the car, and destruction leaves the car vulnerable even after subsequent selection cycles. Actual reconnect preserves the damaged rear shield. These results support a passing assessment of the scoped prototype.

Confidence is narrower for the surrounding game than for the shield itself. The broad native suite has unresolved failures also reported on unchanged main; those failures remain failures and limit integration assurance. Default-loot coverage is supported by current implementation evidence, but this reviewer did not independently complete a normal unmodified-loot driving session. The score reflects these limits and the controlled nature of the observations. It is not increased for the correction effort, test count or critique-round number.

## Category Scores

| Relevant runtime category | Score | Basis |
| --- | --- | --- |
| Runtime Functionality | 8.5 / 10 | Reversible exposure, preserved damaged pools, no-use activation, rejected shield use, finite protection and persistent destruction all completed in the current native run. |
| Physics | 8.0 / 10 | Native rear/quarter rays and a rear vehicle collision obeyed finite plate coverage and shield-only intercepted damage. Controlled flat-ground cases establish a useful but limited physical envelope. |
| Multiplayer / Networking Experience | 8.3 / 10 | Select/stow and damage states converged under local UDP impairment; late admission and three actual resumes preserved persistent state. This score concerns observed operational consistency, not subjective Internet latency. |
| Runtime Stability | 8.3 / 10 | The three independently executed shield/lifecycle/reconnect runs exited successfully without logged warnings or errors. This does not erase shutdown warnings or failures elsewhere in the supplied native evidence. |
| Runtime Integration | 8.0 / 10 | Selection, another equipped weapon, damage, native bodies and recovery cooperate in the exercised paths. Broader pickup/weapon failures and unobserved shield-specific weapon combinations constrain confidence. |
| Feature-Specific Operational Quality | 8.0 / 10 | Sampled rendered states clearly distinguish stored armor, selected rear cover and destroyed absence, with readable finite extent. Temporary art is judged only for this purpose. |

The overall score is a holistic scoped assessment, not a rounded arithmetic average. Manual controls/ergonomics, game feel, chase camera, audio, continuous animation smoothness and measured performance are not scored because they were not meaningfully observed in this review. Final models/animation/VFX, balance and TS-218 world-wall input are outside this Story's approved scope.

## What Was Exercised

- **VERIFIED — fresh independent rendered execution:** `check-rear-shield.ps1 -NoBuild -Visual -Impaired` on Godot 4.7.2 Mono, Windows, OpenGL/NVIDIA RTX 4070 Ti. It ran two real local-UDP peers with 30 ms outbound delay, 5 ms jitter and 2% loss. The harness grants the items and controls poses/firing settings; it is not an unrestricted human play session. [Round 2 shield log](astra-round-2-rear-shield.log).
- **VERIFIED — selection replaces first-use deployment:** before each of eight scenarios, both peers performed three ordinary driver selection stow/reselect cycles without an activation-use press. Each cycle checked exact damaged pool preservation and native cover state on both peers. The shooter then selected Machine Gun, losing rear cover; the target retained its selected shield. After each impact scenario the target was stowed and reselected, preserving damaged state or destroyed absence. The native harness also invoked the authoritative use request against the selected Shield and confirmed rejection; this is not a claim of manually pressing a controller button.
- **VERIFIED — damage outcomes:** rear, moving rear and inner-quarter fire each changed shield 900 to 810 with vehicle remaining 1000. Outer-quarter fire left shield 900 and changed vehicle 1000 to 910; side/front reduced it to 820/730 while shield HP stayed unchanged. Moving sustained fire exhausted a 10-HP shield, then reduced the exposed vehicle 730 to 651.25. A fresh shield took rear collision damage 900 to 823.21387 with chassis HP unchanged. The other player's stored pool remained 900, and replicated pools/collider presence converged.
- **VERIFIED — current images inspected:** this review opened fresh stored, selected, moving-fire, outer-quarter, destruction and rear-collision captures from its own run. [Stored](astra-round-2-stored.png) shows both rear plates absent; [selected](astra-round-2-selected.png) shows only the target covered while the shooter presents Machine Gun; [destroyed](astra-round-2-destroyed.png) shows the exposed target after lethal shield damage. Still images establish sampled presentation and spatial correspondence, not continuous motion quality.
- **VERIFIED — independent lifecycle/late admission:** `check-shield.ps1 -NoBuild -Impaired` completed with four independent pools, repeated damage/transition attempts, retained rear slots and pools, full authority/checkpoint restoration and a third peer joining. Twenty repeated destruction attempts produced exactly two removals. Those shared-fixture lethal cases are world/held pools; rear destruction was directly exercised separately above. [Round 2 lifecycle log](astra-round-2-lifecycle.log).
- **VERIFIED — independent actual reconnect:** `check-reconnect.ps1 -NoBuild -Shield` completed three real local-UDP arena resumes including 125 seconds offline, approximately 140 seconds total. Each restored rear shield/legacy wall at 600/700 HP with exact attachment/pose and damage watermark and no refill. Native body reuse, prediction/interpolation reset and surrounding item/match continuity checks passed. It uses authenticated test identity rather than production EOS. Its pickup history also recorded a Shield, but that observation alone does not prove every default-loot configuration. [Round 2 reconnect log](astra-round-2-reconnect.log).
- **INFERRED — complete ordinary default-loot/configuration coverage:** the current [verification report](../ts-217.md) and [loot log](selection-loot.log) report successful default-weight and saved-settings acquisition coverage. Current production pickup logs show both peers acquiring Shield before the later full-inventory failure. These support availability, but this reviewer did not rerun the full default-loot/configuration suite or complete a normal weighted-loot match. No Round 1 acquisition assumption is reused.
- **INFERRED — additional detailed invariants and weapon combinations:** supplied current tests support capability/slot retention, two Shields in one vehicle, stored vulnerability, cooldown/watermark behavior and projectile paths beyond the Machine Gun/native-contact scenarios independently run here. They are supporting evidence, not a claim of visually playing every combination.

## Runtime / Operational Limitations

The broad native suite is **not wholly green**. This reviewer read the paired Story/main logs and observed the matching recorded failures; the isolated main executions themselves were performed by the implementation agent, not rerun during this critique:

- Movable-prop explosion motion assertion: [Story](selection-items.log), [main](main-baseline-item_checks.log).
- Salvo forward-aim marker assertion during a volley: [Story](selection-salvo.log), [main](main-baseline-salvo_checks.log).
- Mine native knockback motion assertion: [Story](selection-mine.log), [main](main-baseline-mine.log).
- Production pickup inventory trial fails with no matching inventory record after both peers' catalog acquisitions: [Story](selection-pickup.log), [main](main-baseline-pickup.log).
- Pickup-motion guard rejects the same crossing at driver 0, 8 m/s, offset 2.6729002 m, nearest approach 3.0505 m: [Story](selection-motion.log), [main](main-baseline-motion.log). No 180/180 pickup pass is claimed for the correction.

Matching baseline failures support the conclusion that these problems exist independently of the correction; they do not prove every surrounding interaction is regression-free. Some failed-run logs also contain shutdown resource diagnostics. The first rack run likewise reportedly completed assertions with shutdown warnings, followed by a clean verbose rerun and consecutive-mine pass. Those rack runs were not independently repeated here. None of these failed/warning runs is relabeled as a clean pass, and this critique does not diagnose or repair their root causes.

Additional **UNVERIFIED** areas:

- Human-controlled gameplay, physical input ergonomics, selection timing as perceived by a player, normal chase-camera visibility, listened audio, continuous motion/transition smoothness, measured frame times and long-session load.
- Internet/real authenticated EOS, independent machines, cross-platform physics and actual host succession with a selected rear shield. Direct checkpoint restoration and local reconnect are narrower evidence; the supplied migration run's legacy wall fixture does not establish rear-shield host succession.
- Shield-specific native Missile/Salvo/mine combinations, exhaustive grazing trajectories, uneven terrain, banks, rollovers and unrestricted high-speed contact behavior. Stationary fixture cases reposition vehicles; moving cases use short controlled throttle intervals.
- A complete clean production pickup/inventory run under the current environment. Current default-loot evidence is useful but does not remove the documented native pickup failures.

No material defect in the corrected selection/shield behavior was exposed by this review's executions or captures. No corrective recommendations or further round are proposed for this passing scoped assessment. This does not represent acceptance of the unrelated failing native paths or final presentation quality.

**Human gate:** `docs/critique.md` requires stopping after the formal critique and awaiting explicit human instruction. No implementation changes, commit or push were made during this review. PASS does not authorize delivery, another critique round or further implementation.
