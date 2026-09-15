# TS-55 Camera Verification

## Scope and Branch

- Jira Story: [TS-55 — AAA-style third-person vehicle camera](https://cosmoscasino.atlassian.net/browse/TS-55).
- Read the complete Story, all returned fields, comments, links and attachments before implementation; re-read the Story before integrated review.
- Jira returned no subtasks, linked issues, attachments or comments. The complete `parent = TS-55 ORDER BY key ASC` query returned no children and `isLast = true`.
- Implemented on `ts-55-jg`. Initial and final fetches of `origin/main` both resolved to `5f67c37`; the branch base already matched, so no merge was necessary.
- Implementation order: shared presentation math/controller; practice/network integration; local input; contact/damage feedback; automated/native verification; feature documentation; integrated inspection.
- No third-party assets or dependencies introduced. Core and network protocols are unchanged.

## Acceptance-Criteria Evidence

| Story criterion | Evidence and status |
| --- | --- |
| Local chase camera continuously follows vehicle | VERIFIED: shared controller runs in both arenas; practice startup, driving and network harnesses pass. |
| No harsh snapping or physics jitter during normal driving | INFERRED from render-time damping and native-body interpolation; scripted rendered scenarios and stills inspected. Continuous hands-on motion assessment remains UNVERIFIED. |
| Smooth acceleration, braking and heading changes | VERIFIED scripted driving/braking/turning runs; subjective transition quality remains UNVERIFIED. |
| Steering contributes approximately ±10 degrees | VERIFIED pure tests in both directions and native camera checks. |
| Mouse and right stick adjust horizontal camera | VERIFIED synthetic native mouse events passed to the camera callback and buffered native right-stick events through the input adapter. Physical device ergonomics and full-window mouse routing remain UNVERIFIED. |
| Total horizontal look limited to approximately ±20 degrees | VERIFIED pure clamp tests and stationary actual native camera orbit on both sides. Moving follow lag is intentionally separate from this additional look offset. |
| Steering and manual input respect combined limit | VERIFIED sustained extreme input and runtime camera checks. |
| Smooth recenter on release | VERIFIED initial continuity, convergence to steering/neutral and 30/60/144 FPS consistency. |
| Meaningful collision feedback | VERIFIED contact-envelope thresholds/scaling and integrated native collisions; subjective strength/readability remains UNVERIFIED. |
| Damage feedback | VERIFIED accepted Core damage changes the native camera envelope. |
| Shake decays without permanent orientation change | VERIFIED envelope reaches zero; shake never mutates base follow memory. |
| Minor repeated collisions remain controlled | VERIFIED threshold/cooldown/coalescing tests; 172-contact harmless-brush vehicle scenario preserves HP. |
| Readability during rapid steering, collision and airborne instability | VERIFIED finite camera transforms and upright camera basis under synthetic alternating steering/tilted/spinning poses; rendered drift/recovery screenshots inspected. Continuous game-feel assessment remains UNVERIFIED. |
| Important feel values tunable | VERIFIED by exported property/code inspection; defaults documented. Live Inspector tuning session was not performed. |
| Local presentation only; no camera authority/network state | VERIFIED complete diff: no Core changes or protocol additions. |
| Existing movement/prediction/networking preserved | VERIFIED Core tests, matched native movement/surface traces, host/client networking and eight-peer lifecycle regressions. |
| Feature documentation synchronized | VERIFIED new Player Vehicle Chase Camera section covers behavior, controls, tuning, integration and limitations. |

## Commands and Results

Environment: Windows, Godot 4.7.2 .NET, .NET 10; builds used `DOTNET_PROCESSOR_COUNT=4`.

| Check | Result | Local ignored evidence |
| --- | --- | --- |
| `./check.ps1` | PASS: restore, formatting, Debug/Release builds with zero warnings; 213 Core and 100 Client/transport tests per configuration, including six new camera cases. | `.godot/ts55-check.log` |
| Godot `res://scenes/verification/camera_checks.tscn` | PASS, no warnings/errors: camera placement, synthetic look, recenter, settings/focus gates, accepted damage deduplication, new-life reset and tilted/spinning poses. | `.godot/ts55-camera.log` |
| `./check-vehicle.ps1 -GodotPath <exe> -Visual` | PASS: 56 assertions at each of 30/144 FPS, 68 in the rendered run; movement and surface traces match within 0.02. Driving, reverse/braking, drift/boost, launch/landing, collisions, blasts and input/settings exercised. | `.godot/ts55-vehicle.log`; `.godot/vehicle-checks/2ff8e4b4d5d242959bb6adf2a537e9dc` |
| `./check-network-vehicles.ps1 -GodotPath <exe> -NoBuild` | PASS: two native processes; client received 336 snapshots and 652 immediate prediction frames; p99 correction 0 m, maximum 3.849842 m with one large startup correction; prop replica error 0. No claim that every correction is zero. | `.godot/ts55-network.log` |
| `./check-death-respawn.ps1 -GodotPath <exe> -NoBuild` | PASS: four missile/collision death cycles across eight peers, with physics/HP/item/VFX resets. | `.godot/ts55-death.log` |
| Godot `res://scenes/verification/input_checks.tscn` | PASS: 116 native-input assertions. | `.godot/ts55-input.log` |
| `./import-godot.ps1 -GodotPath <exe>` | PASS, no warnings/errors. | `.godot/ts55-import.log` |
| Godot main scene, `--headless --quit-after 120 -- --local-practice` | PASS: actual practice entry and exit, no warnings/errors. | `.godot/ts55-startup.log` |
| Complete diff and `git diff --check` | PASS: only camera/input presentation integration, tests, project interpolation settings and documentation; no generated/debug assets in delivery. | Git diff |

Initial build/style failures and a synthetic-input buffering/reuse warning were fixed before the passing results above.

## Assumptions and Limitations

- Mouse remains uncaptured to preserve the existing development UI. Input stops beyond the viewport or over consuming UI controls, so extended mouse sweeps need practical evaluation.
- The existing prototype has no camera obstruction avoidance. The new camera can be obscured by walls/structures; continuous vehicle visibility is not guaranteed.
- Screenshots establish framing at sampled moments, not temporal smoothness, comfort or AAA-quality feel. Inspected drive, drift, collision and post-blast recovery captures.
- No physical-controller session or continuous hands-on driving/collision playtest was possible with the available native-app controls. Synthetic coverage does not satisfy that subjective completion gate.
- No WAN, cross-platform or additional impaired-network session was performed for this presentation change.
- Current tuning is a starting point. Human driving review is required before accepting the Story for PR creation.
