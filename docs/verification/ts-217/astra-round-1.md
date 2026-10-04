# Astra Runtime Critique — Story Round 1

TS-217 — Shield Rear Shield Deployment. Independent Astra review, 2026-10-01, after integrated implementation verification. No implementation changes were made during this review.

**Overall Score: 8.2 / 10**

**Quality Assessment: PASS (>=8.0)**

The scoped prototype delivers a coherent physical rear shield: the plate visibly occupies the rear, covered fire spends shield health without leaking into chassis damage, exposed angles remain vulnerable, and destruction restores vulnerability while the other vehicle's shield remains independent. Native collision and impaired local multiplayer outcomes agree with that spatial behavior. Repeated state transitions, late admission and actual disconnect/resume retain the damaged pools instead of refilling or duplicating them. These observed results support a passing operational assessment. The score is bounded by controlled scenarios and sampled rendered images; it is not a claim that final presentation, unrestricted driving or Internet multiplayer has been perfected.

The review excludes world-wall input, final models/animation/VFX and balance, as instructed. Existing world-wall state is mentioned only where the shared lifecycle/recovery fixture uses it. The intentional temporary plate is judged for spatial readability and attachment, not final art quality. Test counts and implementation effort did not contribute to the score.

## Category Scores

| Material runtime category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.5 / 10 | Ordinary input deployment, retained damaged HP, covered and uncovered shots, destruction under sustained moving fire, and independent shields behaved consistently. |
| Physics | 8.0 / 10 | Finite rear/quarter coverage and a native vehicle collision produced the expected protected-chassis result. The exercised flat-ground cases completed without a runtime physics failure; uneven terrain and rollover contacts remain unverified. |
| Multiplayer / Networking Experience | 8.2 / 10 | Impaired two-peer pools/collider presence converged; three-peer late admission and three actual resumes retained authoritative state. This evaluates operational consistency, not subjective latency feel. |
| Runtime Stability | 8.5 / 10 | All three independently executed native runs exited successfully without logged warnings/errors or failed state assertions, including the approximately 140-second recovery lifecycle. |
| Runtime Integration | 8.3 / 10 | Deployed armor coexists with another selected weapon, native vehicle bodies and damage, replicated removal, retained inventory state, and recovery. |
| Feature-Specific Operational Quality | 8.0 / 10 | Rendered plate extent makes rear coverage and side exposure understandable; the destroyed shield is absent while the surviving vehicle retains its plate. This is prototype readability, not a final visual-quality score. |

The overall score is a holistic judgment of the scoped result, not a rounded arithmetic average or a reward for passing automated checks. Controls, camera, audio, continuous animation smoothness, game feel and frame-time performance are not scored because this review did not meaningfully observe those qualities.

## What Was Exercised

- **VERIFIED — independently executed rendered native shield scenarios:** `check-rear-shield.ps1 -NoBuild -Visual -Impaired`, Godot 4.7.2 Mono on Windows, NVIDIA RTX 4070 Ti/OpenGL, two local real-UDP peers with 30 ms outbound delay, 5 ms jitter and 2% loss. Ordinary local/remote input deployed the damaged shields; production drivers/native bodies handled the scenarios. The fixture controlled grants, poses and firing settings. [Independent runtime log](astra-round-1-rear-shield.log).
- **VERIFIED — finite coverage:** stationary rear, moving rear and inner-quarter fire each reduced shield HP 900 to 810 while vehicle HP remained 1000. Outer-quarter fire left the shield at 900 and reduced vehicle HP 1000 to 910; side and front fire continued reducing the vehicle to 820 and 730 without shield damage. These are direct runtime results, not conclusions from geometry source code.
- **VERIFIED — destruction and physical impact:** moving sustained fire exhausted a 10-HP shield, removed its native cover and then reduced vehicle HP 730 to 651.25. A subsequent freshly deployed shield took a rear collision from 900 to 823.21387 while chassis HP remained 651.25. The other vehicle's shield remained 900 in every scenario; both peers' pools and collider presence converged.
- **VERIFIED — rendered captures personally inspected:** deployment, moving fire, inner/outer quarter, side, front, destruction and post-collision views. The snapshots show plates behind both vehicles, a weapon still mounted/active on the attacker, visible finite edges, and the target plate absent after destruction. Retained samples: [moving fire](astra-round-1-moving.png), [outer-quarter exposure](astra-round-1-edge.png), [destroyed shield](astra-round-1-destroyed.png). Attachment at sampled moving poses is observed; continuous motion smoothness is not established by still images.
- **VERIFIED — independently executed lifecycle/late admission:** `check-shield.ps1 -NoBuild -Impaired` ran four independent pools, duplicate damage/transition attempts, preserved rear slots and persistent pools, full authority/checkpoint restoration, third-peer late admission, and twenty repeated destruction attempts resulting in exactly two removals. The lethal repeated-destruction cases in this shared fixture concern world/held pools; rear-specific destruction/removal was exercised by the shield run above. [Independent lifecycle log](astra-round-1-lifecycle.log).
- **VERIFIED — independently executed actual recovery:** `check-reconnect.ps1 -NoBuild -Shield` performed three real local-UDP arena resumes, including 125 seconds offline. Each restored the WorldWall/RearShield at 700/600 HP with exact attachment/pose and damage watermark, without refill. Native body reuse, prediction/interpolation reset and surrounding item/match recovery completed successfully. Authentication uses the fixture's test identity, not production EOS. [Independent reconnect log](astra-round-1-reconnect.log).
- **INFERRED — unobserved weapon combinations:** the supplied integrated verification report and deterministic checks support Missile/Salvo/Proxy Mine interception paths and detailed identity/slot invariants beyond the independently exercised paths. They are supporting evidence, not a claim that this reviewer played every weapon/shield combination.

## Runtime / Operational Limitations

- **UNVERIFIED:** a human-controlled match, physical keyboard/controller ergonomics, chase-camera visibility, subjective deployment latency, listened audio, continuous frame-by-frame motion and measured performance. This review launched the rendered harness and inspected its captured frames; it did not manually drive the game or watch a recorded motion sequence.
- **UNVERIFIED:** real authenticated EOS, Internet conditions, independent machines, cross-platform physics, host succession with a deployed rear shield, and long-session stress. Local UDP impairment and direct authority/checkpoint restoration are narrower evidence.
- **UNVERIFIED:** shield-specific native Missile/Salvo/mine combinations, steep banks, rollover contacts, all vehicle geometries and exhaustive edge/grazing trajectories. The independently observed weapon coverage was Machine Gun fire; the collision case was a controlled native rear impact.
- The native shield fixture exercises two shielded vehicles on flat ground with controlled poses and a fixed observation camera. Its moving cases apply throttle for short intervals; stationary angle cases reposition bodies. It establishes the reported damage/coverage outcomes, not unrestricted driving quality.
- The shared lifecycle fixture directly exercises multiple pools and retained slots, but this critique does not claim visual or impact-selection coverage of two overlapping rear plates on one vehicle.
- Final art, deployment animation, destruction VFX, world-wall placement controls and balance are excluded from this Story assessment. No missing final-art polish is counted as an in-scope defect.

No material in-scope runtime defect was exposed by these observations. No corrective recommendations or next round are proposed for this passing critique.

**Human gate:** Per `docs/critique.md`, stop after this formal critique and wait for explicit human instruction. This PASS does not authorize acceptance, delivery, another critique round or additional implementation.
