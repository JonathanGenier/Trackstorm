# TS-141 independent engineering review

**PASS**

Reviewed production commit `388264fcec884061ae7cc6f511000e7233a64c85` on
`ts-141-pg` against main `5aa629d049fc6d7bb6f195f6a89558a83c98b098` on
2026-09-23. Read the complete Jira TS-141 description and its comment (no
subtasks), repository routing/workflow/standards, and relevant vehicle,
simulation, replication and migration contracts. This is an independent
engineering review; the separate Astra review owns runtime/experiential judgment.

No material correctness, architecture, scope or maintainability findings.

- Landing decisions remain in engine-independent Core. Client supplies tagged
  terrain, normals and local contact positions through both production adapters.
  The change does not alter movement forces or TS-75 terrain geometry.
- Forgiveness is restricted to terrain contacts within the local attitude and
  underside envelope. It occurs before strongest-contact selection, so excluded
  landing contacts consume neither damage nor cooldown. Obstacles, vehicles and
  unsafe body contacts retain the existing damage path. Crash continuation
  remains latched through unsupported bounces until stable support returns.
- The recovery window is bounded. Reset, respawn and destruction clear the
  episode; retuning and terminal-match reconstruction preserve continuation.
  Existing severity/scaling, attribution, HP, collision gates and downstream
  committed-event/scoring ownership are unchanged.
- Aggregate v3 and vehicle protocol v8 serialize and validate the new memory.
  Checkpoint paths use the complete aggregate codec. Prediction preserves the
  latest confirmed landing and damage state while predicting movement only.
  Inspected all Core snapshot construction sites for lost continuation.
- Deterministic tests cover yaw, bank-relative attitude, crash entry, secondary
  impacts/cooldown, mixed wall contacts, expiry, reset, codec round trips,
  restoration, malformed memory and prediction. Native fixtures exercise both
  actual adapters, including underside bottom-out and controlled tumble; the
  named infield traversals use the unchanged authored jumps. Test scope is
  proportionate to the change, without weakening existing assertions.

Reviewer execution: the Release `LandingTests|VehicleAuthorityTests` selection
passed **32/32** using the existing completed build; `tools/check-version.ps1`
passed with canonical **0.1.36** and file/product **0.1.36.0**. Main is an ancestor.
The final working-tree diff check passed after the delivery owner normalized a
trailing blank line in the startup log. No implementation was changed by review.

Reviewed the recorded integrated evidence: 556 Core and 352 non-native transport
tests, 30 landing cases, six actual jump traversals, vehicle/oval checks,
separate-process impaired replication and native migration. These are
implementation-run results, not reviewer reruns. The original eight-peer stale
unreliable-packet assertion failure and passing isolated retry remain disclosed;
loss of the injected packet is plausible but not proven by a delivery trace.
This does not identify a landing-code regression.

This verdict does not claim authenticated EOS, exported builds, remote-device
play, exhaustive threshold-boundary feel or a clean CI run. Current CI and final
PR/report maintenance remain the delivery owner's checks. Pre-existing local
InputButtons.cs and grass_variation.png.import edits were excluded. The passing
engineering verdict is not human critique acceptance or merge authorization.
