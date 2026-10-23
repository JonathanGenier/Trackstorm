# EMP Claw flight, attachment and EMP effect

The EMP cannon and four-jawed clown device are Blender-authored production
presentation assets. Their [editable source, pivots and provenance](../../assets/items/emp-claw/README.md)
are separate from combat authority. EMP Claw is a registered Weapon-category pickup
in the existing nine-item roster. The host grants a three-shot load through the
ordinary item authority. Guided flight, physical attachment and EMP impairment are
server-authoritative. Inflated attached noses and all three exposed stored rounds are independently
destructible. Shield contacts pop a claw without applying EMP or pop damage.

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
old/repeated requests cannot spend the next round. A three-bit live socket mask
preserves each round through shooting, destruction and recovery. Shot count and
last-fired socket are separate from depletion, so destruction never replays firing. The same magazine stays mounted.
Both the host deployment/accepted aim and local mechanical readiness gate use.
Three default shots fit within two seconds; an empty slot can receive a fresh pickup.
Switching preserves its load. Inventory TI31, movement version 14, configuration TC54 (280 values,
2261 bytes), and discovery bucket `trackstorm-lobby-23` exclude older layouts.

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
hit position/normal to the attachment owner in the same atomic item step. Eligible
unshielded contact creates an independently identified attachment without damage.
World and shield collisions retire flight without explosion. Contact sparks are cosmetic. Flight has no
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

## Attachment authority and vehicle power

`ItemAuthority` owns `EmpClawAttachment`: fired identity, original owner, target
vehicle/life, unchanged target-local collision position/normal, attachment tick and
absolute expiry tick. Side, front, rear, roof and underside use the same handoff;
there is no relocation to a preferred face. Each attachment lasts exactly 1,800
fixed ticks (30 seconds). A subsequent hit never changes any older deadline.

EMP sweeps query a cached native triangle BVH of the authored rigid bodywork,
including hood, glass, armor and underbody. The convex movement hull is intentionally
coarse and is not an attachment surface. Wheel/suspension rigs, moving trunk lids
and the weapon rack remain presentation and are excluded; a ray can continue to
bodywork behind them. World/prop obstruction and owner exclusion still apply.
The query receives the same solved vehicle observations that Core uses to convert
the impact to local coordinates, avoiding a one-tick offset on moving/rotating cars.
The native surface supplies the actual point and face normal; presentation never
relocates the anchor. Standard weapon and movement collision queries are unchanged.

One, two or three claws apply the same 80% engine-power suppression. The item step
passes the latest surviving deadline to the existing `VehicleMovement`; engine and
reverse propulsion receive a 0.2 multiplier **after** tire-force saturation, with
lateral steering and brake-only force retained. Nitro rocket thrust receives the
same multiplier on ground and in air. Steering, brake forces, health, external
impulses and existing momentum retain their ordinary
rules. Available tire traction bounds normal propulsion before suppression; net
speed change also depends on drag, slope, load transfer and current speed. Nitro
charge consumption and its speed ceiling are unchanged, but its acceleration cannot bypass
the power multiplier. This is a force limitation, not an instant speed clamp.

Expiry and queued removal are staged before movement at the same atomic boundary.
Full power returns on the exact tick when the final identity ends. The host-only
`RemoveEmpAttachment(id)` seam removes just that identity on the next fixed step;
duplicate requests are harmless. Production counterplay uses the same atomic
attachment membership: only the inflated membrane is intersected by gunfire and
swept against native world, prop and other-vehicle shapes. Metal/jaw contact
never pops a nose. The attached vehicle is excluded from its own nose sweep.
Roof, underside and rotated anchors retain the original local point and normal.
The authored 1.5-scale claw is preserved when aligning its contact anchor; the
inflated membrane has a 0.195 m radius and lies 0.33 m outward from that anchor.
A pop deals zero damage, removes only that identity and deadline, and releases
its model. Other claws retain their deadlines and the same capped suppression.
New attachments already touching scenery pop before any EMP is applied.

The match allows at most 64 attached plus in-flight EMP identities; firing reserves
capacity before spending ammunition. Final target departure, target death/new life,
explicit reset and match finish remove the affected anchors. Attached hazards
survive their shooter's later death or departure. Transient transport suspension
keeps the target's existing life and the original deadlines continue advancing;
resume does not refresh them. Arena destruction releases all owned presentation.

Reliable item boundaries carry complete membership; late join, reconnect and host
recovery preserve every original anchor and deadline. Movement carries the latest
active expiry for prediction/replay between boundaries, clearing it at its deadline
or an authoritative zero. Recovery seeds current models without replaying impact
sparks. A missing/dead/new-life target hides stale presentation immediately.

`EmpClawAttachments` resolves the authored metal attachment anchor against the same
rendered vehicle transform used by the car, including interpolation, roll and flip.
New contact closes all four jaws, emits grip sparks and inflates the red nose. One
shared vehicle effect contains eight short branching blue arcs, with 96 core and
96 halo segments regardless of claw count. It follows side panels, roof/hood,
underside and the rear panel for chase-camera visibility; each claw retains its
local electricity. Natural expiry independently
deflates the nose, fizzles, releases and falls away without damage or explosion.
Released models are bounded and reclaimed after their 1.3-second cosmetic tail.

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
The authored two-link loader stays parked below the magazine so every live nose
remains exposed throughout the pressure cycle. The cannon briefly centers
on its fixed axis and reacquires camera aim after the captured cooldown. Other reserves
stay fixed. The receiver and telescopic sleeves remain hollow. The rear pressure
door closes the receiver; no live ammunition is hidden in the bore. An enlarged
tank with straps and plumbing sits beneath the fixed rack, ahead of the reserves.

Local readiness remains false until the configured pressure cycle completes and
camera aim is reacquired. Core independently gates the captured
cooldown and three-round capability. A switch waits for the active pressure cycle to finish
before lowering the rack. Reconstruction with a pending cooldown redeploys first,
then retains the remaining pressure cooldown without replaying discharge. No ammo is created. Default normal-use cadence remains under
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

The original rigid loader and its telescoping sections stay parked beneath the
exposed socket bank. Shot cadence and discharge remain independent of resource
destruction; an empty mask returns the cannon and requires another ordinary pickup.
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

`check-emp-claw-attachments.ps1 -GodotPath <editor> [-Visual]` uses the same three
impaired native UDP peers for six rotated contact faces, one/two/three independent
anchors, exact staggered deadlines, checkpoint reconstruction and repeated cleanup.
It measures rendered-anchor error through controlled moving/rolling poses, then
runs real throttle/turn/Nitro input and an initial angular-velocity tumble fixture
through production movement and native collision queries. Captures and JSON go to
`.godot/emp-attachment-checks/round2`. Additional cases compare the coarse hull with
authored bodywork, fire short segments into hood/window/moving surfaces, compare
native host/client/practice-driver launches with zero through three claws, and
exercise death/respawn from a seeded lethal combat effect. The scene's
`-- --emp-power` mode runs the focused launch/readability cases without the longer
flight sequence. Initial flight segments, power-test membership and lethal effects
are fixtures; production authority, movement, replication and presentation process
their consequences.

`check-reconnect.ps1 -GodotPath <editor> -EmpClaw` exercises real interrupted
Direct-IP resume over impaired UDP with six seeded staggered attachments. It checks
unchanged anchors/deadlines and active presentation after a short interruption,
then full power and cleanup after expiry while offline. Trusted identity and initial
membership are fixtures. These automated scenarios are not human playtesting,
authenticated EOS verification or a multi-machine reconnect/latency acceptance claim.

## Nose, shield and stored-ammunition counterplay

The host intersects Machine Gun rays against inflated attached membranes and the
three live exposed stored membranes, choosing the closest hit before native cover
and shield interception. Only the first membrane is consumed by a bullet. Neither
metal claw bodies nor in-flight projectiles participate. A stored nose has a
0.075 m radius; fixed rear-facing sockets share `EmpClawGeometry` between Core
hit tests and Client presentation. Selection and deployment gate stored exposure.
Fired and destroyed rounds clear their original bits. Destroying the final round
clears the held item, retracts its cannon and cannot reload without a new pickup.

The existing damaging Missile blast can destroy each stored round inside its
actual radius, subject to existing shield interception. It uses the original
radial damage calculation; cosmetic pops create no secondary blast, projectile,
vehicle damage or impulse. Salvo and other damage systems are unchanged.

Active rear armor and deployed world walls intercept EMP segments through the
existing staged shield geometry. A shield contact produces a cosmetic pop/drop,
no attachment, no EMP and no damage to either HP pool. Shield destruction and
multiple impacts use the same ordered authority batch as other weapons; a shield
already destroyed in that batch cannot protect a later impact. Complete sparse
magazines, independent attachment membership and pop outcomes replicate reliably.
Recovery restores live state without replaying historical pops or firing.

`check-emp-claw-counterplay.ps1 -GodotPath <editor> [-Visual]` exercises rendered
production arenas over three impaired UDP peers: membrane/art alignment, metal-only
contact rejection, selective and simultaneous pops on rotated cars, repeated cleanup,
normal Machine Gun destruction of three individually aimed stored sockets, empty
retraction, simultaneous shield impacts, a native Missile/wall blast, and returning
owner/weapon-ray immunity. Saved attachment/projectile states and fixed car poses
are explicit fixtures; authority, native shape/ray queries, inventory, effects,
replication and presentation are production paths. Evidence is written under
`.godot/emp-counterplay-checks`. This is local UDP automation, not human driving,
authenticated EOS, Internet latency or multi-PC acceptance.
