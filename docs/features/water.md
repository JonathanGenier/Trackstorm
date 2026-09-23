# Water interaction

The designated Water field uses the existing [surface identity](surfaces.md) on
Blender-authored terrain. The terrain import registers `water_terrain` and a local
`water_level` of -0.45 m. A non-colliding plane at that level uses the same field
alpha threshold (0.5) as material identity. The existing terrain mesh and collision
are unchanged. Final wakes, splashes, shoreline treatment and water audio remain
visual-effects work.

## Observation and ownership

Client `WaterObservation` samples the center and four tire-footprint positions
through `SurfaceIdentityResolver`, bounded by the authored field bounds. It only
considers water terrain in the body's own World3D. Immersion is the greatest
positive distance from the nominal tire level (sample Y minus canonical ride
height) to the waterline. It is independent of suspension rays, grounding and
contact normals: inverted, falling and below-terrain vehicles remain exposed;
vehicles above the waterline and all Mud/Deep Mud fields remain dry. The field
footprint is intentionally conservative at mixed shoreline contact.

The import contract expects an upright, unit-scale terrain with a horizontal
waterline, as used by the production map. Multiple such fields are supported;
rotated water planes, waves, buoyancy and volumetric fluid simulation are not.
Water has no bottom cutoff, preventing under-terrain collision gaps from escaping
its hazard. Extremely fast externally teleported crossings between ticks are not
swept; ordinary driving is sampled at the shared 60 Hz boundary.

Both `VehicleBody` practice and `NetworkVehicleBody` host/prediction use this same
observation. `VehicleObservation.WaterDepth` is plain validated numeric input to
Core, not client-authorized damage. Host observations own damage; client prediction
uses immersion only for movement and retains confirmed HP/lifecycle.

## Gameplay and tuning

`SurfaceType.Water = 6` extends the existing stable handling identifiers. The
shared `VehicleMovement` applies Water grip/drag/drive tuning to supported immersed
vehicles. Water material support also selects that profile. Defaults are grip
0.45, drag 8, acceleration 0.65, more restrictive than Deep Mud's 0.5/5/0.8.
There is no propulsion without ground support and no buoyancy force. The gentler long-axis bank supports a standing shallow-water exit; the steep short-side bank can resist a standing climb with these deliberately restrictive defaults.

At immersion >= 1 m, `VehicleAuthority` applies 250 HP/s divided by the fixed tick
rate through `VehicleHealth.ApplyDamage`, source `water`, world instigator zero.
A stationary 1000 HP vehicle dies after approximately four seconds of deep
exposure. Damage starts immediately, stops on exit, and never heals on reentry.
Shallow water alone does not damage. The rule does not separately disable a living
engine; reaching zero HP invokes ordinary death, interaction suppression, item
cleanup and respawn. Existing HUD HP, damage feedback, Event Log and lifecycle
presentation communicate the outcome. No additional timer or lifecycle state is
introduced, so existing HP/life/deadline serialization retains all accumulated
consequences through resume/migration.

Developer Options > Configs > **Water** exposes `vehicle.water.grip`, `.drag`,
`.acceleration`, `.depth` and `.damage` through the existing transactional catalog.
Depth accepts 0.01–100 m, damage 0–10000 HP/s; modifiers retain the existing 0–100
bounds. Zero damage is an intentional developer override. Accepted host edits
apply at the next observation, preserve HP/life and persist/recover through the
same settings and configuration revision boundary. Configuration wire version 9
has 97 values; older layouts are rejected. Deliberate host overrides can change
default drivability ordering.

## Verification

`check-water.ps1 -GodotPath <exe> [-Visual]` samples actual basin collision poses,
checks shallow/deep coverage, exclusions and inverted/subterrain cases, runs three
death/respawn cycles through each production adapter repeatedly drives out along the shallow long-axis bank and drives from the bank
through shallow into deep water with logical input. Rendered captures go beneath
`.godot/water-checks`.

`check-death-respawn.ps1 -GodotPath <exe> -Water [-Impaired]` reuses eight real UDP
peers and verifies four remote Water deaths, reliable state/deadlines, item cleanup,
native inactivity, clean respawn and bounded presentation cleanup. This is local
native networking, not authenticated EOS or separate physical machines. Existing
terrain, vehicle, surface, configuration, recovery and networking checks remain
applicable. Core `WaterTests` covers boundaries, repeated exposure, authoritative
configuration, persistence, codec/checkpoint recovery and prediction ownership.

[Vehicles](vehicles.md) · [Developer Options](developer-options.md) · [Lifecycle](death-respawn.md) · [Networking](vehicle-networking.md)
