# TS-73 runtime-feedback correction

## Scope and authority

This correction follows the rejected first handling candidate and the approved player-friendly runtime feedback in [TS-73](https://jonathangenier.atlassian.net/browse/TS-73). The complete current description, comment, dependency TS-72, map foundation TS-71, parent TS-88 and downstream TS-100 were inspected. TS-73 has no required child issues. Work stays on `ts-73-pg`; the existing Story PR is #50. No later surface, pickup or environment work is included.

The intended result is forgiving ordinary acceleration/cornering, decisive smooth lift-off, working suspension over normal disturbances, and intentional recoverable handbrake rotation. The required powered speed remains 44.44 m/s. This is a new production-default candidate awaiting human driving acceptance, not a claim that subjective acceptance has already occurred.

## Investigation and root causes

1. **Collider/suspension interaction — VERIFIED.** The old full-length box filled space beneath the bumper overhangs. On the 35-degree bank-to-flat-infield crease, the leading bottom corner struck the infield before the front suspension reached it. A captured network trace shows downward velocity changing from about -8.09 m/s to -0.15 m/s on a body collision, followed by all four wheel compressions becoming zero and repeated collider landings. The same 6/12/20 m/s downhill entries each lost wheel support for 38 frames. The replacement hull preserves width, length, roof height and the axle-supported underbody, but bevels its underside toward the front/rear bumper ends. Both adapters use the same hull. Revised crossings retain support throughout.
2. **Vertical spring/damping balance — VERIFIED / INFERRED.** The old normalized spring rate 150 and damping 18 gave only 6.54 cm static sag and a damping ratio about 0.735. The new 100/20 pair gives 9.81 cm sag and critical damping for the vertical mode; extension increases by 3.27 cm to preserve the authored 0.9 m ride height. Native crest tests directly verify compression, reduced chassis rise, maintained support and prompt settling. A 65/s² experimental spring failed sustained banking and was rejected before final validation.
3. **Coasting — VERIFIED.** Existing coast resistance is exponential longitudinal damping and is inactive under power on the identity asphalt surface. Its old 0.12/s rate required about 25 seconds to lose 95% of speed. Raising this existing setting to 0.5/s gives about six seconds to lose 95%, with continuous decay and no reversal or velocity snap. No extra engine-braking, drag or transmission subsystem is required.
4. **Steering onset — VERIFIED / INFERRED.** At 6 rad/s the full 0.6 rad wheel request could arrive in 0.1 seconds. The revised 2.4 rad/s rate spreads that over 0.25 seconds; reducing the speed-scaling reference from 22 to 18 m/s also reduces wheel authority at high speed. Native normal-turn/release tests verify bounded yaw and recovery under throttle. Human judgement of responsiveness remains unverified.
5. **Straight-line spin — not reproduced on level asphalt.** Before changing defaults, three deterministic disturbed-yaw tests settled under neutral-steering throttle; both native adapters also accelerated straight on the flat oval section. Therefore the report does not attribute a spontaneous level-road spin to an unproven tire, differential or engine defect. Contact loss and overly abrupt steering are confirmed vulnerabilities addressed here. Final tests cover neutral acceleration from rest to the cap, small yaw disturbances at 5/20/35 m/s, and ordinary turns followed by neutral throttle at 8/28 m/s. Reproducing the user's exact spin scenario remains part of human acceptance.

### Dynamics reviewed and retained

Mass remains 900 kg. Native center of mass remains the existing low, authored-offset position. Tire friction remains 1.35, lateral response 12/s, yaw damping 0.65/s, engine demand 11 m/s² before rear-tire saturation, and drive traction reserve 0.55. Existing longitudinal load transfer, compression-derived axle balance and road-relative forces remain authoritative. The compact axle model has no differential, transmission, individual wheel spin or separate anti-roll bar. There is no evidence from the reproduced straight-line tests requiring a new drivetrain or yaw assist.

The spring calculation already uses contact point velocity (including angular motion), bounded individual forces and support-normal damping. The same damping coefficient applies during compression and rebound; near-critical tuning meets the measured disturbance target without a new asymmetric damper setting. Ground/contact sampling remains fixed at 60 Hz, independent of rendering. No downforce, increased gravity, forced heading, scripted drift, velocity-to-road adhesion or map-specific steering was added.

The production map's continuous collision, triangle normals, flat infield and matching rim were rechecked. No map gap or vertical-step defect was found; the bank-to-infield slope discontinuity is authored geometry. No production map or mesh asset was edited. A separate elevated runway and 12 cm crest exist only inside the acceptance fixture.

## Final production/default values changed

| Configuration / Developer Options key | Previous | New |
| --- | ---: | ---: |
| `SteeringSpeed` / `vehicle.steering_speed` | 22 m/s | 18 m/s |
| `SteeringResponse` / `vehicle.steering_response` | 6 rad/s | 2.4 rad/s |
| `CoastDrag` / `vehicle.coast_drag` | 0.12/s | 0.5/s |
| `SuspensionLength` / `vehicle.suspension_length` | 0.9654 m | 0.9981 m |
| `WheelSpring` / `vehicle.wheel_spring` | 150/s² | 100/s² |
| `WheelDamping` / `vehicle.wheel_damping` | 18/s | 20/s |

The length expression is `VehicleDimensions.RideHeight + 9.81f / 100`, with ordinary single-precision rounding. The 160 km/h drive cap, smooth propulsion taper, legitimate external overspeed, handbrake engagement/recovery and other tuning values remain unchanged. Native tests still demonstrate progressive drift initiation, countersteering, throttle-assisted recovery, tight rotation and excessive-input spin.

**No new Developer Options controls or wire fields.** All six values already use the existing 59-key catalog. Practice `VehicleConfiguration`, `GameplayConfiguration.HostedDefaults`, Reset, persistence, host simulation and client prediction share the same record. Native production UI checks verify Reset stages the complete defaults; Apply commits once, replicates the exact configuration to the client and replaces the saved tuning; restart reads persisted values. Exact-default and codec/persistence tests cover the complete record. Fresh normal gameplay needs no session override. Existing saved deliberate overrides remain respected; Reset + Apply replaces them with this candidate.

## Runtime and automated evidence

The integrated branch includes current main `aa05b00`, with canonical version **0.1.7** and Windows **0.1.7.0**. Requirements were reread from Jira after implementation; its latest runtime-feedback section was unchanged (issue updated 2026-09-19 14:23:30 -0400).

| Check | Result |
| --- | --- |
| `check.ps1` | PASS: formatting, zero-warning Debug/Release builds, **462 Core + 317 non-native Client/transport tests in each configuration**, 108 version-rule checks and frontend media verification. |
| Final `check-oval.ps1` | **175 assertions**; production configuration, both native adapters and committed map collision. |
| Three high-speed laps | Peak **43.028 m/s**, **4,363/4,363** driving frames supported, peak normal speed **0.613 m/s**, maximum centerline deviation about **1.81 m**. |
| Neutral asphalt acceleration | Both adapters: peak yaw **0.0016 rad/s**, lateral deviation about **0.16 m**, continuously supported. |
| Powered runway acceleration | Both adapters reach **44.4399 m/s** under neutral steering, without a cornering controller or configuration override. Existing deterministic overspeed tests retain external 50 m/s velocity under throttle. |
| Lift-off from 44.44 m/s | About **16.3 m/s after two seconds**, **0.30 m/s after ten seconds**; no reversal. |
| Ordinary turns then neutral throttle | Entries **8/28 m/s**: peak yaw below **0.45 rad/s**, final yaw approximately zero, all supported. |
| Bank-to-infield at 6/12/20 m/s | Both adapters remain supported. Practice maximum upward rebound **0.324/0.381/0.542 m/s**; network **0.286 m/s**. Final-second vertical motion below **0.03 m/s**. |
| Diagonal bank crossing at 16 m/s | No support loss; practice/network rebound **0.210/0.273 m/s**, final-second vertical motion effectively zero. |
| 12 cm crest at 6/12/20 m/s | Chassis rises **6.1–8.7 cm**; wheel compression peaks **16.4–18.5 cm**; no support loss; final-second vertical motion about **0.0001 m/s**. Compression/rebound is measured, not merely inferred from a grounded final frame. |
| Low-speed bank gravity/start | Neutral vehicle descends **1.510 m in three seconds**; banked standing start reaches **8.154 m/s** with support. |
| Short handbrake, countersteer and throttle recovery | Peak yaw **0.551 rad/s**, final lateral speed approximately zero; rear grip recovers progressively. |
| Excessive-input spin | Held steering/handbrake reverses facing relative to entry: forward dot about **-0.969**. |
| `check-vehicle.ps1` | **126 assertions at both 30 and 144 render FPS**, matching movement and surface replays; braking, reverse, lane change, low/fast corners, twelve timed throttle/handbrake-release cases, collisions, damage, ramp launch/landing and explosions pass. |
| Final `check-developer-options.ps1` | **243 write/UI/replication assertions + 8 restart/read assertions** pass after main integration. Reset + Apply commits, persists and replicates the complete current default record. |
| Rendered oval | **179 assertions** before the final diagonal checks were added. Side and banked images inspected; authored dimensions, ride height and static tire contact retained. The final headless run includes the added diagonal checks. |
| Additional `check-startup.ps1` | Startup/recovery assertions reach Main Menu, but the strict script **FAILS** on a shutdown warning: two leaked native objects. Repeated, then identified with `--verbose` as `AudioStreamMP3` and `AudioStreamPlaybackMP3` associated with `Music`. The frontend audio/startup code is unchanged by this Story; the cause is not established as a TS-73 regression or conclusively proven pre-existing. No unrelated audio change was made. |

New deterministic coverage adds three perturbed-yaw cases plus smooth coast-down and suspension crest/settling tests. Exact-default expectations cover all six changes. The steering-onset test now requires a gentler first fixed tick (0.04 rad) while retaining measurable physical yaw within 100 ms. Existing authority, surfaces, progressive handbrake, powered limit, external impulses, damage, lifecycle, prediction and codec tests remain active.

The [baseline log](ts-73-correction/baseline-oval.txt) and [80-frame network crossing trace](ts-73-correction/baseline-network-crossing.csv) retain the reproduced old contact failure. [Final oval evidence](ts-73-correction/oval-evidence.txt), [full solution results](ts-73-correction/full-check.txt), [native vehicle results](ts-73-correction/vehicle.txt) and [rendered bank view](ts-73-correction/vehicle-banked.png) preserve the measured correction.

The initial impaired two-process run used 50 ms outbound latency, 10 ms jitter and 2% loss while rendering/formatting also ran. It passed the existing harness but had p99 correction **1.719 m**, maximum **4.008 m** and **one large correction**. An isolated repeat with identical impairment passed at p99 **0.166 m**, maximum **2.345 m**, **zero large corrections**. Both outcomes are retained ([concurrent](ts-73-correction/network-concurrent.txt), [isolated](ts-73-correction/network-isolated.txt)); attributing their difference to scheduling load is an inference, not proof that a general networking limitation is fixed.

The [final integrated 0.1.7 network run](ts-73-correction/network-final.txt) also passes the existing harness, with p99 **0.397 m**, maximum **3.339 m** and **one large correction during startup** (about 1.08 seconds, 25 pending inputs). This remains an explicit replication/startup limitation; the zero-large-correction repeat is not presented as a guarantee. This correction adds no prediction or networking algorithm changes.

[Final Developer Options results](ts-73-correction/developer-options.txt), [startup warning](ts-73-correction/startup-warning.txt) and [verbose leaked-object identification](ts-73-correction/startup-verbose.txt) preserve the additional integration evidence. The startup warning means that not every extra runtime script is clean, despite the passing required solution checks and handling suite.

## Remaining acceptance and limits

Human driving, subjective fun/forgiveness, physical-controller ergonomics and reproduction of the exact reported spin are **UNVERIFIED**. Automated inputs and rendered frames do not establish human acceptance. The infield retains its authored Concrete handling ID; final grass/dirt/mud handling remains later scope. This pass does not claim an eight-player soak or separate-PC EOS/NAT validation.

## Files changed in this correction

- `code/Core/Vehicles/VehicleConfiguration.cs`: six default changes; existing authority and validation owners retained.
- `code/Client/Vehicles/VehicleVisual.cs`: one shared convex chassis collider with beveled front/rear underside.
- `code/Tests/Vehicles/VehicleMovementTests.cs`: disturbed-yaw, coast and suspension-settling regression tests; gentler steering-onset expectation.
- `code/Tests/Development/ReleaseDefaultsTests.cs`: exact production defaults and existing persistence/wire round-trip coverage.
- `code/Client/Verification/OvalIntegrationChecks.cs`, `.Handling.cs`, `.Bumps.cs`: hull agreement, actual support/settling assertions, both physics adapters, runway/cap, coast, normal turns, bumps and bank crossings.
- `code/Client/Verification/VehicleIntegrationChecks.cs`: onset assertion now requires the gentler first tick while retaining the 100 ms physical-yaw response requirement.
- `docs/features/vehicles.md`, `developer-options.md`, `oval-map.md`: current configuration, collision and validation contracts.
- `Directory.Build.props`, `export_presets.cfg`: canonical version synchronization after main integration.
- This report, correction evidence and `docs/verification/README.md`: investigation, verification and delivery record.

Main synchronization also brings already completed DevTools, game-loop and CI work; those are not additional TS-73 feature changes.

The recurring tab-only `InputButtons.cs` edit was normalized under the user's existing explicit authorization and returned to tracked content. It adds no Story diff.

## Astra Self-Critique — Story Round 2

**Overall Score: 6.5 / 10**

**Quality Assessment: PASS (>=6.0)**

| Category | Score | Evidence / limitation |
| --- | --- | --- |
| Functionality | 6.5 | Requested deterministic and native handling behaviors pass; exact reported spin and human acceptance remain open. |
| Controls / responsiveness | 6.5 | Gentler onset still creates physical yaw within 100 ms; ordinary turns recover under throttle. Human ergonomics unverified. |
| Vehicle feel / gameplay | 5.0 | Measured coast, compression and settling improve the specified baseline, but enjoyment cannot be established by scripted inputs. |
| Physics | 7.5 | Reproduced contact defect corrected; sustained laps, perpendicular/diagonal crossings, bumps, drifts, impacts and launches exercised. Averaged support and two-axle dynamics remain approximations. |
| Networking | 5.5 | Shared defaults, codecs, prediction and impaired native harness pass; startup correction spikes remain, including one large correction in the final run. |
| Architecture / code quality | 8.0 | Six existing defaults and one shared collider changed; no new controller, state machine, tunable, dependency or Shared layer. |
| Testing / reliability | 8.0 | Regression checks now measure the entire transition, both adapters, actual compression/rebound and settling rather than only a final grounded frame. Full Debug/Release checks pass. |
| Integration / stability | 6.0 | Reset/Apply/persistence and damage/lifecycle pass; additional strict startup script is not clean because of the MP3 shutdown leak warning. |

**VERIFIED:** Current Jira requirements read before and after implementation; all six production defaults and shared configuration path; full solution checks; 175 final native oval assertions; 30/144 FPS vehicle replays; native Reset/Apply/replication/restart checks; impaired two-process tests; rendered side/banked views; current-main ancestry and version 0.1.7; complete Story diff and dependency direction.

**INFERRED:** Removing premature body contact and softening the critically damped vertical response should make normal transition handling more forgiving. Reduced steering onset should improve novice control. Neither inference substitutes for human driving feedback. The source review did not establish a tire/differential defect behind the user's exact neutral-throttle spin.

**UNVERIFIED / verification limitations:** Human driving and fun, exact original spin reproduction, physical controllers, large-player-count soak and Internet EOS/NAT. The final network run has a startup correction spike; the startup checker fails its no-warning gate on two MP3 objects. The infield remains Concrete physics. All automated driving used the committed production defaults, not temporary tuning overrides.

The implementation has been checked against current TS-73, including the approved runtime-feedback requirements. The human-driving/subjective acceptance criteria are not closed. Per [the mandatory human gate](../critique.md#mandatory-human-gate), stop after presenting this review. The correction is committed locally on `ts-73-pg`; renewed acceptance is required before updating the existing final PR under [workflow](../workflow.md#critique-and-final-pr).
