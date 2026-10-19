# EMP Claw flight and presentation

The EMP cannon and four-jawed clown device are Blender-authored production
presentation assets. Their [editable source, pivots and provenance](../../assets/items/emp-claw/README.md)
are separate from combat authority. EMP Claw is a registered Weapon-category pickup
in the existing nine-item roster. The host grants a minimal three-shot presentation
load through the ordinary item authority. Guided flight and contact handoff are
server-authoritative. Persistent attachment, EMP impairment, vulnerable noses,
shield counterplay and destructible stored ammunition remain future consumers.

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
Switching preserves its load. Inventory TI29, configuration TC54 (280 values,
2261 bytes), and discovery bucket `trackstorm-lobby-21` exclude older layouts.

## Guided flight

Core captures one validated aim-assisted rival and its life at fire time. The
shared aim request carries a candidate ID/life, never an accepted hit: Core
checks eligibility, the accepted forward cone, a 300 m lock bound and native
visibility. Once captured, camera motion, occlusion and nearby rivals cannot
switch that identity. A removed/dead/new-life victim leaves the claw on its
committed direction with terrain response; it never rebinds to a respawn.

Without a valid lock, the first segment follows the accepted firing direction.
Subsequent ticks search living rivals within the forward cone/range and native
visibility. First detection commits identity permanently; simultaneous candidates
use nearest distance then stable vehicle ID. Homing is bounded, with rapid launch
deceleration. Ground support, cliffs and banks reuse `MissileFlight` with separate
EMP tuning. Standard Missile remains non-homing.

Every segment, including pivot-to-muzzle travel, is swept against native scenery
and cars. The original owner is excluded throughout flight, including returning
shots. Another car can physically intercept regardless of the intended victim.
One committed `ItemEvent.EmpClawContact` carries the actual vehicle/life and local
hit position/normal for the subsequent attachment consumer. There is no damage,
impairment, attached timer or balloon counterplay yet. World and shield collisions
retire flight without explosion. Contact sparks are cosmetic. Flight has no
shootable native shape and never participates in weapon damage queries.

Each shot captures an absolute deadline of at most 3,600 ticks. Retuning, recovery
and delayed publication cannot restart it. Owner death/life reset/departure and
match finish remove flights. Weapon switching and empty stow preserve independent
live shots. The existing shared set allows sixteen Missile/Salvo/EMP projectiles;
full capacity rejects use without spending ammo.

Launch, target acquisition, contact and retirement publish reliably. Continuing
motion uses the existing replaceable projectile stream. Complete checkpoints
preserve target/owner lives, velocity and deadline. Client flight interpolation
extrapolates at most 100 ms; missing membership removes the model and electric
trail. The launcher retains its compressed-air discharge and original mechanism.

## Flight tuning

Existing host-validated Configs categories **EMP Claw flight** and **EMP Claw
terrain** use staged Apply, replication and export. Keys have the
`items.emp_claw_` prefix. Lifetime affects new shots. Speed controls (including the launch-phase ceiling)
and guidance controls affect current flight. Standard Missile speed controls never retune an EMP.

| Key suffix | Default | Valid range |
| --- | --- | --- |
| `launch_speed` | 65 m/s (234 km/h) | Above 200 km/h, at most 150 m/s |
| `cruise_speed` | 27.7778 m/s (100 km/h) | 10 to 50 m/s, below launch speed |
| `deceleration` | 75 m/s^2 | 10 to 200 |
| `turn_rate` | 100 deg/s | 1 to 180 |
| `response` | 5/s | 0.1 to 20 |
| `acquisition_degrees` | 18-degree half cone | 1 to 60 |
| `acquisition_range` | 120 m | 5 to 300 |
| `lifetime` | 60 s | 0.1 to 60 |

`terrain_` controls expose clearance 1 m, look-ahead 12 m, vertical detection
range 10 m, reacquisition height 3 m, pitch limit 42 degrees, pitch rate 150 deg/s,
response 16/s, slope limit 40 degrees and cliff tolerance 1 m. Validation is shared
with the Missile terrain envelope, including clearance < reacquisition height <=
detection range. Homing rate and terrain pitch rate are separately bounded
contributions, allowing support correction while pursuing a low car.

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
All hardware remains full size. One round starts inside the chamber; two 50%-larger rear-facing reserves hang beneath the rack. The approved Round 3 layout overrides the reference image�s three stored units. The loader may nest inside the vehicle interior. The magazine, saddle and
vehicle rack never inherit cannon yaw or pitch. Return
centers yaw/pitch above the deck, reverses articulation and closes over 0.82 seconds
plus any centering/final-discharge hold. Mid-deploy cancellation reverses its actual
path. The final accepted shot retains a 0.24-second discharge hold.

The launcher consumes `WeaponAimSolution`, the existing [shared aiming](weapon-aiming.md)
result, scoped to matching life/token. It uses the existing camera, aiming input,
deployed pivot, accepted yaw/pitch and reticle. Its ready state participates in the
ordinary local use gate, alongside the independent authoritative readiness gate.

Accepted shot-sequence changes produce one 0.20-second barrel response and a
0.34-second three-ring compressed-air discharge with a brief pressure-mist puff.
A single two-link arm retrieves the next identified reserve, turns it forward,
opens the hinged rear breech, inserts the actual round, releases its gripper,
withdraws clear, closes the breech and folds into rest. The cannon briefly centers
on the fixed loading axis and reacquires camera aim after closure. Other reserves
stay fixed. The receiver and telescopic sleeves are genuinely hollow; the loaded
round naturally blocks the bore. An enlarged tank with straps and plumbing sits
beneath the fixed rack, ahead of the reserves.

Local readiness remains false until the entire configured cycle completes, the
breech closes and camera aim is reacquired. Core independently gates the captured
cooldown and three-round capability. A switch waits for the active loader to finish
before lowering the rack. Reconstruction with a pending cooldown redeploys first,
then completes a visible reload without replaying discharge; elapsed reloads seed
the chamber directly. No ammo is created. Default normal-use cadence remains under
two seconds for three shots. EMP alone uses a 0.37 m conservative cannon clearance
radius in the shared aiming solver; other weapons retain their existing limits.
Duplicate publications never restart the loader or discharge.

## Claw and effects

`EmpClawPresentation` is an arena-owned collection accepting complete `EmpClawView`
snapshots (ID, resolved pose and cosmetic state). Fixture adapters can resolve target-local attachment transforms before submission.
Production flight uses accepted samples through `EmpClawFlightVisual`.
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
fixture measures imported stowed vertices and folded-cartridge fit, checks an unobstructed empty bore against actual rendered triangles, and verifies reload timing at 30/60/144 FPS across the supported duration range. It
exercises ordered deployment, full yaw and vertical/shared-clearance aim, three
accepted shot cues at 0/1/2 seconds, exact depletion, duplicate suppression,
multiple independent claws, terminal cleanup, reconstruction, repeated reversal,
Wrench handoffs and life reset. Captures/logs are under `.godot/ts296-round4/`.

Add `-Preview` for interactive visual inspection: RMB/mouse orbit and shared aim,
wheel zoom, Space accepted shot fixture, E deploy/stow, R fresh capability fixture,
1–6 claw state, Escape exit. These controls belong only to the verification scene.
The preview does not establish real EMP gameplay, network replication or combat
acceptance. The normal rack harness remains the native two-peer regression check.

`check-developer-options.ps1 -GodotPath <Godot .NET editor> -Visual -EmpClawOnly`
exercises the actual EMP search/edit/Apply/invalid/Cancel/category-reset controls,
EMP-only weights, and canonical defaults after a separate-process restart.

The loader parks wholly beneath the two reserve cradles. Its two rigid telescoping
links use translated inner rails, with a lower-rear mounting crossmember; they do
not stretch their meshes. Retrieval clears the magazine before lifting and turning.
After insertion the wrist withdraws behind the breech, lowers behind the magazine,
then folds forward beneath it. The chamber seat keeps the gripper crossbar outside
the barrel. Fixed services follow the existing in-car storage convention.
Three primary launcher hoses remain: tank feed, left cannon and right cannon.
They terminate on their own fixed or cannon assembly without bridging yaw/lift
joints; cartridge induction cables and metal tank straps are separate details.

`check-emp-claw-flight.ps1 -GodotPath <editor> [-Visual]` exercises three native UDP
arenas under 100 ms delay, 15 ms jitter and 3% loss: target persistence, forward
acquisition, native interception, owner exclusion, weapon-ray immunity, thin walls,
simultaneous players, recovery, expiry and climb/descent/bank fixtures. Car paths
are controlled fixtures, not human driving. The same scene with
`-- --emp-long-flight` checks returning owner immunity and exact 60-second expiry;
`-- --emp-production` drives the real car onto both tabletop approaches and the
outer bank before firing. Evidence goes under `.godot/emp-flight-checks`.
The original presentation harness retains isolated pickup, three-shot cadence and
rack lifecycle coverage. These checks do not establish remote Internet behavior or
physical-controller usability.
