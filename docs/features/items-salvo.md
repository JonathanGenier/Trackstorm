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
simulated. Accepted use gives a rapid
75 ms upward swing, a 45-degree stop with damped recoil, winch return and captive
reload; the presentation cycle lasts 0.48 seconds to fit the existing minimum
single-shot cadence. Stored rounds and the bowl's loaded round remain attached
to the mechanism during airborne, rolling and inverted vehicle poses.

Salvo uses the existing life/token/selection-validated camera direction and
replaceable accepted-aim publication for full yaw articulation. It does not enable
direct-fire sticky assistance or alter shared camera motion. Accepted Salvo token
rotation carries articulation to the successor token; stale-token input remains
invalid. No new packet or inventory authority is introduced. Both peers derive
mechanical state from accepted selection/count/use events. Recovery reconstructs
the remaining load and replays deployment without historical firing. Cosmetic
deployment phases remain local, as with the ordinary rack; exact phase-locking
across late admission is not part of this presentation contract.

`SalvoCatapult.Bowl`, `IsReady`, `Cycling` and `ObserveRounds` provide the mechanical
integration points for the automatic firing system. The current firing rules,
forward ground target, projectile arc and effects below remain unchanged. This
mechanical layer does not create an automatic volley or a fuse timer. Non-default
developer inventories above five rounds show at most five physical rounds; the
future five-shot controller owns the final fixed magazine contract.

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
Repeated-shot inputs wait for the authoritative cooldown as well as the mechanical
reset.

## Existing single-shot gameplay

Salvo is a Weapon in the existing registry (wire identity 6, alongside Proxy Mine identity 5). Each pickup holds five individually fired shots by default. Each separate UseItem press fires one missile and decrements only that physical slot; holding never repeats. A thirty-tick (0.5-second) minimum cooldown rejects early presses without spending ammunition or queuing a future shot. The HUD shows the remaining count, and the slot clears after its final shot. Each shot needs one place within the shared sixteen-projectile cap; insufficient capacity or missing target ground preserves ammunition. There is no manual target packet. At each launch the host projects the vehicle's current horizontal forward direction 65 metres onto static ground. Driving and steering change the aim for remaining rounds; each airborne missile finishes its own committed arc. Shots launch three metres above the current host-observed vehicle when their individual requests are accepted. A parabola twelve metres above the launch-to-target chord provides the arc, with mean travel speed 150 m/s. Complete 60 Hz segment sweeps can hit obstacles/vehicles before the intended ground point; an unobstructed final segment impacts the stored target.

Each impact uses a ten-metre blast radius, 250 maximum HP damage and 3,500 N s impulse. Damage and impulse fade with `(1 - distance / radius)^falloff`, default exponent one, reaching zero at the edge. The existing vehicle damage transaction produces distinct actual-damage events per target/round, with source `salvo`, owner attribution and projectile ID in the radial context. Ordinary kill attribution recognizes Salvo. Each applied rival hit contributes through the shared authoritative Circus item-damage path; this weapon has no independent score rule.

The local arena creates only its own circular marker. Selected Salvo and the owner's ongoing salvo display one guide at the current fixed-range forward aim point. It follows the car throughout the salvo rather than leaving rings at earlier launch targets. No marker identity, geometry or visibility is serialized. Both ring edges sample native static ground, follow height and surface normals, and offset slightly above the surface. Unsupported/discontinuous segments are omitted. The unshaded gold ring is designed for the normal chase camera. Remote inventories never create guides. Slot switching, death, Finished, loss of active session and teardown hide the applicable guide; recovery reconstructs it from local identity and current ownership/flight.

Runtime keys `items.salvo_*` cover count (1–16), interval_ticks (1–60), range (25–250 m), launch_height (1–10 m), arc_height (1–60 m), speed (20–200 m/s), blast_radius (1–30 m), damage (0–1000 HP), falloff (0.25–4), impulse (0–50,000 N s), marker_scale (0.5–2 times blast radius), marker_width (0.1–2 m) and marker_lift (0.02–0.5 m). Ordinary Configs, persistence, host validation and configuration version-forty-one carry all values. Count is captured on acquisition; changing it does not refill held ammunition. Each shot captures its cooldown, current forward aim/range, launch height, speed and arc. A rejected shot retains the item and its remaining count. Flying rounds retain their arc; impact and marker properties use current tuning. Straight Missile behavior is unchanged.

Item protocol version twenty-one carries remaining shots and absolute ready ticks in each physical slot alongside flying arcs and owner life. There are no scheduled automatic rounds. Each accepted shot retires its capability and assigns a fresh token to remaining ammunition, rejecting replayed use requests; this continuation does not emit another pickup sound. Allocation and consumption commit only after the world step succeeds. Resume/migration restore partial ammunition, cooldowns and flying rounds without replaying old events. Owner reset, death or permanent departure removes their rounds; disconnected retained players continue under the existing authority. Finished removes Salvo rounds, and new matches begin empty. Effects reuse existing licensed rocket/particle/audio assets; the HUD SVG is project-original.

`check-salvo.ps1 -GodotPath <exe> [-Visual] [-Impaired]` exercises three UDP peers in isolated native worlds: repeated salvos, five separately pressed launches/impacts per peer, held-input non-repeat and replicated ammunition, driving/steering with a moving guide and per-launch targets, two blast targets, second-slot preservation, chase-camera captures, banked ring geometry and owner-only visibility. Reconnect/migration fixtures seed partial ammunition, cooldowns and slow flying rounds to isolate exact checkpoint installation. These local fixtures do not establish Internet/EOS or independent-device coverage.
