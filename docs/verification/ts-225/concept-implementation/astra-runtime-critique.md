# Astra Runtime Critique — Story Round 3

**Overall Score: 7.8 / 10**

**Quality Assessment: FAIL (<8.0)**

Independent review on 2026-09-28 after the final verification in [the Story report](../../ts-225.md). The new layout and startup sequence work: the chamber hangs under the rack, the forward intake connects through a duct, the outlet has a turbine/hub, and sparks/fumes precede the preserved flame. However, close rendered hardware still looks noticeably less finished than the approved reference and its surrounding production car. This is solid functional work below the highly polished visual gate; the last permitted round does not change that threshold.

## Category Scores

| Runtime / experiential category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.6 | Fresh rendered sequence passes selection, readiness, fan state, priming, ignition, cancellation, reuse, release, switching and lifecycle checks. |
| Visual Quality | 7.3 | Reference layout is recognizable, but the flat clean intake, bright uniform metal and sparse-looking chamber assembly lack the reference's convincing material depth and cohesion with the worn car. |
| Animation / Motion | 8.0 | Sampled startup states are distinct; runtime fan-movement assertions pass. Continuous motion smoothness was not directly watched. |
| VFX / Feedback | 7.9 | Sparks and gray fumes visibly precede the powerful flame; cancellation leaves no lit outlet. High-speed smoke remains discrete puffs. |
| Camera / Readability | 8.2 | Lower outlet remains readable from chase and leaves the track ahead clear. Fleet views retain distinct cars. |
| Runtime Integration | 8.3 | Underslung hardware and upper intake share the deployed rack; native ready/use evidence agrees with fixture presentation. |
| Observed Performance | 8.0 | Fresh capped 32-car p95 is 16.67 ms; fleet capture shows 58 FPS. This is not uncapped GPU headroom. |
| Runtime Stability | 8.4 | One independent normal-access rendered run exits cleanly with no diagnostics. |

The overall score is holistic, not an average or a reward for test counts. Audio, human controls, driving physics and WAN multiplayer are not scored.

## What Was Exercised

- **VERIFIED — independent execution:** Ran `check-boost-exhaust.ps1 -NoBuild -GodotPath .godot/verification-tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe` once with normal cache/log access. Godot 4.7.2 .NET, Windows, Compatibility/OpenGL, RTX 4070 Ti. Clean exit; [fresh log](critique-rendered.log).
- **VERIFIED — direct image inspection:** Compared the [approved sketch](../../../../assets/vehicles/boost/reference/ApprovedBoosterConcept.png) with fresh [mechanical detail](critique-mechanical-detail.png), [front intake](critique-intake-front.png), [sparks](critique-priming.png), [fumes](critique-fumes.png), [ignition](critique-ignition.png), [cancelled startup](critique-prime-cancelled.png), [sustained thrust](critique-sustain-b.png), [chase](critique-chase.png) and [32-car fleet](critique-thirty-two.png). Hardware placement and the startup stages are visibly distinguishable. Smoke exists behind the moving car; it is not a volumetric ribbon.
- **VERIFIED — supplied native evidence inspected:** Viewed [native ready](native-ready.png) and [native thrust](native-thrust.png), and read the native Nitro log. The 224 km/h capture shows the lower outlet firing and clearly separated smoke puffs. The close ready view shows the connected duct, supports and projecting hub. Native tests were executed by the implementation agent, not independently rerun here.
- **INFERRED:** Two-peer lifecycle consistency and real boost-speed behavior are supported by the supplied native verification. Fan rotation is supported by the independently executed runtime assertions, not a claim of continuous visual observation. Core/transport results were supplied verification context, not visual evidence.

## Material Finding

### Hardware finish does not yet cohere with the approved concept and car

- **Problem:** The main arrangement matches, but the clean, almost featureless red intake panel and uniformly bright silver/gold surfaces give the new assembly a simplified appearance beside the distressed production body. The chamber reads lighter and less mechanically substantial than the reference's dense enclosed engine. Small braces and lines exist, yet do not provide comparable visual depth in the close rendered views.
- **Evidence:** [Mechanical detail](critique-mechanical-detail.png), [front intake](critique-intake-front.png) and [native ready](native-ready.png), compared directly with the retained approved sketch. The discrepancy is visible in the rendered result, not inferred from source geometry or triangle count.
- **Severity:** Medium.
- **Impact:** The hero upgrade looks newly added rather than convincingly belonging to this car, and the approved concept's heavy mechanical character is only partly realized.
- **Suggested Improvement:** If the human chooses further work, prioritize material and surface cohesion: restrained wear on intake edges, roughness/value separation among casing, fasteners and hot metal, and clearer dark recesses around the turbine and casing. Reassess chamber mass and visible casing depth against the reference at the native close angle. Preserve the working layout, short extension and flame. Exact illustration pixels or a wholesale redesign are unnecessary.
- **Scope:** In Scope — visual realization of the approved booster concept.
- **Corrective Work Type:** Existing Task — refine the current authored asset/material presentation; no new gameplay system is needed.

## Runtime / Operational Limitations

- Evidence is sampled rendered imagery and scripted checks, not uninterrupted video or manual driving. Smoothness of the fan, the rack's visibility reveal and the subjective 0.30-second startup delay remain unverified by continuous observation.
- The sustained native smoke trail is a sequence of separated particle puffs. Its presence is verified; continuous volumetric smoke is neither delivered nor claimed. This is a retained visual limitation, not an additional automatic work request.
- The fleet is 32 visual instances, not 32 network peers. Native evidence uses two local peers; WAN/EOS, separate devices and reconnect conditions were not exercised by this critique.
- Early capture HUD readings fall into roughly 22–33 FPS while capture work is active; causal attribution is unavailable. Capped final timing does not establish low-end performance. No exported-build, weather, lighting or audio matrix was tested.

## Recommended Next Round

No fourth Story critique round exists under [the repository policy](../../../critique.md). The human must decide whether to accept this result as-is, defer the material-finish gap, or explicitly direct how further work should be handled. If further asset work is authorized, prioritize the single cohesion finding above. Do not begin it automatically.

**Human gate:** Final allowed Story Round 3 is complete. Stop for explicit human direction. This reviewer changed only critique evidence/documentation and made no production changes.
