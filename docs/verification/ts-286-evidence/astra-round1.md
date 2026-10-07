# Astra Runtime Critique — Story Round 1

TS-286 — recovered TS-282 chassis-contact correction, 2026-10-07, delivery branch `ts-286-pg`.

**Overall Score: 8.5 / 10**  
**Quality Assessment: PASS (>=8.0)**

This independently operated corrective pass finds the accepted aerial behavior coherent and responsive: natural rotation remains before input, release holds the chosen pose while travel continues, and native chassis impacts resume rotation immediately. The exact damage-forgiving underside correction is covered by the fresh deterministic regression; it is not misrepresented as a reproduced native collision geometry.

## Category Scores

| Relevant category | Score |
| --- | --- |
| Runtime Functionality | 8.5 |
| Controls / Responsiveness | 8.7 |
| Vehicle / Movement Feel | 8.5 |
| Physics / Contact Integration | 8.3 |
| Runtime Stability | 8.5 |

## What Was Exercised

**VERIFIED — independent rendered runtime.** After the parent confirmed completed final verification, Astra launched Godot 4.7.2 .NET with `handling_playtest.tscn -- --inertia-playtest --handling-flat`, production default tuning and the practice adapter. Bounded native synthetic keyboard/analog segments were selected after observing prior state. This was segmented agent playtesting, not continuous human play. Local trace summary `.godot/ts-282-correction/astra/summary.json` preserves first/last states, first contacts, peak spin and selected unsupported crash ticks.

- Natural flight retained angular velocity `(1.5, -0.8, 2.2)` rad/s through 40 neutral frames, with forward speed 11 m/s and gravity continuing.
- A 22-frame keyboard roll reached 3.2328134 rad/s. Releasing for 35 frames immediately produced zero spin and retained the exact quaternion, with uprightness 0.72631, while position changed from Y 21.92999 to 18.959728 and Z -3.5000005 to -9.166665. The [held tilted pose](astra/hold.png) was directly viewed.
- From that observed pose, 22 frames of opposite roll returned uprightness to 1. Neutral release then completed a wheels-down landing at 1000 HP over 180 frames. Input resumed rotation on the first commanded step; neutral arrested it on the first released step.
- Synthetic analog pitch armed a sideways pose. Eighteen neutral frames held zero spin at uprightness approximately zero while falling. The [side hold](astra/side-hold.png) was directly viewed. On the first chassis-contact tick 602, all wheel compressions were zero, four terrain contacts were present, crash age was only 0.016666668 seconds and angular velocity was `(-3.9345284, -0.2564013, 0.16159888)` rad/s (magnitude 3.9462). Rotation resumed before delayed recovery could explain it. The 230-frame impact segment ended upright with tire support and 968.1666 HP.
- A separate synthetic analog yaw input armed an inverted pose; 15 neutral frames held zero spin at uprightness approximately -1. The [roof hold](astra/roof-hold.png) was directly viewed. First roof contact at tick 860, with zero wheel compression and crash age 0.016666668 seconds, produced `(-3.274868, -1.3230053, 1.1735015)` rad/s (magnitude 3.7219). At unsupported neutral ticks 955–962 the angular vector remained exactly `(0.18795143, 0.0323069, -1.9911491)` while orientation continued changing. The segment ended upright at 965.4166 HP; the [recovered rendered result](astra/roof-impact.png) was directly viewed. Later recovery remains the existing recovery system, not evidence of a newly added righting mechanism.
- The independent fixture log identified the native renderer and default tuning and contained no runtime errors; stderr was empty. Only the owned fixture/wrapper processes were stopped. The user editor was untouched.

**VERIFIED by completed parent checks, independently inspected evidence rather than rerun:** the fresh parent verification extracts inspected before evidence cleanup record the 1178 Core tests and 445 transport tests plus ten passing native gates, including both adapters, crash/landing, six terrain surfaces, vehicle replay at 30/144 FPS, impaired local UDP and reconnect. Initial GUI-launch and engine signal-disconnect failures were retained separately, followed by clean retries. These are not scored as independently played multiplayer sessions.

**INFERRED for the precise native underside edge:** the inspected `BodyContactDisarmsHoldAndRestoredBounceRetainsImpactSpin(3, 1)` regression explicitly supplies upright damage-forgiving terrain contact at local Y=-0.3, zero wheel support and incoming `(2,-1,3)` spin. It asserts hold cancellation, exact retained spin and ten-step equality after codec restoration. The fresh Core suite passed. Native side/roof trials above independently establish contact-release integration, but their contacts are not the same damage-forgiving underside geometry.

## Runtime / Operational Limitations

The native fixture does not expose contact local positions or the release-hold latch itself; hold/cancellation is observed from input, orientation, contacts and spin. Exact upright underside contact with zero wheels was not independently recreated in Godot. This remaining narrow native coverage gap limits confidence relative to an exhaustive collision-geometry playtest and is reflected in the score.

No physical gamepad, sustained human trick-driving session, audio listening, production chase-camera feel, exported build, authenticated EOS/Internet, second PC or independent multiplayer visual inspection is claimed. The parent network contact correction maximum was 1.8357 m; this is not evidence of invisible reconciliation. Isolated rendered captures and bounded traces do not establish general performance quality. Prior TS-282 Story rounds are historical. The user assigned this recovered correction to TS-286; this is TS-286 Story Round 1, using the same unchanged implementation exercised above.

No material in-scope experiential defect was exposed in this pass. No additional correction is recommended. Raw traces, runtime logs, helper script and PID file are kept only in ignored `.godot/ts-282-correction/astra`; the report and four directly inspected captures are the delivery artifacts. Delivery remains with the parent under the user's explicit authorization to commit/push a new corrective PR without merging after this critique.

