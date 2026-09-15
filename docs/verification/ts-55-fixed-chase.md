# TS-55 Fixed Chase Correction — Round 2 Evidence

Historical Round 2 evidence. Its heading interpolation was removed by the [direct-heading correction](ts-55-direct-heading.md), which describes current behavior and verification. Jira now contains the approved fixed-camera requirements; the mismatch described below existed at the time of Round 2.

## Authorized Direction

Read TS-55 completely in Jira and repeated the complete child query before changes. Jira still specifies mouse/right-stick look and steering anticipation; it has no children, comments, linked dependencies or attachments. The user's explicit correction replaces those particular requirements with fixed chase orientation and motion-based positional inertia. All other Story requirements remain applicable. Jira was not edited and the Story is not marked complete.

Work remains on `ts-55-jg`. Both initial and final main fetches confirmed `origin/main` at `5f67c37` was already included; no integration merge was needed. Read the repository workflow, standards, critique policy, Client instructions, current camera implementation and relevant feature documentation.

## Implementation and Requirement Mapping

| Requirement | Result and evidence |
| --- | --- |
| No mouse/right-stick camera control | Removed camera input callbacks, polling, ownership lookup, sensitivity properties and manual-look memory. Native injected input leaves camera transform unchanged in both directions. Right-stick values remain available through Godot input. |
| No steering-driven independent yaw | Removed steering arguments and stored input from both camera integrations. Native checks prove steering input still reaches vehicle controls but cannot affect a fixed-state camera. |
| Actual vehicle orientation determines camera direction | Round 2 checked eventual alignment only. Current yaw follows the displayed heading directly; the Round 3 one-update regression supersedes this insufficient convergence check. Fixed pitch and heading define camera basis; inertia and shake do not feed look-at rotation. |
| Smooth acceleration/braking weight | Measured velocity changes sampled using physics-tick time produce bounded fore/aft offsets, with continuous exponential damping. Pure tests check sign, smooth transition and return to neutral; native checks inspect camera position and actual snapshot sampling. |
| Lateral turn/drift weight without yaw | Lateral acceleration and actual sideways velocity drive an outside-turn offset, capped at 0.4 m by default. Tests verify both directions, slip response, bounds and unchanged native camera aim. No drift-button dependency. |
| Settling and stable recovery | Pure tests compare 30/60/144 FPS results and verify reset behavior. Native checks verify settling within 1 mm and 0.001 degrees, finite transforms under tilted/spinning poses, and new-life resets. |
| Existing collision/damage feedback | Preserved bounded contact impulses, damage sequence deduplication and shake decay. Pure feedback tests and native damage/lifecycle tests pass. |
| Input/vehicle/network behavior unchanged | Only camera-specific additions were removed from input code. No Core or protocol changes. Native input, driving/replay, host/client and eight-peer lifecycle regressions pass. |
| Documentation and tuning | Updated Player Vehicle Chase Camera section in `docs/features.md`; removed current manual-look/steering-yaw descriptions and tuning. Documented inertia gains, caps, damping and architecture. |

## Validation

Windows, Godot 4.7.2 .NET, .NET 10; builds use `DOTNET_PROCESSOR_COUNT=4`. Evidence logs are ignored local artifacts under `.godot`.

- Camera deterministic tests: six cases covering acceleration/braking, both turn directions, sideways motion, bounds, render-rate consistency, decay and reset. Log included in the full check.
- Native `camera_checks.tscn`: PASS with no warnings/errors. Injected mouse/right-stick/steering, heading alignment, inertia without yaw, actual snapshot velocity sampling, damage deduplication, life reset and airborne horizon. Evidence: `ts55-r2-camera.log`.
- `check-vehicle.ps1 -Visual`: PASS; 56 assertions at each of 30 and 144 FPS, 68 in the rendered run, with matching movement and surface traces within 0.02. Evidence: `ts55-r2-vehicle.log`, `vehicle-checks/094005c322a248bbb5b95d52ec681193`. Inspected drift and recovery screenshots.
- `check-network-vehicles.ps1 -NoBuild`: PASS; two native processes. Client received 319 snapshots and produced 650 immediate prediction frames; p99 correction 0.006667 m, maximum 2.173897 m, zero large corrections and zero prop replica error. Evidence: `ts55-r2-network.log`.
- `check-death-respawn.ps1 -NoBuild`: PASS; four missile/collision death cycles across eight peers with physics/HP/items/VFX resets. Evidence: `ts55-r2-death.log`.
- Existing native `input_checks.tscn`: PASS, 116 assertions. Evidence: `ts55-r2-input.log`.
- Production practice startup/exit: PASS, no warnings/errors, using `--headless --quit-after 120 -- --local-practice`. Evidence: `ts55-r2-startup.log`.
- `./check.ps1`: PASS; restore, formatting, Debug/Release builds with zero warnings, 213 Core tests and 100 Client/transport tests per configuration. Evidence: `ts55-r2-check.log`. Subsequent edits only clarified a documentation comment, removed trailing blank lines and recorded this evidence.
- Diff inspection includes the complete integrated Story against main and the correction against Round 1. No Core modifications, camera-input remnants, new dependencies or generated/debug artifacts are included.

Initial test formatting/line-ending diagnostics were corrected. A native settling assertion exposed approximately 0.04 mm of floating-point residue after wraparound; angle normalization and explicit submillimetre/angular tolerances now cover this correctly. No failing result is counted as a pass.

## Limitations and Human Review

- Runtime driving was scripted. Native camera/input assertions and sampled screenshots were observed; continuous hands-on driving comfort and subjective inertia strength remain unverified with the available native-app controls.
- The existing lack of camera obstruction avoidance remains. This correction does not guarantee visibility through nearby walls/structures.
- Inertia responds to measured velocity changes, so network corrections may produce a restrained, bounded positional response. No additional impaired-WAN or cross-platform test was run.
- The camera has fixed FOV. Current gains are defaults exposed for tuning; a live Inspector tuning session was not performed.
- The useful next review is a focused driving playtest of acceleration-to-braking and rapid left/right/sliding transitions, followed by specifically authorized tuning if needed. No automatic next improvement round or PR creation is authorized.

## Post-PR Main Synchronization

At the user's request, merged `origin/main` at `438c223` into the Story branch after PR #20 was created. The merge was conflict-free. TS-54 introduces progressive keyboard steering, so the native camera check now verifies partial initial steering, full held steering, and return to neutral while preserving the fixed-camera input assertions. Production camera behavior was unchanged by this test adjustment.

- `./check.ps1`: PASS; Debug/Release builds without warnings, 230 Core tests and 100 Client/transport tests per configuration.
- Native camera integration: PASS. Native input integration: PASS, 149 assertions.
- `check-vehicle.ps1`: PASS, 116 assertions at each of 30 and 144 FPS; movement and surface replays match within 0.02.
- `check-network-vehicles.ps1 -NoBuild`: PASS; 318 client snapshots, 650 immediate frames, p99 correction 0.021139 m, maximum 0.918774 m, zero large corrections and zero prop replica error.
- Diff whitespace validation: PASS. Local logs: `ts55-main-sync-check.log`, `ts55-main-sync-vehicle.log`, `ts55-main-sync-network.log` under ignored `.godot/`.

This synchronization did not repeat rendered driving, hands-on comfort assessment, or the eight-peer death/respawn suite. Earlier evidence remains historical; the limitations above still apply.
