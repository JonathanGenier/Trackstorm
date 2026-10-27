# Astra Runtime Critique — Story Round 3

**Story:** TS-234 — Machine Gun Firing Animation & Weapon VFX
**Date:** 2026-10-12
**Branch:** `ts-234-jg`
**Overall Score: 7.6 / 10**
**Quality Assessment: FAIL (<8.0)**

The second sizing request is satisfied in the observed result: the gun is clearly larger and the barrels substantially longer. The side and rear silhouettes now communicate a heavier mounted weapon, while the central rack/drum envelope remains compact. Inspected poses show no obvious new Car/rack penetration, and muzzle effects remain at the extended barrel tips.

The overall Story score stays at 7.6. Stronger size/readability does not remove the existing mechanical-finish concerns; the larger receivers also make their plain faces more prominent. These were not authorized corrections in this sizing round. This assessment does not treat their omission as failure to follow the user's request, and does not turn recommendations into authorization. The score is holistic and the passing threshold remains exactly 8.0.

## Category Scores

| Relevant category | Score | Assessment |
| --- | ---: | --- |
| Visual quality / silhouette / reference faithfulness | 7.2 | More assertive barrel silhouette; broad plain receivers and fanning cartridge feeds still limit finish. |
| Articulation / physical presentation | 8.0 | Forward, side, rear and complete overhead views remain coherent; sampled clearance is clean. |
| Animation / motion | 7.4 | Confirmed feed motion and recovery work; connected-belt appearance remains weak. |
| VFX / sustained-fire readability | 8.0 | Effects follow new tips and stop cleanly; eight scripted guns do not produce overwhelming smoke/flash. |
| Runtime functionality / integration | 7.4 | Focused current checks pass; broader surrounding-system runtime confidence remains incomplete. |

No audio, human-control, network-feel or measured-performance score is inferred from this evidence. Engineering/source quality is outside this review.

## What Was Exercised

- **VERIFIED — independent rendered execution:** reran `check-twin-fang.ps1 -NoBuild -Motion` in Godot 4.7.2 with required AppData/cache access. Exit 0, no warnings/errors. Forward/side/rear/overhead, full yaw/elevation sweep, confirmed belt motion, release, low-FPS flash and eight simultaneous scripted shooters passed. [Reviewer log](ts-234-evidence/size-refinement-2/critique-render.log).
- **VERIFIED — direct current visual inspection:** inspected [forward](ts-234-evidence/size-refinement-2/forward.png), [side](ts-234-evidence/size-refinement-2/side.png), [rear](ts-234-evidence/size-refinement-2/rear.png), [overhead](ts-234-evidence/size-refinement-2/overhead.png), [firing](ts-234-evidence/size-refinement-2/firing-80.png), [released](ts-234-evidence/size-refinement-2/released.png) and [eight-shooter](ts-234-evidence/size-refinement-2/eight-shooters.png) renders. The overhead inspection camera is farther back to include both tips; that view is not evidence of a production-camera change.
- **VERIFIED — fresh temporal samples:** inspected `.godot/ts234-visual/motion-013.png` and `motion-065.png` after the independent replay. Firing and release states show appropriate recovery and clean tips after release. The fixture directly verifies inter-frame cartridge displacement; no continuous human-play claim is made. [Current captured animation](ts-234-evidence/size-refinement-2/firing.gif).
- **Recorded verification, not independently replayed network evidence:** [current verification](ts-234.md) records 1,321 Core plus 462 Client/transport tests passing and clean builds. The [native impaired Machine Gun log](ts-234-evidence/size-refinement-2/native.log), inspected for this review, records release/exhaustion and agreement on 1,237 hits / 2,293.419922 points. **INFERRED:** focused authoritative Machine Gun functionality remains coherent after sizing.

## Remaining Material Problems

### Ammunition feed appearance

- **Problem:** exposed cartridges remain conspicuously fanned, with insufficient visible link continuity and entry guidance to read as a connected ammunition belt.
- **Evidence:** current close firing/released images and fresh motion samples; the approved concept has a more contained, connected feed appearance.
- **Severity:** Medium.
- **Impact:** the animated feed reads decoratively instead of as a convincing mechanical feed, weakening the explicitly requested presentation.
- **Suggested Improvement:** refine round orientation/spacing, link readability and visible receiver entry while preserving confirmed-shot animation and the central clearance route.
- **Scope:** In Scope for TS-234; outside this round's authorized sizing correction.
- **Corrective Work Type:** Existing Task, subject to human direction.

### Receiver finish

- **Problem:** large plain rectangular receiver faces remain visually dominant. Enlargement increases their presence without adding the layered cradle/receiver character of the approved design.
- **Evidence:** current forward and close firing views compared with the approved reference assessed in Round 1.
- **Severity:** Medium.
- **Impact:** limits the production-quality mechanical character despite a stronger overall silhouette.
- **Suggested Improvement:** refine large contours, readable side/cradle plates and restrained material separation; retain the short rear envelope that clears overhead articulation.
- **Scope:** In Scope for TS-234; outside this round's authorized sizing correction.
- **Corrective Work Type:** Existing Task, subject to human direction.

## Runtime / Operational Limitations

The Round 1 aiming crossing-drift, rack cleanup and Missile activation results remain historical unresolved evidence. The broader fixtures were not rerun after synchronization to the current main baseline. This review claims neither current reproduction nor resolution of those failures. Current focused passes do not establish wholly green integrated verification.

No fresh human-driven chase-camera session, real WAN/EOS, separate physical-client test, audio listening, controller-feel review, GPU/frame-time profile, long soak or exhaustive geometric-clearance check was performed. Eight scripted guns are not eight network peers. Current visible muzzle alignment is verified visually; exact authoritative origin/direction is supported indirectly by checks. The actual fixed-rack fit remains an adaptation of the approved reference, not literal 100% reproduction.

## Recommended Next Round

**None: Story Round 3 is the final permitted round under `docs/critique.md`; no Round 4 exists.** The next action is a human disposition: accept the sizing result as-is, defer/reject it, or explicitly direct how remaining recommendations should be handled under the workflow. No further automatic polish or critique is authorized. If separately directed, feed/receiver refinement and current broader integration verification are the meaningful priorities; repairing demonstrated pre-existing surrounding-system defects is an **Out-of-Scope Recommendation** requiring separate authorization.

**STOP — human decision required.** No production changes, corrective issues, commit or push were performed by this reviewer. Earlier critique reports remain unchanged.
