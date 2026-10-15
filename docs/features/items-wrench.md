# Wrench repair-arm presentation

Wrench remains a one-use, capability-bound repair. Core applies the configured
repair immediately (500 HP by default), clamps to maximum health, consumes at
full health, and cannot revive a destroyed vehicle. The arm introduces no
gameplay delay, new item state, packet, input, or collision shape.

The [original Blender rig](../../assets/items/wrench/README.md) replaces the
temporary Wrench box. `WrenchArm` is a child of the production `WeaponRack`,
under the same vehicle presentation/interpolation root as the chassis. All
targets and IK are vehicle-local, so driving, turning, camera changes and
network correction move the arm and roof together.

`CarRackPresentation` retains selection, capability, life and use ownership.
The existing `CarDeployment` opens the two trunk halves and raises the rack.
The full-size folded arm rides that mechanism; it never scales into existence.
Once raised, it unfolds over 0.75 seconds. A confirmed use waits for its own
installed arm to be ready and then plays a 2.7-second cosmetic sequence:
lift, left/middle/right roof contacts with a rotating impact socket, and return
to rest. Core repair has already occurred. An early use is still accepted by
the existing gameplay rules and its cue waits for physical clearance.

The 0.70/1.25 m rigid links share a hinge plane and retain their fixed lengths.
The fold path arcs around the shoulder's unreachable inner radius. Selection
changes cancel the current stroke through a smooth raised return, then nest
the arm before allowing the shared rack to descend. Pending selections retain
their authoritative capabilities. Death removes the arm; reseeding rebuilds
confirmed ownership without replaying old repair events.

Both owner and observers consume the existing accepted item publications and
use events. The cosmetic phase is local, as for the previous Wrench cue;
network delay can offset phases. A late join/reconnect reconstructs current
ownership, not an already-consumed historical repair animation. No additional
phase synchronization or recovery contract is implied.

`check-wrench-arm.ps1 -GodotPath <editor> [-Visual]` runs the production rack
through two native local UDP arenas. It checks imported folded geometry against
the trunk envelope, fixed-length articulation, roof contact positions, early
and repeated use, health/clamping, motion continuity, owner/observer agreement,
driving/collision, interruption, reseeding and life reset. Rendered evidence is
written to `.godot/ts292`. The full `check-car-rack.ps1` covers surrounding item
handoffs. These fixtures do not establish multi-PC Internet/EOS behavior.
