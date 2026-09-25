# Destructible environment

The production oval's 147 dressing rocks and 2,447 soft ground-cover instances
have match-owned destruction state. Structural terrain, bridge/tunnel, barriers,
drains, poles and the unreachable exterior conifer forest remain authored geometry.
The latter is non-interactive scenery outside containment, not reachable vegetation.
Old Map retains its existing three movable demonstration props.

## Authority and interaction

`EnvironmentAuthority` belongs to `HostVehicleSession`. Native adapters identify
the actual struck rock on their ordinary vehicle contact observations. Core uses
normal approach speed, a harmless 4 m/s threshold, 12 damage per excess m/s and
a 120 damage cap. A twelve-tick rock cooldown coalesces repeated manifold contacts.
The existing item transaction supplies committed Missile and Salvo impacts and
the existing explosion falloff; Proxy Mine remains a vehicle-contact weapon.
Rejected world/item steps do not advance destruction. Dead vehicles cannot crush
plants or originate environment impact damage.

Intact rocks are static and require 180 damage. A break swaps to one smaller
reusable BoulderLow representation. Stage 2 requires 60 damage; its next break
produces the smallest authored BoulderLow scale, measured from actual placements.
Existing loose stones at that scale begin in the final stage. A single accepted
batch advances at most one stage per rock; excess damage does not skip a stage.
Final rocks do not fracture again.

Broken rocks use controlled horizontal motion rather than free rigid bodies:
at most sixteen move per step, speed is capped at 6 m/s, damping settles them,
and displacement stays within eight metres of their authored origin. The native
view projects the resulting position onto map support. There are no fragment
allocations, rock-to-rock collision chains or independent Client damage rules.
Their shallow wheel-only collision envelope occupies layer 8. Chassis queries
exclude it; suspension rays include it. Both layer and mask are disabled on the
replaced intact collider. A separate simple layer-16 weapon target follows the
broken mesh and is included only in projectile queries. This intentionally
prioritizes stable drive-through
contact over a general rigid-debris simulation.

Soft plants have no rigid collision. Core tests the vehicle's oriented footprint
and height against authored plant positions. Accepted run-over or radial weapon
damage flattens the batched instance briefly and then clears it. Each arena owns
a copy of the MultiMesh transforms; shared imported meshes/materials remain shared.
The nine-tick flatten phase clears through at most one buffer upload per changed
batch per accepted boundary. Reseeding rebuilds the instance resource from retained
authored transforms so a fully cleared batch restores correctly.
Destroyed plants stay cleared for the match. Scene teardown/new match and the
explicit practice reset restore authored state.

## Replication and recovery

Stable identities follow authored scene-path and batch-instance order. Matching
builds provide the layout; peers cannot submit damage, stages or plant identities.
The bounded `TD` publication packs each rock's stage, accumulated damage, impact
cooldown, horizontal offset and velocity, plus one bit per plant. It is at most
7,959 bytes for the hard limit of 256 rocks and 4,096 plants. Production uses
4,592 bytes. Changed states publish at up to 20 Hz; an unchanged complete state
repeats once per second to recover dropped terminal updates. Current-host, match,
epoch/generation and increasing-tick checks fence live updates.

Version-three `TR` checkpoints embed the same complete state for initial entry,
fresh admission, resume and nested migration. Replacement authority restores
damage and movement continuation without replaying impacts. Native views reseed
cleared plants without historical flatten effects. Missing/mismatched layout
counts reject checkpoint installation. Normal map reset creates fresh authority.

## Verification

`check-destructible-environment.ps1 -GodotPath <exe>` uses the real renderer and exercises imported
rocks, soft cover and smallest-rock drive-over through both native vehicle adapters,
then three UDP worlds with delayed/lost traffic, late admission and sustained
cleared state. Core tests cover staged thresholds, duplicate ticks, cooldown,
weapon transaction reuse, compact codec rejection, reset and authority restoration.
The existing reconnect and migration harnesses seed exact damage and cleared
plants to isolate recovery from separately exercised impact behavior.

The explicit handling playtest accepts `--destructible-playtest`, writes evidence
under `.godot/ts-162/playtest`, and adds an optional `blast: [x,y,z]` fixture command.
It records stage/damage and cleared-plant counts alongside ordinary driving traces.
These controlled fixtures do not establish physical-controller ergonomics,
Internet/EOS behavior or multi-device performance.

See [vehicles](vehicles.md), [items](items.md), [oval map](oval-map.md),
[reconnection](reconnection.md) and [host migration](host-migration.md).
