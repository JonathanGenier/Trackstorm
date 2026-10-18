# EMP Claw presentation

The EMP cannon and four-jawed clown device are Blender-authored production
presentation assets. Their [editable source, pivots and provenance](../../assets/items/emp-claw/README.md)
are separate from combat authority. EMP Claw is a registered Weapon-category pickup
in the existing nine-item roster. The host grants a minimal three-shot presentation
load through the ordinary item authority. Homing, damaging projectiles, vulnerable
noses, EMP impairment, shield interactions and destructible stored ammunition
remain outside this module.

## Normal pickup and testing

In Developer Options → Configs → Item spawns, set `spawns.emp_claw_weight` positive
and every other item weight to zero. Keep the Weapon category weight positive.
Drive through any existing available pickup; it grants the actual launcher and
the ordinary HUD displays its icon and remaining count. The existing host developer
grant also recognizes EMP Claw. No additional spawn manager or marker is introduced.

The **EMP Claw presentation** configuration category exposes
`items.emp_claw_deploy_seconds` (default 1.2, range 0.8–4),
`items.emp_claw_stow_seconds` (0.82, range 0.5–3), and
`items.emp_claw_interval_ticks` (50, range 15–60). Deployment/return durations are
captured when the launcher installs. Existing spawn cooldown and shared aim controls
also apply. Settings use the existing staged Apply and host replication. Shared
gameplay tuning is session-only and returns to canonical defaults on restart.

`EmpClawLoad` retains a stable magazine identity, remaining count and cooldown in
each physical inventory slot. A successful presentation shot retires its use token;
old/repeated requests cannot spend the next round. The same magazine stays mounted.
Both the host deployment/accepted aim and local mechanical readiness gate use.
Three default shots fit within two seconds; an empty slot can receive a fresh pickup.
Switching preserves its load. Inventory TI28, configuration TC53 (263 values,
2125 bytes), and discovery bucket `trackstorm-lobby-20` exclude older layouts.

Accepted use produces a bounded 0.75-second straight cosmetic flight at 65 m/s.
It performs no hit queries and has no combat effect. Landed/activated/expired/pop
states are inspectable in the preview below; actual target attachment and gameplay
transitions still require the later EMP Stories.

## Rack integration

The ordinary `CarRackPresentation.Observe` maps the accepted selected `EmpClawLoad`
to `EmpClawRackState`: stable magazine identity, tick, three-bit remaining-round
mask, shot sequence and fired-round index. The direct `ObserveEmpClaw` seam remains
available for fixture states and later integrations. Bits identify fixed sockets;
rendering never
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
result, scoped to matching life/token. It uses the existing camera, aiming input,
deployed pivot, accepted yaw/pitch and reticle. Its ready state participates in the
ordinary local use gate, alongside the independent authoritative readiness gate.

Accepted shot-sequence changes produce one 0.20-second barrel response and a
0.34-second three-ring compressed-air discharge with a brief pressure-mist puff. A 0.22-second feed sled withdraws
the next surviving round behind the rack, lifts it and transfers it to the breech.
Only that loading round leaves storage; other rounds remain fixed beneath the rack.
This presentation can follow shots one
second apart without serializing world claw views. Duplicate publications and
reconstruction do not replay a shot.

## Claw and effects

`EmpClawPresentation` is an arena-owned collection accepting complete `EmpClawView`
snapshots (ID, resolved pose and cosmetic state). The future adapter resolves
authoritative flight and target-local attachment transforms before submission.
This snapshot adapter makes no trajectory, raycast, attachment decision or timer-authority claim.
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

Each claw has 120 fine white-blue segments, a separate 120-segment blue halo,
short forked branches and 32 impact/pop flecks; inactive batches draw zero instances.
A launcher has three reusable pressure rings and 18 pressure-mist sprites. Resource
ownership follows the claw/launcher rather than persistent global effects.

## Verification and preview

`check-emp-claw.ps1 -GodotPath <Godot .NET editor> -Visual` renders production Cars
with two real UDP peers first: actual EMP-only marker pickup, configuration replication,
normal use, three-shot cadence, repeated pickup, switching and final trunk closure.
It also instantiates the production Developer Options controls. The subsequent
fixture measures imported stowed vertices,
exercises ordered deployment, full yaw and vertical/shared-clearance aim, three
accepted shot cues at 0/1/2 seconds, exact depletion, duplicate suppression,
multiple independent claws, terminal cleanup, reconstruction, repeated reversal,
Wrench handoffs and life reset. Captures/logs are under `.godot/ts296-round2/`.

Add `-Preview` for interactive visual inspection: RMB/mouse orbit and shared aim,
wheel zoom, Space accepted shot fixture, E deploy/stow, R fresh capability fixture,
1–6 claw state, Escape exit. These controls belong only to the verification scene.
The preview does not establish real EMP gameplay, network replication or combat
acceptance. The normal rack harness remains the native two-peer regression check.

`check-developer-options.ps1 -GodotPath <Godot .NET editor> -Visual -EmpClawOnly`
exercises the actual EMP search/edit/Apply/invalid/Cancel/category-reset controls,
EMP-only weights, and canonical defaults after a separate-process restart.
