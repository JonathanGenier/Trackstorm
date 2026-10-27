# Astra Runtime Critique — Story Round 1

**Story:** TS-234 — Machine Gun Firing Animation & Weapon VFX
**Branch:** `ts-234-jg`
**Date:** 2026-10-11
**Overall Score: 7.4 / 10**
**Quality Assessment: FAIL (<8.0)**

The weapon is recognizable, articulated and operationally convincing enough to be a substantial replacement for the placeholder. Twin barrels, suspended drums, brass feeds and restrained firing effects establish its identity. It does not yet reach the approved concept's production-quality mechanical finish. The exposed feeds read as fans of individual cartridges, and large plain receiver faces dominate the silhouette. These are visible presentation issues, not requests for invisible detail or new weapon behavior. The score also reflects incomplete integration confidence; a completely green integrated result has not been demonstrated.

This is an independent runtime/experiential critique, not an engineering/source review. The exact passing threshold is 8.0. The overall score is holistic, not an arithmetic average or a reward for test counts.

## Category Scores

| Relevant category | Score | Observed assessment |
| --- | ---: | --- |
| Visual quality / approved-reference faithfulness | 6.8 | Strong recognizable concept; receiver treatment and feed construction remain visibly simplified. |
| Articulation / physical presentation | 8.0 | Cardinal poses, overhead pose and sampled sweep communicate distinct yaw and pitch without obvious Car penetration in inspected views. |
| Animation / motion | 7.4 | Cartridge displacement and recoil/recovery are visible across captured frames; feed appearance weakens the mechanical effect. |
| VFX / sustained-fire readability | 8.0 | Flashes stay at muzzle tips, smoke is restrained, and release frames are clean. The eight-weapon view avoids a screen-filling effect cloud. |
| Runtime functionality / integration | 7.4 | Substantial native Machine Gun evidence, but complete aiming/rack/Missile integration remains non-green. |
| Multiplayer experience, within supplied native evidence | 7.8 | Local impaired peers agree on results and demonstrate remote firing; final feed motion is documented by a scripted fixture rather than final native playback. |

No numerical score is assigned to audio, human controls, vehicle feel or measured performance: evidence does not support those judgments.

## What Was Exercised

- **VERIFIED — direct visual inspection:** compared [ApprovedTwinFang.png](../../assets/items/machine-gun/reference/ApprovedTwinFang.png) with current [forward](ts-234-evidence/forward.png), [side](ts-234-evidence/side.png), [overhead](ts-234-evidence/overhead.png), [firing](ts-234-evidence/firing-80.png) and [eight-shooter](ts-234-evidence/eight-shooters.png) renders. The production rack remains visually separate from the yaw/pitch assembly. Smaller central drums, a concealed feed throat and shortened receiver tails are production-fit adaptations; this is not literal 100% reproduction of the approved drawing.
- **VERIFIED — direct motion-frame inspection:** inspected `.godot/ts234-visual/motion-010.png`, `motion-013.png`, `motion-059.png` and `motion-065.png`. Cartridge positions visibly change between firing frames; receiver/barrel recovery and disappearance of flash/smoke after release are evident. These are rendered temporal samples, not a claim of continuous human play or a listening review. The [captured animation](ts-234-evidence/twin-fang-firing.gif) is retained as delivery evidence.
- **VERIFIED — direct gameplay-image inspection:** inspected `mg-forward.png` and `mg-multiple-sustained.png` under `.godot/aim-checks/29a99ea4c2fa4f5da7e2a6fe7a96708d/`. At chase-camera scale the gun points coherently at the engagement direction, twin barrels are readable, and firing feedback does not conceal the target. These captures precede the final belt visibility/smoothing corrections; they do not prove final belt animation in native gameplay.
- **VERIFIED — inspected recorded runtime results, not independently replayed network sessions:** [render log](ts-234-evidence/ts234-render-completed.log) records a clean PASS for imported poses, full sweep, confirmed feed displacement, release, 100 ms flash and eight scripted shooters. [Native Machine Gun log](ts-234-evidence/ts234-machine-gun-final.log) records 1,237 hits and 2,293.419922 points agreed by two impaired UDP peers. [Sticky](ts-234-evidence/ts234-sticky-final.log) and [freedom](ts-234-evidence/ts234-freedom-final.log) runs pass 64 and 41 checks. These support **INFERRED** integration robustness beyond the images directly inspected.
- **VERIFIED — independent rendered runtime execution:** reran `check-twin-fang.ps1 -NoBuild -Motion` with Godot 4.7.2 and required AppData/cache access. Exit 0, no warnings/errors, all pose/sweep/feed/release/low-FPS/eight-shooter checks PASS; [reviewer log](ts-234-evidence/ts234-critique-render.log). Reinspected freshly generated firing/release frames 013/065. The initial sandbox invocation returned no output and was interrupted; only the subsequent successful run is counted.

## Material Problems and Recommendations

### 1. Feed belts lack the connected mechanical appearance of the approved concept

- **Problem:** exposed rounds project outward as bright, widely fanning spikes. Links do not read strongly enough to make the visible curved sections look like a continuous ammunition belt entering a receiver. The top entry and hidden lower route are difficult to understand.
- **Evidence:** current close firing render and motion frames 010/013/059; compare the contained, connected feeds in the approved reference. Motion exists; the finding concerns the appearance of the moving assembly.
- **Severity:** Medium.
- **Impact:** the explicitly requested animated ammunition belt becomes a decorative bright arc instead of a believable rapid-feed mechanism, particularly in close views and side articulation.
- **Suggested Improvement:** refine round orientation/spacing and visible link continuity, and give the receiver entry a readable feed guide. Preserve the fixed rack, concealed central clearance route and confirmed-shot animation. Judge the result at chase distance as well as close-up; do not add a new feed physics system.
- **Scope:** In Scope.
- **Corrective Work Type:** Existing Task.

### 2. Receiver massing and material finish fall short of the approved weapon

- **Problem:** broad rectangular grey rear plates and simple upright supports dominate the gun. The reference has a clearer layered receiver, articulated side cheek/cradle and varied metal surfaces. The runtime receiver reads more like paired plain boxes, especially from the normal rear approach.
- **Evidence:** forward and close firing images versus approved reference; the native chase image also retains the paired rectangular plate silhouette.
- **Severity:** Medium.
- **Impact:** the result preserves the concept's component checklist but loses its aggressive, mechanically assembled character. This is a meaningful gap against the requested production model and reference fidelity.
- **Suggested Improvement:** refine large receiver contours and visible cradle/side plate shapes, add restrained edge/material separation, and retain the shortened tails required by overhead clearance. Prioritize changes visible in silhouette; do not restore oversized drums or length that intersects the actual rack.
- **Scope:** In Scope.
- **Corrective Work Type:** Existing Task.

### 3. Integrated runtime acceptance remains unresolved

- **Problem:** recorded final integration is not wholly successful. The full aiming run stops at crossing drift 0.55364 rad against 0.5; rack runs fail exit cleanup; the Missile run fails repeated activation.
- **Evidence:** [verification report](ts-234.md) and linked current/baseline logs. Unchanged HEAD also fails the crossing assertion and rack cleanup. The baseline Missile run passes repeated activation before failing density reset, so the exact activation regression is unresolved.
- **Severity:** Medium for current acceptance confidence; the available evidence does not establish a TS-234 production regression.
- **Impact:** sustained Machine Gun use is well supported, but complete cross-item lifecycle and surrounding aiming behavior cannot be represented as robustly verified.
- **Suggested Improvement:** isolate the repeated Missile activation outcome with controlled repeat runs and preserve the existing aiming/rack failure attribution. Obtain a human disposition for known baseline failures. If a TS-234 regression is demonstrated, correct that narrow regression and rerun affected integration; do not tune TS-262 aiming to force a green result.
- **Scope:** In Scope for integration diagnosis. Repair of demonstrated pre-existing aiming/rack defects is an **Out-of-Scope Recommendation** requiring separate authorization.
- **Corrective Work Type:** No Code Change Required for diagnosis/disposition; Existing Task only if a TS-234 regression is established.

## Runtime / Operational Limitations

The rendered eight-weapon scene uses scripted confirmations, not eight network players. Native evidence covers local impaired UDP, not real WAN/EOS or separate physical clients. Final belt motion has close-up rendered evidence, but no final human-driven chase-camera play session. No controller feel, audio listening, GPU/frame-time profiling, exhaustive clearance certification, long soak or reconnect experience was directly reviewed. Muzzle tips and visible firing direction look coherent; authoritative ray origin accuracy is supported by recorded checks rather than independently measured here. No instability is visible in the inspected rendering samples, but that cannot establish measured performance or negate the recorded lifecycle cleanup failures.

## Recommended Next Round

If the human authorizes Round 2, prioritize connected-looking ammunition feeds and reference-faithful receiver/cradle refinement, then inspect both in final native chase-camera motion with bursts, sustained fire, elevation and release. Resolve or explicitly disposition integration uncertainty before claiming complete acceptance. Preserve gameplay, authoritative firing, the fixed production rack and current aim rules.

**STOP — human decision required.** No recommendations have been implemented, no corrective issues created, and no commit/push or additional critique round is authorized by this report. The human may accept this result as-is or select specific corrective work.
