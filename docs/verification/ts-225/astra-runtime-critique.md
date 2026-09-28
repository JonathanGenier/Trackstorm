# Astra Runtime Critique — Story Round 1

**Overall Score: 7.7 / 10**

**Quality Assessment: FAIL (<8.0)**

Reviewed the revised TS-225 result on `ts-225-jg` on 2026-09-27, after the comprehensive verification recorded in [the Story report](../ts-225.md). This score evaluates the rendered experience, not implementation effort or test counts. The hardware and large living flame are convincing; the smoke presentation remains materially below the explicitly requested substantial eruption/plume. The exact score is below the quality gate and has not been rounded upward.

## Category Scores

| Runtime / experiential category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.5 | Deployment, thrust, release, retraction, cancellation and reuse work coherently in the exercised sequences. |
| Visual Quality | 8.0 | Rear mounting, telescoping rings and metallic supports fit the production Car; the hot core and changing orange silhouette are readable in daylight. |
| Animation / Motion | 8.2 | Fast extension and controlled retraction communicate a mechanical assembly; sustained flame shape visibly changes between samples. |
| VFX / Feedback | 7.2 | Large layered flame communicates thrust strongly. Smoke remains light and fragmented, especially at actual Boost speed, weakening the requested powerful exhaust eruption. |
| Game Feel / Juice | 7.5 | Mechanical anticipation and immediate flame cutoff give the lifecycle definition. The smoke contributes too little visual mass to match the otherwise forceful jet. |
| Camera / Readability | 8.2 | Rear chase and fleet views leave the road and cars visible; the flame sits behind the vehicle without covering the route ahead. |
| Multiplayer / Networking Experience | 8.0 | Fresh native two-peer runs complete repeated use/release/exhaustion; local and remote presentation assertions pass. This is local-harness evidence, not a WAN judgment. |
| Observed Performance | 8.0 | Fleet views remain readable at 8 and 32 effects; an uninstrumented loop segment reports 16.70 ms p95 at the 60 FPS cap. Capture-time FPS reductions limit finer conclusions. |
| Runtime Stability | 8.0 | Both native review runs exit cleanly and the fixture completes a loop. Earlier nonreproduced articulation teardown warnings remain a stated uncertainty. |
| Runtime Integration | 8.4 | Rear jet stays separate from the deliberately moving rack/trunk; production Car, active map and real native driving all render the same presentation. |

The overall score is a holistic visual-polish judgment, not an arithmetic mean. Audio, human control ergonomics, combat enjoyment and comprehensive vehicle physics were not scored.

## What Was Exercised

- **VERIFIED — direct execution and window observation:** Launched the final `boost_exhaust_checks.tscn -- --boost-loop` fixture at 1280×720 in Godot 4.7.2 .NET, OpenGL Compatibility, RTX 4070 Laptop GPU. Activated its window and pressed Space to restart the lifecycle. Observed the stowed car, partial extension, active thrust with the rack raised, moving 6 m/s presentation, release/cancelled state, reignition, rear chase framing, eight effects and 32 effects. These are production presentation instances driven by scripted accepted snapshots, not 32 live players or physical driving.
- **VERIFIED — sampled motion:** Direct captures at deployment and subsequent active stages, plus successive sustained frames, show the telescoping outlet and changing flame envelope. The white/cyan core, violet transition and orange outer flame remain distinct. The flame is large relative to the Car and does not read as a static cone.
- **VERIFIED — rendered lifecycle evidence:** Inspected [ignition](ignition.png), [sustained thrust](sustain-b.png), [release](release.png) and [retraction](retract.png) captures from the final implementation. At release the flame is gone while residual smoke remains; retraction and eventual decay are clean. Live fixture observation independently confirmed reuse and fleet presentation.
- **VERIFIED — real native physics and transport:** Ran `check-nitro.ps1 -Visual -NoBuild` twice during this critique, using normal Godot cache/window access. Both completed successfully, including two local native UDP peers, six launch conditions, repeated use/release and resource exhaustion. Observed the follow-camera runtime during moving/recovery phases. Inspected the newly generated [native active-thrust capture](critique-native-thrust.png) at 224 km/h / approximately 62.22 m/s; its [run log](critique-native.log) records activation/cutoff assertions on each peer's reconstructed presentation. Active high-speed visual assessment relies on this timed runtime capture; it is not claimed as uninterrupted manual driving.
- **INFERRED from supplied final verification:** The comprehensive Core/transport, impaired Nitro, rack, articulation, arena, separate-process networking and startup results are recorded in the Story report. They support operational confidence but are not substitutes for the visual observations above.

## Material Finding and Recommendation

### Smoke lacks the requested substantial exhaust mass

- **Problem:** Smoke is present and fades correctly, but reads as small transparent wisps rather than a substantial eruption and plume accompanying the large jet flame. At real Boost speed it becomes visibly separated puffs with large gaps.
- **Evidence:** The live 6 m/s sustained view and [sustained capture](sustain-b.png) show scattered pale wisps around/behind the flame. [Ignition](ignition.png) is dominated by the flame with little visible smoke mass. The freshly generated [native 224 km/h capture](critique-native-thrust.png) makes the disconnected smoke puffs particularly clear. [Release](release.png) confirms the residual layer exists, but it remains light.
- **Severity:** Medium.
- **Impact:** The effect communicates activation reliably, but underdelivers the user-approved visual-polish target of large flame and substantial smoke erupting from the outlet. The thin trail feels less powerful than the integrated hardware and flame imply.
- **Suggested Improvement:** If authorized, tune the smoke's initial volume, coverage and lifetime profile so ignition produces an identifiable expanding plume and sustained travel preserves a coherent turbulent smoke trail at real Boost velocities. Consider velocity-aware spawn spacing and size/opacity shaping rather than increasing particle count alone. Retain prompt flame cutoff, natural residual fade and unobstructed chase visibility. Reassess at 0, 6 and approximately 62 m/s, then repeat the 8/32-effect check to bound overdraw/readability costs.
- **Scope:** In Scope — the explicit substantial-smoke and strong-ignition requirements of TS-225.
- **Corrective Work Type:** Existing Task — refine the Story's existing VFX work; no additional gameplay system is required.

## Runtime / Operational Limitations

- Observation used intermittent native window screenshots and timed fixture captures; it was not continuous video or a human gamepad play session. Space restarted the presentation fixture, while native driving inputs were scripted.
- Fleet poses are scripted, and fleet cars are stationary during the 8/32 phases. No 32-peer session, multi-device EOS/WAN session or human multiplayer match was exercised by this critique.
- Native active-thrust visual detail was inspected in the harness's fresh capture. The live screenshots mainly caught release/recovery; they do not establish every high-speed transition's subjective timing.
- Clear-day Compatibility rendering on one laptop GPU was assessed. Other lighting/weather, graphics hardware, exports, extreme camera angles and particle/scenery collision behavior were not evaluated.
- The fixture reported 16.70 ms p95 in a completed loop at a 60 FPS cap; the prior final verification reported 16.71 ms. Neither establishes uncapped GPU headroom. During screenshot activity the on-screen counter sometimes fell into roughly 30–46 FPS, so this review cannot attribute those transient drops solely to the game or claim universally steady 60 FPS.
- The initial sandboxed launch could not write user logs/read the certificate store. It was stopped and repeated with normal access. These environmental diagnostics are separate from the clean native review runs.
- The first pre-critique headless articulation run reported 24 ObjectDB instances and three resources at teardown. The verbose repeat and rendered final repeat were clean without code changes. Cause remains unknown; this review does not erase that uncertainty.
- No audio judgment is made. No change to Boost physics is proposed.

## Recommended Next Round

If the human authorizes another round, refine only the smoke/ignition visual mass and its continuity at actual Boost speed, then recheck lifecycle, road visibility and fleet cost. Preserve the strong existing hardware integration and animated flame. Do not begin another round, implement this recommendation or create corrective Jira work automatically.

**Human gate:** Formal Story Round 1 is complete. Stop for explicit human direction. The critique's own loop was stopped and both native review runs exited; the user's Godot editor was left open.
