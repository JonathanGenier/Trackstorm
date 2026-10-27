# Astra Runtime Critique — Story Round 2

**Story:** TS-234 — Machine Gun Firing Animation & Weapon VFX
**Date:** 2026-10-12
**Branch:** `ts-234-jg`
**Overall Score: 7.6 / 10**
**Quality Assessment: FAIL (<8.0)**

The approved sizing correction succeeds: the gun is visibly larger and the longer barrels give it a stronger, more aggressive twin-barrel silhouette. The inspected poses retain a compact central mount, and firing effects follow the extended muzzle tips. I found no new obvious Car/rack intersection in the inspected forward, side, rear and overhead views. The overall Story remains below the exact 8.0 threshold because the feed and receiver finish identified in Round 1 remains visible, and current broader integration evidence is incomplete. Those refinements were not authorized by the sizing request; their absence is not a failure to follow this round's instruction.

This assesses the observed integrated presentation, not source quality. The overall score is holistic rather than an average of category scores. [Round 1](ts-234-critique.md) remains historical and unchanged.

## Category Scores

| Relevant category | Score | Assessment |
| --- | ---: | --- |
| Visual quality / silhouette / reference faithfulness | 7.2 | Improved barrel prominence and weapon mass; connected-feed and receiver finish still trail the approved concept. |
| Articulation / physical presentation | 8.0 | Distinct yaw/pitch, overhead reach and sampled clearance remain coherent after enlargement. |
| Animation / motion | 7.4 | Visible feed displacement and recoil/recovery; feeds still look overly fanned. |
| VFX / sustained-fire readability | 8.0 | Flash/smoke stays at the new muzzle tips, release is clean, and eight scripted weapons avoid excessive visual noise. |
| Runtime functionality / integration | 7.4 | Current focused runtime evidence passes; complete surrounding-system behavior has not been re-established on the newer main baseline. |

Audio, human controls, measured performance and subjective network feel are not scored from unavailable evidence.

## What Was Exercised

- **VERIFIED — independent runtime execution:** ran `check-twin-fang.ps1 -NoBuild -Motion` with Godot 4.7.2, using required AppData/cache access. Exit 0 without warnings or errors. Forward/side/rear/overhead, full yaw/elevation sweep, confirmed belt motion, release, low-FPS flash and eight simultaneous scripted shooters passed. [Reviewer log](ts-234-evidence/size-refinement/critique-render.log).
- **VERIFIED — direct image comparison:** compared the [previous forward pose](ts-234-evidence/forward.png) with the [larger forward pose](ts-234-evidence/size-refinement/forward.png). Inspected current [side](ts-234-evidence/size-refinement/side.png), [rear](ts-234-evidence/size-refinement/rear.png), [overhead](ts-234-evidence/size-refinement/overhead.png), [firing](ts-234-evidence/size-refinement/firing-80.png) and [eight-shooter](ts-234-evidence/size-refinement/eight-shooters.png) renders. Barrel extension is clear; the drums and fixed rack retain their previous envelope. This remains an adaptation of the approved reference, not literal 100% reproduction.
- **VERIFIED — fresh rendered temporal samples:** inspected `.godot/ts234-visual/motion-010.png`, `motion-013.png` and `motion-065.png` after the independent run. Feed positions change between firing frames; the release frame has clean muzzle tips without lingering flash/smoke. Frame inspection supports these observations, not a claim of a continuous human play session. [Current animation artifact](ts-234-evidence/size-refinement/firing.gif).
- **VERIFIED — inspected current recorded results:** [comprehensive check](ts-234-evidence/size-refinement/check.log) passes 1,321 Core and 462 Client/transport tests. [Native impaired Machine Gun log](ts-234-evidence/size-refinement/native.log) records release/exhaustion and agreement on 1,237 hits / 2,293.419922 points. These are implementation-run records, not an independently replayed network session. **INFERRED:** focused Machine Gun functionality remains coherent after sizing.

## Remaining Material Problems

### Connected-feed readability

- **Problem:** exposed cartridges still form bright fanning arcs; visible links and entry geometry do not establish a convincing continuous belt into the receiver.
- **Evidence:** current close firing render and fresh motion frames 010/013, compared with the approved concept inspected in Round 1. The belt does animate; this is an appearance finding.
- **Severity:** Medium.
- **Impact:** weakens the requested mechanical rapid-fire character, particularly at close and side views.
- **Suggested Improvement:** refine cartridge orientation/spacing, link continuity and the visible feed-entry guide while preserving the central clearance route and shot-confirmed movement.
- **Scope:** In Scope for the Story, but not authorized by this round's sizing-only request.
- **Corrective Work Type:** Existing Task, only with human authorization.

### Receiver finish

- **Problem:** large plain rectangular rear faces still dominate the receiver; increasing size also increases their prominence. They lack the layered mechanical shape of the approved reference.
- **Evidence:** current forward and firing images. The improvement in barrel length does not change this receiver treatment.
- **Severity:** Medium.
- **Impact:** the weapon reads clearly but does not yet achieve the intended production-quality assembled-metal character.
- **Suggested Improvement:** refine visible receiver contours, cradle/side plates and restrained material separation while retaining the short rear envelope required for overhead articulation.
- **Scope:** In Scope for the Story, but not authorized by this round's sizing-only request.
- **Corrective Work Type:** Existing Task, only with human authorization.

## Runtime / Operational Limitations

Round 1 recorded crossing-target aiming drift, rack exit-cleanup failures and a Missile repeated-activation failure with different baseline behavior. Those fixtures were **not rerun on the current newer main baseline**. They are historical unresolved risks, not verified current failures, and are not claimed fixed or reproduced here. The new focused passes cannot establish a completely green integrated Story.

The enlarged gun has current fixture-distance and close-up evidence, but no new human-driven native chase-camera review. The eight-shooter scene uses scripted confirmations, not eight network peers. Muzzle presentation appears aligned; exact authoritative origin/direction is supported indirectly by current checks rather than independently measured here. No real WAN/EOS, separate physical clients, controller feel, audio listening, GPU/frame-time profiling, long soak or exhaustive geometric-clearance certification was performed. A clean fixture execution does not establish a performance budget.

## Recommended Next Round

The requested size direction is satisfied; no further enlargement is recommended on this evidence. If the human authorizes Story Round 3, prioritize the feed and receiver presentation findings, then review final native chase-camera motion and rerun the relevant aiming/rack/Missile integration on the current baseline. Verification/disposition needs no production change by itself. Repairs to demonstrated pre-existing surrounding-system defects are an **Out-of-Scope Recommendation**, requiring separate authorization.

**STOP — human decision required.** No production changes, corrective issues, commit, push or additional critique round were performed or authorized by this review. The human may accept the sizing result or authorize specific further work.
