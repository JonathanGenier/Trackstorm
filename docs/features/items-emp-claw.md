# EMP Claw presentation

The EMP cannon and four-jawed clown device are Blender-authored production
presentation assets. Their [editable source, pivots and provenance](../../assets/items/emp-claw/README.md)
are separate from combat authority. The EMP is not yet registered as a usable
pickup in the eight-item production roster. Homing, ammunition ownership,
vulnerable noses, EMP suppression, shield interactions and multiplayer publication
belong to later gameplay integration; this module implements none of those rules.

## Rack integration

`CarRackPresentation.ObserveEmpClaw` accepts an optional `EmpClawRackState` after
the ordinary `Observe` call for the same life. A future authority adapter supplies
the selected capability, monotonic revision, three-bit remaining-round mask,
shot sequence and original fired-round index. Bits identify fixed round sockets,
so destroying the middle round does not relabel the others. Rendering never
decrements ammunition. Null means removal/deselection, and zero remaining bits
requests an empty return without reloading.

The existing rack owns the entire deployment and return. An outgoing payload
finishes its established return before EMP installs. Once installed, EMP retains
the rack through centering, nesting, lowering and trunk closure before a replacement
can start. Death and life changes reset both immediately. Reconstruction clears
the old instance and seeds the accepted round/shot state without historical firing.

Deployment takes 1.20 seconds: the existing split deck/rack path occupies the first
44%, then the cannon emerges vertically, seats down, and extends its barrel.
All hardware remains full size. The three stored claws hang beneath the rack,
facing rearward as in the approved rear reference. The magazine, saddle and
vehicle rack never inherit cannon yaw or pitch. Return
centers yaw/pitch above the deck, reverses articulation and closes over 0.82 seconds
plus any centering/final-discharge hold. Mid-deploy cancellation reverses its actual
path. The final accepted shot retains a 0.24-second discharge hold.

The launcher consumes `WeaponAimSolution`, the existing [shared aiming](weapon-aiming.md)
result, scoped to matching life/token. It uses the same deployed pivot and accepted
yaw/pitch. This adds no camera, target selector, reticle or authoritative firing gate.
`IsReady` is visual readiness only; subsequent gameplay must retain host validation.

Accepted shot-sequence changes produce one 0.20-second barrel response and a
0.34-second three-ring compressed-air discharge. A 0.22-second feed sled withdraws
the next surviving round behind the rack, lifts it and transfers it to the breech.
Only that loading round leaves storage; other rounds remain fixed beneath the rack.
This presentation can follow shots one
second apart without serializing world claw views. Duplicate publications and
reconstruction do not replay a shot.

## Claw and effects

`EmpClawPresentation` is an arena-owned collection accepting complete `EmpClawView`
snapshots (ID, resolved pose and cosmetic state). The future adapter resolves
authoritative flight and target-local attachment transforms before submission.
There is no local trajectory, raycast, attachment decision or timer authority.
Unique finite snapshots are limited to 64 views. Missing IDs remove their owned
models/effects; reconstruction replaces the set without entry bursts.

Each `EmpClawVisual` presents compact stow, partially opened flight, four-jaw grip,
activated nose inflation, natural deflation/fizzle, or distinct balloon pop/release.
Flight owns six short blue lightning ribbons. Activated ribbons spread from the
claw toward its contact plane; landed entry produces small gold sparks. Expiry
deflates over 0.38 seconds before a cosmetic world-space fall. Pop hides the
membrane immediately and emits red fragments. Both terminal presentations finish
within 1.3 seconds. They neither explode nor damage anything. Independent instances
allow simultaneous flight/attachments and independent terminal cues.

Each claw has a fixed 72-segment arc batch and 16-fleck batch; inactive batches
draw zero instances. A launcher has three reusable pressure rings. Resource
ownership follows the claw/launcher rather than persistent global effects.

## Verification and preview

`check-emp-claw.ps1 -GodotPath <Godot .NET editor> -Visual` renders production Cars
with fixture-supplied presentation states. It measures imported stowed vertices,
exercises ordered deployment, full yaw and vertical/shared-clearance aim, three
accepted shot cues at 0/1/2 seconds, exact depletion, duplicate suppression,
multiple independent claws, terminal cleanup, reconstruction, repeated reversal,
Wrench handoffs and life reset. Captures/logs are under `.godot/ts296/`.

Add `-Preview` for interactive visual inspection: RMB/mouse orbit and shared aim,
wheel zoom, Space accepted shot fixture, E deploy/stow, R fresh capability fixture,
1–6 claw state, Escape exit. These controls belong only to the verification scene.
The preview does not establish real EMP gameplay, network replication or combat
acceptance. The normal rack harness remains the native two-peer regression check.
