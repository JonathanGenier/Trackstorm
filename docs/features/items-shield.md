# Items Shield

Read the [shared system contract](items.md) first. This reference contains only the selected subsystem; preserve its shared ownership and integration requirements.

## Shield rear armor and persistent health

Shield is canonical in the item registry, code, configuration, journal causes,
resources and verification harnesses. `HeldItem.Shield` retains numeric wire identity
8; item protocol 21 and configuration protocol 41 retain their binary layouts and
catalog ordering. Historical host seed/export names are accepted only by the
configuration readers, then become canonical Shield keys. Historical structured
item/developer journal causes are normalized on event decoding. Live edits and new
exports use Shield exclusively. See [configuration compatibility](developer-options.md#host-local-persistence).

An early use press is retained locally while the selected shield's rack/unfold
animation completes. The production arena reports readiness only for the exact
confirmed life, grant token and selection revision, with the rack raised and
mount progress at one. `VehicleNetworkDriver` then sends one ordinary reliable
use request (or invokes the same host use path locally). Repeated taps coalesce;
releasing the button does not discard the queued press. Simultaneous select/use
selects first and waits for that confirmed shield. Switching, discard, destruction,
death/life change and session recovery cancel pending local intent. A blocked
placement consumes that attempt and requires another press, retaining the item.
Protection begins with accepted selection as before. The queue is presentation
input state, not replicated gameplay state or a new server cooldown; Core still
owns placement validation, consumption, HP and the deployed wall. Direct authority
fixtures without an arena presentation continue to use the existing immediate API.
`check-shield-presentation.ps1 -Impaired` exercises this flow over local UDP
with 30 ms delay, 5 ms jitter and 2% loss.

Slot selection atomically exposes the selected Shield as `RearShield` and stows unselected Shields as `Held`. Acquisition into the selected slot exposes it immediately; acquisition into the other slot stores it. No use press is required. Switching away removes rear protection and presents the newly selected item; switching back exposes the same identity with its existing HP and damage/collision watermarks. Both states reserve the physical slot. One normal use press while selected deploys that same pool as a world wall and immediately clears its exact physical slot. Switching away before the use commits stows it and cancels deployment; failed native placement retains the rear shield and capability for a later press.

`ShieldGeometry` defines three full-height armor boxes: a 3.7 × 2.5 × 0.28 m rear panel centered at local `(0, .25, 3.25)`, plus 0.28 × 2.5 × 1.45 m side wings centered at `(±1.85, .25, 2.525)`. The wings wrap forward around the rear quarters; the interior and forward sides remain open. The panels follow the complete authoritative orientation, including pitch/roll. Core tests segments against their union using the current observation batch; native world/chassis hits still win when closer. Machine Gun rounds stop at the first shield. Missile/Salvo segments can impact it, and each radial target's center path is tested against its own shield before applying that target's unchanged radial damage. Proxy Mine contact paths likewise test the attached shield. An intercepted hit spends shield HP and suppresses that target's vehicle damage/weapon impulse, including the lethal shield hit; subsequent hits reach the now-exposed car. Other blast targets retain their ordinary damage. This is finite spatial cover, not a rear-angle immunity rule, and does not add general wall occlusion to explosions.

Native contact points inside the armor envelope route the existing collision severity and host damage tuning to the shield. A collision reported by the striking car also reaches the stationary defender's shield. Multiple manifold/slide observations collapse to the strongest shield contact for the step, with the existing collision cooldown retained per pool through checkpoints. The attacking car keeps its own normal collision damage. Damage uses the same evaluator as `DamageShield`; candidate changes, destroyed-slot cleanup and the single destruction journal entry commit only after the vehicle step succeeds. Sequential world ticks, capability validation, per-entity ordered damage and collision cooldown prevent replay or manifold duplication. When both slots contain Shields, only the selected pool is exposed and receives intercepted damage; the other remains stored.

Client reconstructs those three native collision shapes and the Blender-authored Carnage Circus armor under the interpolated production Car transform. Crimson/ivory stripes, painted grin, welded chevron, brass stars, scraping shoes and caged amber lamps identify the same model mounted and deployed. Its wings wrap forward while mounted and rotate outward to a flat wall. A separate carriage attaches at the production rack's rear socket pair and appears with the compact shield as the cleared rack starts lifting. Core remains the hit authority; the local input path queues early deployment intent until the confirmed selected shield finishes unfolding. Native weapon queries temporarily omit all three reconstructed shield shapes so same-step destruction cannot leave phantom cover. Accepted lifecycle/item state removes them on death, reset, destruction and permanent departure, and reinstalls them after retained recovery. Bevels, lamps and the shallow peaked trim are cosmetic details on conservative panel boxes. [Shield asset notes](../../assets/items/shield/README.md) define editable source, pivots and clearances.

The mounted armor's own sweep ignores contacted driveable support (`landing_terrain`
or legacy `SurfaceBody`), so ordinary slope contact
does not snag the shield or submit armor-ground damage observations. A separate
chassis sweep always retains ground contact. Solid obstacles, other vehicles,
weapon interception and deployed-wall physics retain their existing paths. This
is ground-skimming behavior; the low visual edge may intersect a sharp terrain
transition. Terrain exclusions apply to the contacted body's RID for that sweep;
other faces of the same terrain body remain covered by the chassis query.
The armor filter deliberately ignores sweep-normal direction: concave road seams
can report lateral separating axes even on driveable asphalt. All faces of a
tagged support body are skimmable by the armor; separately authored barriers and
obstacles still block it. Chassis contact classification is unchanged.
`check-shield-presentation.ps1 -ProductionTrack -GodotPath <exe> [-Visual]`
exercises ordinary input across the actual west-bank track join with two peers.
Both presentation modes also compare mounted/unmounted native responses against
sampled production track triangles, including pitched poses that exposed seam snagging.

`check-rear-shield.ps1 -GodotPath <exe> [-Impaired] [-Visual]` runs two production arenas over real local UDP, with independent shields, repeated ordinary selection/stow input from both peers without use, stationary/moving fire, inner/outer rear-quarter edge shots, exposed side/front shots, shield destruction while driving, and a native vehicle collision into the rear plate. Temporary fixture poses isolate shot geometry; moving scenarios advance native physics. Rendered captures use a fixed observation camera. Impairment adds 30 ms outbound delay, 5 ms jitter and 2% loss. Core tests additionally cover rejected-batch atomicity, exact slot retention, collision cooldown restoration, rotated geometry and missile interception. `check-shield-presentation.ps1` also runs native ground/obstacle comparisons and ordinary driving over a twenty-degree climb, crest and descent on two UDP peers.

Shield is registered as a Droppable (wire identity 8) in the existing two-slot inventory. Acquisition creates one match-unique live entity with **1000 HP**, independent of vehicle HP or vehicle-health tuning. Its normal default within-category spawn weight is **1**, using the existing registry-generated configuration and authoritative weighted pickup flow. The shared [Item HUD](hud.md) presents each held/rear-mounted pool with its own shield icon, HP readout and armor plates; released walls no longer belong to a HUD slot.

`ItemAuthority` owns the complete live Shield set, bounded at sixteen including stored/exposed items and released walls. Each pool retains its original entity ID, HP and ordered host-damage watermark. Immutable `ShieldState` records carry attachment life/capability or the installed wall pose. Attached `Held <-> RearShield` state follows accepted slot selection; publications reject exposure that disagrees with ownership/selection. `TransitionShield` remains a host-only recovery fixture seam. Ordinary selected use stages `RearShield -> WorldWall` inside the same transaction as vehicle simulation, slot clearing and the use event. Rejected world steps commit none of those changes.

`DamageShield` accepts a positive finite host-observed amount and a monotonic sequence scoped to that persistent entity, using the shared `VehicleHealth` clamping and destruction semantics on an independent pool. It returns the actual `DamageEvent` without changing vehicle health, kill attribution or player scoring. Host adapters must allocate ordered damage-observation sequences; neither HP nor damage/transition outcomes are accepted in client packets. Duplicate/older sequences are ignored, including after recovery. At zero HP the entity leaves the complete live set, its attached slot clears and one committed Shield destruction event is recorded. Absence is the existing authoritative item-destruction representation; no dead pool is retained or resurrected. Further damage/transitions cannot recreate it. A later acquisition is a new entity with a fresh ID and pool.

Complete item protocol version twenty-one publishes health, stage, entity ID, attachment capability, installed pose, damage watermark and last damaging collision tick through the existing reliable item envelope and nested admission/resume/migration checkpoints. Publications validate each attached pool against exactly one matching inventory slot and reject malformed health, stage, pose, ownership, count and cross-entity IDs. Authority restoration checks the existing token high-water bound, emits no historical destruction and retains damage replay memory. Selection changes, checkpoint installation and lifecycle transitions cannot refill a pool.

Attached Shields follow existing inventory policy: ordinary death/reset/permanent departure removes them, temporary disconnect retains them, and optional retained respawn changes life/capability while preserving ID, HP, stage and watermark. Released walls survive owner death/departure. Finished clears all live Shields and attached Shield slots; new matches start empty.

`check-shield.ps1 -GodotPath <exe> [-Impaired]` runs production drivers over three real local UDP peers, exercising four independent pools, state/slot transitions, repeated damage/destruction, late admission and full authority restoration. Impairment is 30 ms outbound delay, 5 ms jitter and 2% native packet loss. The production reconnect fixture retains a damaged rear shield and world wall; migration retains two damaged world walls. These preserve exact ownership/pose and replay memory. The separate rear-shield harness exercises native blocking and temporary presentation; these checks do not establish remote EOS, independent-device performance or competitive balance.

<a id="movable-shield-world-walls"></a>

## Movable Shield world walls

Normal use asks the host native adapter for rear ground and full expanded-box clearance. The
adapter projects behind the current heading, samples the center and four footprint ends,
and fits the standing wall to their support plane without changing its horizontal heading.
It searches up to one metre upward for a clear box. Its clearance query synchronously
places the deploying car's native proxy at the current observed pose and excludes only its
consumed mounted armor panels, then restores both pose and panels. This
avoids rejecting fast forward deployment against the car's preceding-frame collider while
still checking the actual chassis and other obstructions. A missing/steep support or blocked volume rejects that use
without consuming the shield. Core also rejects overlapping wall candidates within the same batch, before either native body exists. Deployment retains the entity ID, exact HP, damage sequence and
collision cooldown. It captures horizontal vehicle velocity, wall dimensions and mass; it does
not retain an attachment or dependency on the former owner's vehicle life. The Blender visual
releases from the displayed mounted pose over 0.18 seconds while its full-height hinged wings
open from 90 degrees to a flat wall over 0.42 seconds. The full collision envelope remains active
immediately. Captured dimensions scale the installed visual; configured sizes preserve the
same silhouette rather than authoring a separate model for each tuning combination.

Selected presentation reveals the compact folded stack as soon as the deck clears,
as the rack begins lifting. It rides the rack out of the bay. Each center half and
wing has a top and bottom section, forming eight sections in two rows. The subsequent
1.15-second carriage motion carries the stack aft, pitches it upright, opens the two
center halves around a visible central hinge, unfolds the side wings, then opens
all four horizontal hinges before pulling the shield into rear position. Center
tops fold backward and wing tops forward to nest the rows. Deselection reverses
this sequence and holds the rack open until the shield is nested. All panel
folding keeps full constant size. Inventory packing stays at 0.42 scale during rack
rise and initial aft travel, reaching full size behind the Car before the center
hinge opens; it does not represent exact mechanical packing. Accepted protection still
starts/stops immediately with selection. Early player use waits for the completed
mount animation, then releases through existing authority. The wall still inherits
the displayed pose/folds for continuity; the empty carriage returns independently. Rapid switching,
duplicate identities and remote publications share the same reconstructable path.

`ShieldVisual` owns per-instance accepted-HP weathering, impact scars, a brief hit flash
and bounded sparks. Switching stored identities reconstructs their current damage without
replaying an old impact. Late admission and explicit reseeding install a fully expanded wall,
without replaying release. Ordinary world-wall removal detaches a non-colliding visual for an
0.8-second panel breakup; this also represents expiry. At most 32 such world effects coexist.
Reseeding clears those effects. Mounted destruction is shown only when the identity disappears
while its owner remains alive in the same life; switching, deployment and death are not shield
destruction. These effects add no authoritative state, particles with collision, or network fields.

`check-shield-presentation.ps1 -GodotPath <exe> [-Visual]` drives two production arenas over
local UDP and checks mounted/rack state, duplicate inventory, selection, health persistence,
intermediate expansion, remote deployment, simultaneous independent damage, destruction,
repeated deployment, reacquisition, chase-camera driving and landing. Conservative imported
mesh bounds check clearance from articulated rear tires and trunk lids throughout the captured
commands. The opt-in `shield_playtest.tscn` also accepts
bounded JSON input/camera/damage fixture commands in `.godot/ts-219/playtest/input.json`; captures
and traces identify each command. Run only one instance per workspace. This supplements the
rear-shield, world-wall, vehicle/articulation and recovery checks; it does not establish physical
controller ergonomics, real WAN behavior or final competitive balance.

Host Configs exposes `items.shield_width` (6.6 m), `height` (2.5 m), `depth` (0.6 m),
`mass` (250 kg), and `clearance` (1 m behind the chassis envelope). Bounds are respectively
3–12 m, 2–6 m, 0.3–2 m, 50–2000 kg, and 0.5–5 m. Dimensions and mass are captured at
deployment, so later tuning cannot resize an installed collider. Configuration protocol 41
carries these values through the existing validation, publication and recovery path. These
are operational defaults verified on flat/banked native fixtures, not final balance.

`ShieldWorld` reconstructs one rigid body per accepted wall. Only the active host runs its
native solver; Core commits observed pose, linear/angular velocity and independent HP.
Frozen replicas receive the complete accepted state. Vehicle collision queries include wall
layer 32 and retain normal vehicle collision damage. For wall contacts only, Core shares
contact-normal momentum using the car and captured wall masses plus the wall box inertia, so a car pushes the
wall while slowing instead of retaining the generic sweep's stationary-obstacle stop.
Distinct manifold points form one contact centroid per wall; the contact offset produces
angular momentum as well as translation. Duplicate points cannot add duplicate torque.
Unrelated blocking contacts keep their resolved motion. Shield contacts bypass the
native adapter's additional fixed-prop pitch/roll kick: applying it before restoring shared
horizontal momentum could drive the chassis into the floor and launch it through suspension.
Native Shield side-contact normals use the same horizontal plane as that Core response,
so a banked side face cannot turn a sustained push into vertical lift. Top contacts and
terrain support retain their existing normals.
General car handling, suspension, tuning and other collision responses are unchanged. Writing an unchanged host
observation back to the solver is skipped to preserve pending contact impulses.
Standing walls use friction 0.6 and linear damping 2 so they settle after an unpowered push rather
than coasting across the arena. Their captured mass and contact momentum sharing remain
unchanged, allowing a moving car to keep pushing them. Before a strong vehicle impact,
native pitch/roll torque is constrained;
terrain support controls their slope-relative orientation and height as they slide. Horizontal
heading is preserved through ground alignment, while contact torque may rotate yaw freely.
Unsupported or airborne walls retain gravity. A rigid base cannot bend around a crest: it
rests above the highest sampled support, and placement requires all five supported samples.
Walls collide with
terrain/vehicles/other walls and survive the deployer's death, respawn or departure until
expiry/destruction. Inactive authority freezes the bodies.

When a vehicle contact's impulse divided by wall mass reaches `items.shield_tip_speed`
(default 20 m/s, range 1–100), Core permanently marks that wall as tipping. Its native
friction/damping return to 0.08/0.2 to preserve the fall and ground-contact response. Its
pitch/roll locks and ground stabilization release, and the contact offset supplies physical
angular momentum. A tipping wall leaves vehicle collision/query layers on every peer so its
rotating face cannot become a ramp under the car or its wheel rays. It retains native terrain
and standing-wall contact, and Core weapon intersections remain active until destruction.
Core destroys the remaining pool once native walkable-ground contact
coincides with the wall up-axis falling below a 0.2 dot product with that contact normal
(about 78.5 degrees from upright). Airborne tilt and ordinary slope alignment cannot alone
break it. The normal destruction path removes its collider and records the terminal event.

`items.shield_lifetime` defaults to 120 seconds (range 1–600). Successful deployment
captures an absolute host simulation-tick deadline; retuning, owner death/departure and
recovery cannot restart it. Expiry atomically removes the independent wall and records an
Expired event without touching the former owner's new inventory. Held/rear shields have
no deployment timer.

Core oriented-box intersections stop Machine Gun, Missile and Salvo segments at the closest
wall, comparing against native terrain/chassis hits. Native weapon queries omit layer 32 so
same-step destruction cannot leave phantom cover. Bullets damage the struck pool. Explosions
damage/impulse each wall once independently of the number of vehicles in the blast; the
existing non-occluded vehicle radial explosion rule is preserved. Vehicle contacts name the
struck wall; strongest manifold severity damages it once, with the shared default collision
cooldown on the independent pool. Lethal damage journals one destruction and removes the
body through complete-state absence, without touching the former owner's new inventory.

Item protocol 21 and nested checkpoints retain dimensions, mass, pose, both velocities,
identity, HP, expiry deadline, tipping state and replay watermarks. Live motion uses the existing reliable item channel,
including its delayed-delivery limitations. Resume/migration rebuild native bodies from the
selected boundary without replaying use or destruction. Native solver contact caches and
cosmetic expansion progress are not serialized; recovered walls appear fully expanded.

`check-world-wall.ps1 -GodotPath <exe> [-Impaired] [-Visual]` exercises two production UDP
arenas, stationary/60 m/s forward/reverse/banked deployment of damaged shields, blocked-use
retry with the same capability, retired-use rejection,
drive-away independence, retained release facing, bank alignment/elevation following,
native vehicle push with measured slowdown and damage, bounded three-second unpowered coast
after an 8 m/s push, fresh third-peer admission, sustained
Machine Gun destruction, native Missile/Salvo impacts and exact publication-boundary comparisons. It also deploys sixteen
walls through ordinary use and sustains their native state for 600 frames. Rendered runs
capture expanded, pushed, destroyed and stress states. Every live wall is checked for upright
orientation before tipping throughout the run. An off-centre vehicle strike must rotate
the wall without artificial torque. A hard native vehicle strike must tip it and break it
on ground contact. Ordinary banked contact and five hard strikes (host/remote, centred/offset,
40/60 m/s setup speeds, reverse-facing and banked) measure support-relative car rise and
upward speed; hard strikes continue for three seconds without resetting the car. A short
configured lifetime verifies exact expiry and peer cleanup. Missile/Salvo target fixtures reposition the surviving
wall onto level support after the motion test; they do not establish moving-target accuracy.
Add `-ProductionMap` to exercise sixteen normal-use deployments at
all eight oval grid spawns and the seven authored infield pickup areas, including impaired
UDP convergence. That fixture suppresses pickup acquisition to isolate exact-slot assertions.
`check-reconnect.ps1 -Shield` and
`check-migration.ps1` compare complete retained wall state against the exact selected host
boundary; deterministic tests additionally exercise Missile/Salvo wall damage and impulse.
These fixtures do not establish Internet/EOS multi-PC performance, final art or balance.
