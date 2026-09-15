# TS-55 Direct Displayed Heading — Round 3

## Scope and Current Behavior

Read the full TS-55 Story and all returned fields before implementation. Jira already contains the approved fixed-camera direction, including direct displayed heading, positional inertia, no mouse/right-stick camera input, and no steering-driven yaw. The complete child query returned no children; there are no comments, attachments or linked dependencies. No Jira edit was needed.

Work remains on `ts-55-jg`, synchronized with `origin/main` at `ab3c575`. PR #20 is the existing Story PR. No camera-specific Core, input-frame or network state is introduced.

`VehicleChaseCamera` assigns its yaw directly from the already-interpolated practice pose or network visual pose. Normal driving has no separate rotational interpolation or catch-up period. The obsolete exported rotation-damping setting is removed. Invalid, near-vertical and overturned orientations retain the last usable heading; valid orientation is adopted immediately when it returns. Pitch remains fixed and the horizon stays level.

Measured lateral acceleration and sideways velocity continue to provide bounded positional turning/drift weight. Longitudinal acceleration/braking inertia, vertical positional damping, collision feedback, damage deduplication and life resets are preserved. Inertia and shake do not alter camera aim.

## Regression Coverage

- Replaced eventual heading convergence with one-update checks at 30, 60 and 144 FPS, including large left/right turns and wraparound. Angular tolerance is 0.00001 radians (less than 0.001 degrees). A second update must not change aim.
- Verified the new one-update assertion fails against the old yaw interpolation before changing production code.
- Added a 90-degree displayed-heading change with existing lateral inertia: yaw matches immediately while lateral displacement persists, damps and stays within its cap in the new vehicle frame.
- Added explicit nonfinite, degenerate, vertical and overturned orientation fallback and immediate valid-pose recovery checks.
- Added native contact feedback and decay assertions with unchanged orientation. Existing input-independence, acceleration/braking, damage, reset and airborne checks remain.
- The six deterministic camera cases continue to cover acceleration/braking, lateral direction/slip/bounds, render-rate consistency, collision scaling/coalescing/decay and reset.

## Verification Results

Windows, Godot 4.7.2 .NET, .NET 10; dotnet commands used `DOTNET_PROCESSOR_COUNT=4`. Logs and generated screenshots remain ignored under `.godot/`.

| Check | Result |
| --- | --- |
| `./check.ps1` | PASS: formatting, restore, Debug/Release builds without warnings; 232 Core and 103 Client/transport tests per configuration. |
| Explicit `ChaseCameraMotionTests` subset | PASS: all six deterministic cases. |
| Native `camera_checks.tscn` | PASS: direct heading, no catch-up, retained lateral inertia, fallback/recovery, input independence, acceleration/braking, collision/damage feedback and lifecycle reset. |
| Native `input_checks.tscn` | PASS: 149 assertions. |
| `check-vehicle.ps1 -Visual` | PASS: 116 assertions at each of 30/144 FPS; movement and surface traces match within 0.02; rendered run passes 131 assertions. Inspected cornering, drift and recovery screenshots. |
| `check-network-vehicles.ps1 -NoBuild` | PASS: 351 client snapshots, 644 immediate frames, p99 correction 0.0345881 m, maximum 0.2461168 m, zero large corrections and zero prop replica error. |
| Project import and practice startup/exit | PASS without warnings/errors. |
| Complete Story diff and whitespace inspection | PASS: no obsolete rotational-damping property/configuration, Core changes, unrelated correction changes, generated output or debug artifacts. Current feature documentation matches direct-heading behavior. |

Evidence: `ts55-r3-check.log`, `ts55-r3-camera.log`, `ts55-r3-input.log`, `ts55-r3-vehicle.log`, `ts55-r3-network.log`, `ts55-r3-import.log`, `ts55-r3-startup.log`. The intentionally failing pre-fix regression is `ts55-r3-red.log`. Rendered captures: `vehicle-checks/bb43f6ab763e4e4cab83060c71a14081`.

## Astra Self-Critique — Story Round 3

**Overall Score: 7.0 / 10 — PASS.** Category scores: Functionality 8, Architecture 8, Testing/Reliability 8, Integration 7, Camera 6.5.

**VERIFIED:** the regression fails with the former yaw interpolation and passes with direct displayed heading. Native checks exercise finite fallback, immediate recovery and retained positional effects; scripted physics/network checks pass. Rendered sampled framing keeps the vehicle visible and the horizon level.

**INFERRED:** using the existing displayed pose avoids a second orientation filter in both production paths; the complete Story diff preserves Client-to-Core dependency direction and camera-local ownership.

**UNVERIFIED:** continuous hands-on driving/collision comfort, physical-controller ergonomics, live Inspector tuning, impaired WAN and cross-platform behavior. Screenshots do not prove temporal smoothness. The eight-peer death/respawn suite was not repeated in this round; native camera life/reset checks were. Existing camera obstruction avoidance remains absent. The Jira hands-on completion gate still requires human driving review.

Recommendation: perform a focused driving review of rapid alternating turns, sideways slides and recovery from overturned poses before accepting camera feel. This is a validation recommendation; no further tuning or critique-driven code changes were made.
