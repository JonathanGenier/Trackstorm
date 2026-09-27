# Production Trackstorm Car

`source/TrackstormCar.blend` is the editable Blender 5.2 master. `TrackstormCar.glb`
is its game export. `WastelandVehicle.tscn` retains the established resource path
used by startup, Lobby, practice, matches and Podium; it instances the new GLB.

## Authoring and import

Run Blender in background mode with `--python assets/vehicles/source/BuildCar.py`,
then `--python assets/vehicles/source/FinalizeCar.py`. The second pass poses the
suspension, adds coil springs, keys hydraulic extension and batches stencils.
Both scripts use Blender mesh authoring and retain the master; no runtime mesh
construction substitutes for it. Reopening/editing the master directly is supported;
regeneration deliberately rebuilds the design, so preserve manual refinements first.
Export selected production hierarchy as GLB, +Y up, applied modifiers, no animation.
The Blender authoring coordinates are +Y forward / +Z up; Godot uses -Z forward.

The committed GLB import configuration runs `ImportCar.gd`, flattening only the
identity file root and binding shared materials. Paint/steel use an original
3D weathering shader; rubber reuses the existing licensed Poly Haven maps.
All mesh UVs remain available for subsequent texture authoring. Static detail and
wheel surfaces are batched by material; lamps, hinges, links and sockets remain
separate. Godot generates mesh LODs. Blender source is excluded from Godot import.

## Reference resolution

All ten TS-164 attachments informed the design: both front three-quarter views,
side profile, front/rear elevations, elevated front/rear views, orthographic sheet,
and Car Closed/Open. The coherent design retains a rust-red armored coupe,
window grids, round headlights, four roof lamps, riveted extended fenders, tubular
guards, side exhausts and radial mechanical stencils. The closed/open pair defines
two outward-opening rear deck halves around an internal lift rack. Weapons are
intentionally absent; this asset provides only their mounting structure.

## Rig contract

- `WheelCarrier_FL/FR/RL/RR`: suspension translation and front steering.
- Their `WheelSpin_*` children: rotation about local X; nominal tread radius 0.582 m.
- `SuspensionAnchor_*`: chassis attachments. `SuspensionLink_*` and `ShockRod_*`
  stretch/aim between attachments and hubs. Coil meshes inherit the shock sleeve.
- `TrunkHinge_L/R`: longitudinal hinges. `WeaponRack`: vertical lift with four
  stable `WeaponMount_L/R_Front/Rear` empties and socket plates.
- `LiftCylinder_*`, `LiftPiston_*`: visible hydraulic sleeves and telescoping rods.
- `Headlight_*`, `RoofAuxLight_*`, `TailRunning_*`, `Brake_*`, `Reverse_*`, and
  `RearIndicator_*`: independently addressable lens meshes/materials.
- `Identification`: existing per-player color and combat-flash surface.

Blender timeline frames 1–25 open the deck, 25–48 raise the rack, 48–60 hold,
60–75 retract and 75–100 close. Runtime `CarDeployment` reproduces the ordered
path in 1.6 seconds each way and permits smooth reversal. Its `Deployed` property
is a Client presentation seam for later integration, not a new gameplay input,
weapon rule or network message. Normal gameplay keeps the rack stowed.

`WheelPresentation` reads accepted speed, steering and per-wheel compression.
Existing offline interpolation and network presentation roots remain unchanged.
No Core physics, force, tuning, collision, authority, serialization or input rule
is changed. The physical 2.601105 m wheelbase stays aligned. The visual tire
centres are widened to ±1.03 m while existing support rays remain at ±0.8165335 m;
this deliberate presentation offset avoids a handling redesign. Narrow terrain
edges are consequently approximated by the unchanged physical supports.

## Validation

`check-car-articulation.ps1 -GodotPath <exe> -Visual` drives the production adapter,
checks articulation/deployment and saves captures and traces under `.godot/ts164-car`.
Use the regular vehicle/network/oval checks for surrounding integration.
Current Story evidence and limitations belong in `docs/verification/ts-164.md`.
The previous Kenney-based master and source remain as historical editable assets.
