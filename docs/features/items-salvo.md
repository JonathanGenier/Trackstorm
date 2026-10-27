# Items Salvo

Read the [shared system contract](items.md) first. This reference contains only the selected subsystem; preserve its shared ownership and integration requirements.

## Catapult mechanical presentation

The production [Blender catapult](../../assets/items/salvo/README.md) replaces the
labelled box on the existing Car weapon rack. An outgoing weapon retains the
mechanism until it retracts and the trunk closes. Salvo then uses the ordinary
split-deck/rack lift, extends its forward sliding bearing and overlapping wooden
spoon sections, winches the arm to a five-degree downhill incline and feeds one
of five horizontal rounds into the bowl. Four remain on the handle after loading.
The 1.25-second catapult extension/loading path begins only after the rack clears
the roof. Retraction centers yaw first, completes any release, returns ammunition
to its captive storage positions and nests the arm/mount before lowering the rack.

The fixed winch is inside the rear rack. A captured-post platform lift and overlapping
slide runners connect the rack to the forward slew bearing. The spoon's hinge rises
within telescoping cheeks. The straight rearward support plate connects to a visible
fixed braced bracket on that bearing. It remains level relative to turret yaw;
spoon pitch never drives it. Its telescoping section finishes seating before
cocking starts and moves again only for stow. The spring's lower anchor remains
stationary during cocking, release, recoil and reset.
The six-turn spring has separate rigid seats connected to the plate and spoon
saddles, plus an internal guide. Its authored morph changes pitch without stretching
the wire gauge; the rigid seats pivot about their captive anchor pins as the spring
leans toward the moving spoon. The spring and plate rotate with the turret,
independently of the magazine. Red fixed gussets and gold locking bolts make the
plate's rigid mounting visible without altering the approved spoon or livery.

The thick brown ship rope runs from the drum through a travelling level-wind guide,
up the mount, along the lifting/sliding platform, through an axial tension swivel
and along the spoon underside to a visible spliced eye around a pin behind the
spring, toward the turret. The terminal is attached to the beginning of the outer
spoon member, 0.32 m toward the turret from the spring's upper saddle; it does not
continue to the bowl. The eye and strap remain children of that spoon member.
The rotating fairlead and spoon guide keep the line outside the spring. Seven
normalized mesh spans retain a 66 mm diameter and constant braid texture pitch
throughout deployment and rotation. Coincident anchors hide their zero-length
segment instead of creating a singular transform.

The weapon's original circus livery uses scuffed red enamel on the spoon exterior,
fixed turret/support housings and magazine flanges, bold white stripes and selected
gold stars. The bowl interior remains wood. Bare sliding metal, joints, spring,
brown rope and the existing firecrackers retain contrast. Decoration follows its
authored rigid parent and does not change the vehicle's unrelated materials.

The drum has ten visible windings, crimson rims and asymmetric brass rotation
markers. Its phase derives from routed rope length at a 0.18 m winding radius;
the level wind tracks the authored 52 mm helix pitch. Rope texture moves continuously
along the route with its spliced end fixed. Yaw alone leaves the drum still, while
deployment, release and reset pay out or haul in rope. Reconstruction derives the
same pose without an accumulated phase or additional networking. The stored winding
is a fixed visible rope bed; physical tension and changing spool volume are not
simulated. Each accepted launch deadline gives a rapid
five-tick upward swing, a 45-degree stop with damped recoil, winch return and captive
reload; the presentation cycle lasts 0.48 seconds. Stored rounds and the bowl's loaded round remain attached
to the mechanism during airborne, rolling and inverted vehicle poses.

Salvo uses the existing life/token/selection-validated camera direction and
replaceable accepted-aim publication for full yaw articulation and shared car-only
sticky assistance, without altering shared camera motion. Accepted Salvo token
rotation carries articulation to the successor token; stale-token input remains
invalid. Fresh accepted aim follows the same committed magazine even when reliable
inventory and replaceable aim publications cross at a shot boundary. The normal
0.3-second presentation expiry and vehicle-life checks still apply; new magazines
and other weapons require their exact token. No new packet or inventory authority
is introduced. Both peers derive
mechanical state from accepted selection/count/deadline/use events. Recovery reconstructs
a committed magazine already deployed, without historical firing; an uncommitted
magazine deploys normally. Cosmetic
deployment phases remain local, as with the ordinary rack; exact phase-locking
across late admission is not part of this presentation contract.

`SalvoCatapult.Bowl`, `IsReady`, `PrepareRelease` and `ObserveRounds` connect the
mechanism to authoritative automatic firing. Loading ignites a bounded looping
spark at the captive round's fuse. It waits indefinitely without a timer or damage.
The authority gates activation on completed rack and catapult deployment. A launch
starts at the authored bowl position at the 45-degree stop, transformed by the
current vehicle orientation and accepted yaw. Visible ammunition decreases only
when the authoritative shot launches; the next captive round feeds during reset.

`check-salvo-catapult.ps1 -GodotPath <editor> [-Visual]` covers the actual Car,
two local native UDP peers, full-yaw aim, repeated releases, weapon switching,
airborne/rolled poses, captive rounds, impaired handoffs and reconstruction. It
also samples actual rendered cable endpoints, coil morph/scale, seat attachment
and platform engagement through deployment, eight yaw bearings, use and retraction.
It also checks spool/rope-length agreement, tangent/guide alignment, physical braid
pitch, payout/rewind and no winding from yaw alone. Visual runs record a buffered
winch cycle with frame timestamps and observed drum/spoon/guide poses. The terminal
is also checked beneath the spoon and on the turret side of the upper spring seat.
The audit verifies the lower plate's turret-relative pose throughout operation,
and that its base is fully seated before cocking. A second buffered side-view
cycle records plate pitch, spring-foot position and spring length during firing.
One activation exercises all five mechanical cycles without additional presses.

## Automatic five-shot gameplay

Salvo is a Weapon in the existing registry (wire identity 6). Every normal pickup holds exactly five shots. Once fully loaded, one UseItem activation commits the whole magazine. The first launch follows a five-tick release stroke; subsequent launches are 75 ticks (1.25 seconds) apart by default. Early deployment taps are rejected without queuing. Repeated presses, trigger release, selection and discard cannot interrupt or duplicate a committed volley. A selection command arriving during firing is acknowledged without changing the selected slot or deferring a later switch. The other physical slot remains untouched; the firing slot clears after launch five.

Acceptance reserves all remaining rounds within the shared sixteen-projectile cap. Insufficient capacity preserves the uncommitted magazine. Other projectile weapons respect those reservations, so an accepted volley cannot stall for capacity. Each launch retires its capability and gives remaining ammunition a new token. The existing per-slot absolute ready tick is zero before activation and the next launch deadline after commitment. Counts, deadlines, projectiles and token allocation commit atomically with the world step; failed steps can retry without consumption or duplicate fire. Existing bounded inventory/checkpoint fields carry this state without a new packet.

Each launch independently samples the shared accepted camera/assisted direction, current vehicle pose and deliberate aiming state. Full yaw covers front, sides and rear. Salvo retains the shared angular/pitch limits but does not apply the straight-barrel self-clearance test to its arcing spoon. Native closest cover along the accepted ray wins within reach; otherwise horizontal/downward aim projects onto available static ground. Sky aim or missing ground retains a finite endpoint. Ordinary fire retains the configured base range (65 m by default). Held RMB or existing controller aiming intent initially doubles that range (130 m by default), including shared assist acquisition and the ground circle. This multiplier is initial playtest tuning, not a permanent balance decision. The held-only `AimItem` state travels through existing ordered vehicle input; the authority applies it only with a fresh accepted aiming solution. Missing/expired transient aim falls back to current chassis-forward direction and base range; it never cancels a committed sequence. Previously launched rounds retain their own target, origin, speed and parabola independently of later camera motion.

Shots start at the fully extended bowl's 45-degree release socket. The default launch-height tuning preserves that authored socket; an offset from the default adjusts it along chassis up. The existing parabola rises twelve metres above the launch-to-target chord, with mean travel speed 150 m/s. Complete 60 Hz segment sweeps can hit obstacles/vehicles before the stored target. Each impact has a default 0.9144 m (three-foot) blast radius, 250 maximum HP damage and 3,500 N s impulse, with distance falloff `(1 - distance / radius)^falloff` (default exponent one). The existing vehicle damage transaction and Circus scoring own actual per-target damage, kill attribution and points.

## Explosion, damage and destruction

One accepted impact removes its round and creates one authoritative explosion.
Three quick visual bursts never create additional damage, scoring or impact events.
Salvo falloff measures the nearest point in the existing oriented chassis hull,
attached shield panels, detached wall or rock bounds, so small blasts can reach
large objects without reaching their distant origins. Vehicle/wall impulse still
points away from the blast toward the body center. Missile behavior is unchanged.

[Rear-shield coverage](items-shield.md) retains the existing blast-to-victim-center
intersection rule, evaluated before that hit can destroy armor. A covered vehicle
takes neither damage nor weapon impulse from that hit. An exposed front can take
damage while rear armor remains intact; when both exposed bodywork and armor are
inside the radius, each receives its own single damage application. Other vehicles
retain their own coverage tests. Detached walls receive one blast application each,
independent of vehicle count; explosions gain no new general wall occlusion.

Eligible attached [EMP noses](items-emp-claw.md#nose-shield-and-stored-ammunition-counterplay)
retain their membrane-distance and shield-interception rules. Cosmetic pops add
no secondary blast. Stored EMP ammunition retains its Missile-only blast rule.
[Destructible rocks](destructible-environment.md) consume the same committed impact
through the existing staged-destruction transaction, with at most one stage change
per piece per batch.

`SalvoExplosion` renders three token-seeded bursts 0.16 seconds apart, each with
48 colored sparks and a soft flash, inside twenty expanding dark smoke billboards.
Firework spread/flash size and explosion-smoke size are each ten times the initial
small firework presentation, independently of damage radius and aiming-circle size.
Smoke builds from just after the third burst to full density over 0.55 seconds,
so all three colorful accents precede the thick cloud. It fades gradually from
1.5 to 20 seconds and survives projectile removal. Flight trails are unchanged.
Spark updates stop after 1.15 seconds. The arena retains at most 128 explosion
owners (2,560 smoke billboards); saturation replaces the oldest cosmetic owner,
so extreme overload may shorten its smoke lifetime without changing gameplay.
Teardown frees all effects. Accepted reliable publications suppress duplicate
effects; an empty recovery checkpoint rebases the event revision without replaying
historical explosions. A restored round whose explosion is still alive keeps that
effect owner rather than duplicating or restarting its bursts. Peers use the same token seed, but appearance starts at local
receipt time. Late admission reconstructs flight, not historical explosion smoke.

F1 Configs provides a **Salvo · Local explosion VFX** accordion with independent
firework and smoke scales (1–20, default 10), burst spacing (0.08–0.3 s), smoke
lifetime (5–30 s), opacity (0.1–0.8) and firework brightness (0.5–3). Like existing
Missile cosmetics, these are session-only local previews captured at the next
impact; changing them never restarts existing smoke or changes authoritative
damage. Default settings yield identical token-seeded geometry across clients.

`check-salvo-explosion.ps1 -GodotPath <editor> [-Visual]` exercises native chassis
and shield contacts, simultaneous repeated volleys across three impaired UDP peers,
an imported-rock blast, duplicate/recovery presentation, three bursts, smoke fade,
bounded accumulation and teardown. Its explicit cosmetic stress fixture is not a
full-match GPU benchmark. It also accounts for each launch, native contact,
authoritative impact and effect owner across concurrent five-round volleys on
flat, sloped and uneven mesh terrain, plus production terrain surface boundaries.

## Firework flight and smoke

Airborne rounds use the exact crimson paper/ivory-band model extracted from the
catapult's original `Round_0` subtree. A white magnesium-like fuse spark and glare
remain lit while loaded and throughout flight. They are presentation only: waiting
in the spoon does not consume flight lifetime or cause an explosion. Independent
axial roll and end-over-end tumble leave the ballistic path unchanged. There is no
rocket motor, exhaust thrust, homing or midflight aim update.

Core starts a fixed 1,800-tick (30-second) lifetime at launch. The stored target
defines the initial constant-acceleration parabola, not an automatic detonation
point. Without contact, the same arc continues past that point and outside arena
bounds. The first eligible swept collision detonates immediately; otherwise the
final lifetime step detonates at its endpoint. Collision on the expiry step wins.
Salvo native terrain rays overlap segment boundaries by 0.01 m to cover exact concave-surface starts; contact is clamped to the current segment and closer native body hits retain priority. The existing atomic impact/removal transaction prevents duplicate damage/events.
The normal owner-life, reset, departure and Finished cleanup rules still apply.
Checkpoint and motion validation retain the original arc and launch age, including
late off-map positions and velocities; restoration never restarts the timer.

One arena-owned `SalvoSmokeTrail` MultiMesh holds at most 16,384 world-space textured
segments, sampled at 60 Hz per visible round with at most six catch-up steps per
render. Its budget accommodates the shared sixteen-projectile cap for fifteen
seconds of history. Saturation declines new cosmetic segments instead of deleting
existing smoke early. Each dark segment expands/drifts gently, fades from age two
through fifteen seconds, then expires. Smoke survives projectile removal and does
not inherit projectile spin. Late admission reconstructs current flight without
replaying historical smoke. Client extrapolation is limited to 100 ms and cannot
decide a hit or detonation.

`salvo_projectile_checks.tscn`, included in `check-salvo.ps1`, renders captive feeds,
translated/inverted seating, shared round/fuse, tumbling, five simultaneous arcs,
smoke removal/fade, thirty seconds of sixteen concurrent rounds and pool saturation.
Its performance sample is a focused presentation fixture, not a full-match or
remote-multiplayer benchmark.

Salvo reuses the shared centered camera cursor, assisted-car indicator and existing owner-only gold terrain circle. The circle follows current accepted targeting while the selected magazine or its flying rounds remain; it never steers old shots or leaves their targeting rings behind. Both ring edges sample static ground and surface normals; unsupported/discontinuous segments are omitted. Sky/outside-arena shots may have no supported footprint to draw. Remote inventories do not create guides. Death, Finished, inactive session and teardown clear the applicable guide.

Runtime keys `items.salvo_*` retain the existing Developer Options/Configs pipeline. `count` is fixed at 5; `interval_ticks` allows 30–180 ticks with default 75. Range (25–250 m), launch_height (1–10 m, default 3), arc_height (1–60 m), speed (20–200 m/s), blast_radius (0.1–30 m, default 0.9144), damage (0–1000 HP), falloff (0.25–4), impulse (0–50,000 N s), marker_scale (0.5–2 times a fixed 10 m reference; default 5 m guide radius), marker_width (0.1–2 m) and marker_lift (0.02–0.5 m) remain configurable. Each shot captures flight tuning and its successor deadline; live edits cannot refill ammunition, move an already scheduled deadline or bend an existing flight. Damage/marker properties use their independent current tuning. Isolate normal pickups with `spawns.salvo_weight = 1`, every other registered item weight zero, and `spawns.category_weapon_weight > 0`.

Resume/migration preserves the remaining magazine, next launch deadline and flying arcs without replaying historical events. Transient aim must be reacquired after restoration; disconnected retained players continue under the existing authority with forward fallback if necessary. Reset, death, permanent departure and Finished clear committed/flying rounds. A new life cannot resume a dead magazine even when the optional respawn policy retains other inventory. New matches begin empty.

`check-salvo.ps1 -GodotPath <exe> [-Visual] [-Impaired]` exercises three native UDP peers: repeated single-activation volleys, exact launch spacing, held/repeated input, per-launch targets, two blast targets, moving/banked terrain circles and owner privacy. It also runs the shared camera fixture for all four bearings, car assistance, independent arcs, out-of-arena firing, isolated weighted pickups and more than a minute of repeated pickup/use/cleanup with bounded scene-node counts. Reconnect/migration fixtures seed committed partial magazines and flying arcs for exact checkpoint comparisons. These local checks do not establish real EOS, independent-device or Internet coverage.
