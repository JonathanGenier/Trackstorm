# Destructible environment

The production oval's 147 dressing rocks and 2,447 soft ground-cover instances
have match-owned destruction state. Structural terrain, bridge/tunnel, barriers,
drains, poles and the unreachable exterior conifer forest remain authored geometry.
The latter is non-interactive scenery outside containment, not reachable vegetation.
Old Map retains its existing three movable demonstration props.

## Authority and interaction

`EnvironmentAuthority` belongs to `HostVehicleSession`. Native adapters identify
the actual struck rock on their ordinary vehicle contact observations. Core uses
horizontal normal approach speed, a harmless 3 m/s threshold, and damage
`min(360, 10 * (severity - 3)^1.5)`. Projecting sloping faces horizontally keeps
solid vehicle impacts meaningful while rejecting tangential brushes and roof
contacts. Low stones beneath the chassis use the same oriented footprint as
broken pieces; a real tagged contact takes precedence over that fallback.
A twelve-tick vehicle-impact cooldown coalesces repeated manifold contacts.
The existing item transaction supplies committed Missile and Salvo impacts and
the existing explosion falloff; Proxy Mine remains a vehicle-contact weapon.
Rejected world/item steps do not advance destruction. Dead vehicles cannot crush
plants or originate environment impact damage.

Intact rocks are static and require 180 damage. Each root reserves four stable
piece slots, initially one active and three dormant. A break replaces a piece
with two reusable BoulderLow pieces when a dormant slot remains; subsequent
breaks reduce the existing pieces within that fixed pool. Each stage scales
linear size by 0.72. The root's authored mesh-volume equivalent diameter and the
smallest authored BoulderLow diameter determine its terminal depth, so larger
rocks have more stages. Production's minimum is approximately 0.327 metres;
the largest profiles reach stage 11. No piece falls below that minimum. Rocks
within one size step of the minimum begin as one final movable piece.

Stage 2 requires 60 damage; later stages require `max(20, 60 * 0.72^(stage-2))`.
Each active piece receives damage independently. A single accepted batch advances
at most one stage per piece; excess damage does not skip a stage, and newly born
pieces cannot receive that batch's damage. New committed weapon impacts remain
effective during the vehicle-contact cooldown. Final rocks do not fracture again.

Broken rocks use controlled horizontal motion rather than free rigid bodies:
at most sixteen move per step, speed is capped at 6 m/s, damping settles them,
and displacement stays within eight metres of their authored origin. The native
view projects the resulting position onto map support, excluding practice cars
that share the terrain collision layer so pieces cannot ride on their roofs. There are at most 588
active visual pieces in production, with no free rigid-body debris, rock-to-rock
collision chains or independent Client damage rules.
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

Stable identities follow authored scene-path and batch-instance order, with four
consecutive piece identities per root. Matching
builds provide the layout; peers cannot submit damage, stages or plant identities.
The bounded version-two `TD` publication packs each piece's stage, accumulated damage, impact
cooldown, horizontal offset and velocity, plus one bit per plant. It is at most
30,231 bytes for the hard limit of 256 roots / 1,024 pieces and 4,096 plants.
Dormant slots occupy one byte; active slots occupy 29. Production uses 5,033
bytes initially and at most 17,381 bytes with every reserved slot active.
Changed states publish at up to 20 Hz; an unchanged complete state
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
plus a small/medium/large, low/medium/high speed and straight/angled impact matrix
(`-TuningOnly` selects that matrix),
then three UDP worlds with delayed/lost traffic, late admission and sustained
cleared state. Core tests cover staged thresholds, duplicate ticks, cooldown,
weapon transaction reuse, compact codec rejection, reset and authority restoration.
The existing reconnect and migration harnesses seed exact damage and cleared
plants to isolate recovery from separately exercised impact behavior.

The explicit handling playtest accepts `--destructible-playtest`, writes evidence
under `.godot/ts-162/playtest`, and adds an optional `blast: [x,y,z]` fixture command.
It records per-piece stage/damage/size/offset, authored size profiles and cleared-plant
counts alongside ordinary driving traces.
These controlled fixtures do not establish physical-controller ergonomics,
Internet/EOS behavior or multi-device performance.

See [vehicles](vehicles.md), [items](items.md), [oval map](oval-map.md),
[reconnection](reconnection.md) and [host migration](host-migration.md).
