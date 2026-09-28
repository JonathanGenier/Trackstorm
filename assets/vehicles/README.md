# Production Trackstorm Car

`source/TrackstormCar.blend` is the editable Blender 5.2 master. `TrackstormCar.glb`
is its game export. `WastelandVehicle.tscn` retains the established resource path
used by startup, Lobby, practice, matches and Podium; it instances the new GLB.

## Authoring and import

Edit `source/TrackstormCar.blend` directly. This is the retained production master,
including the refined loose armor panels, continuous manifold tire carcasses, integrated staggered tread,
cage rails, exhaust shields and telescopic hydraulic sleeves. Do not run the
historical `BuildCar.py` / `FinalizeCar.py` on it: those rebuild the earlier design
and overwrite subsequent manual refinements.

Run Blender with `--background --python-exit-code 1 --python
assets/vehicles/source/ExportCar.py` to export a temporary material-batched copy.
The exporter does not overwrite the editable master. It preserves articulated
nodes, sockets, lamps, identification and named body panels. Export uses +Y up, applied modifiers
and no animation. Blender coordinates are +Y forward / +Z up; Godot uses -Z forward.

The committed GLB import configuration runs `ImportCar.gd`, flattening only the
identity file root and binding shared materials. Paint/steel use an original
3D weathering shader; rubber reuses the existing licensed Poly Haven maps.
All mesh UVs remain available for subsequent texture authoring. Static detail and
wheel surfaces are batched by material; body panels, lamps, hinges, links and sockets remain
separate. Godot generates mesh LODs. Blender source is excluded from Godot import.

## Body panel contract

`BodyPanel_Hood`, `BodyPanel_FrontFender_L/R`, `BodyPanel_Door_L/R` and
`BodyPanel_RearQuarter_L/R` are separate formed sheets in both the editable master
and game export. Their inward returns close the mesh edges; the gaps come from
separated geometry, not painted outlines. The hood has an 8 mm nominal side gap,
door leading/trailing gaps are 10 mm, and the split deck center gap is 10 mm.
Rounded shoulder surfaces connect each quarter to its wheel opening and fascia.
`BodyPanel_Rocker_L/R` sit below the doors; the lower cage stays inboard.

`TrunkLid_L/R` retain their names beneath `TrunkHinge_L/R` and remain the two
independent deck panels. Do not combine them with the static quarters or change
their pivots when editing fit. These names preserve editable geometry only;
there is no detachable-panel, damage or customization behavior.

## Reference resolution

All ten TS-259 attachments informed the design: both front three-quarter views,
side profile, front/rear elevations, elevated front/rear views, orthographic sheet,
and Car Closed/Open. The coherent design retains a deep-red armored coupe,
window grids, round headlights, four roof lamps, narrow riveted wheel-arch armor, tubular
guards, side exhausts and radial mechanical stencils. The closed/open pair defines
two outward-opening rear deck halves around an internal lift rack. Weapons are
intentionally absent; this asset provides only their mounting structure.

## Rig contract

- `WheelCarrier_FL/FR/RL/RR`: suspension translation and front steering.
- Their `WheelSpin_*` children: rotation about local X; nominal tread radius 0.54 m front / 0.565 m rear.
- `WheelLinkMount_<corner>_A/B/Upper/Upper2`: carrier-local inboard attachments,
  rotated with steering. Lower arms and two shocks terminate on the hub bracket,
  outside the rubber. Unit-length links scale along their rotated local axis.
- `SuspensionAnchor_*`: chassis attachments. `Upper`/`Upper2` and `ShockRod`/`ShockRod2` form two shocks at every wheel. `SuspensionLink_*` and `ShockRod_*`
  stretch/aim between attachments and hubs. Coil meshes inherit the shock sleeve.
- `TrunkHinge_L/R`: longitudinal hinges. `WeaponRack`: vertical lift with four
  stable `WeaponMount_L/R_Front/Rear` empties and socket plates.
- `LiftCylinder_*`, `LiftStage1_*`, `LiftStage2_*`, `LiftPiston_*`: visible hydraulic sleeves and telescoping rods.
- `Headlight_*`, `RoofAuxLight_*`, `TailRunning_*`, `Brake_*`, `Reverse_*`, and
  `RearIndicator_*`: independently addressable lens meshes/materials.
- `Identification`: existing per-player color and combat-flash surface.

Blender timeline frames 1–25 open the deck, 25–48 raise the rack, 48–60 hold,
60–75 retract and 75–100 close. Runtime `CarDeployment` reproduces the ordered
path with independent trunk/rack speed multipliers. Defaults of 3 give 0.24 s
trunk travel and 0.2933 s rack travel (0.5333 s total) in either direction.
Accepted live retuning preserves the current pose and permits smooth reversal. Its `Deployed` property
is a Client presentation seam for inventory presentation, not a new gameplay input,
weapon rule or network message. Network gameplay drives the mechanism through `CarRackPresentation` from confirmed inventory and use outcomes; see `docs/features/items.md`.

`WheelPresentation` reads accepted speed, steering and per-wheel compression.
Existing offline interpolation and network presentation roots remain unchanged.
The visual wheelbase is 3.351105 m. Physical support spacing remains 2.601105 m
to preserve accepted handling; the total longitudinal presentation offset is
0.75 m. The collision
hull length is 5.06 m, with a 5.85 m conservative spawn exclusion diameter.
Visual tire centers are ±1.38 m front / ±1.30 m rear while lateral support rays
remain at ±0.8165335 m. Narrow lateral terrain edges therefore remain an
approximation. Force laws and authority ownership are unchanged.

## Validation

`check-car-articulation.ps1 -GodotPath <exe> -Visual` drives the production adapter,
checks imported panel identity, articulation/deployment and saves captures and traces under `.godot/ts259-round6/car`.
Use the regular vehicle/network/oval checks for surrounding integration.
Current Story evidence and limitations belong in `docs/verification/ts-259.md`.
The previous Kenney-based master and source remain as historical editable assets.

The quarter panels include integrated shoulder surfaces and recessed wheel tubs.
Tire carcasses are 0.525 m wide before tread, versus 0.30 m in the first pass.
AuditCar.py samples rubber/chassis surface intersections across steering/travel;
its numerical sample is supplementary to native driving and visual review.

The raised rack origin is 1.34 m above the model origin (1.42 m of travel);
mounts remain 0.17 m above it. Nested sleeves make the longer hydraulic travel
read as a telescopic mechanism. Default speed multipliers and accepted session
values use the normal shared configuration catalog; no weapon rule or firing
latency depends on this presentation animation.

Paint uses a low-roughness red topcoat, specular/clearcoat response and sparse
oxide chips. Steel independently uses the same shader with higher roughness and
no clearcoat. Existing rubber texture provenance remains unchanged. Godot's
Compatibility renderer and production environment lighting determine the final
reflection appearance; the Blender material is an editable preview.

The extended door/cabin span carries the added length through the body. Lower
quarter shoulders, narrower elliptical openings, inset tubs, a door/quarter-light
pillar and fixed rear-deck shoulders retain editable individual panels. Arch armor
crowns/opening lips now reach approximately 0.42/0.40 m at their centre,
inside the unchanged 0.425 m shoulder. The larger cutout clears the outboard tires
through default steering and representative full travel without increasing the
outer fender width or shoulder height. Rubber width remains 0.526 m; front/rear
centres sit at +/-1.38 / +/-1.30 m. The rear rolling radius is 4.6% larger.
Central coachwork is 25% wider, with pivot origins and deck geometry refitted;
the door/cabin span carries the additional 0.25 m longitudinal extension.
Twin warm-metal springs, mounting seats and extended hub brackets articulate
with each carrier. Recessed rear bay walls expose the springs above the tires.
The longitudinal physical axle stations preserve that documented visual offset. The rack retains
its new authored longitudinal position (1.845 m aft) throughout deployment.
The closed deck sheet meets the 0.425 m fender shoulder. Each rubber tire is one
closed manifold mesh with 108 integrated broad tread blocks and rounded edges;
the 0.526 m width and slightly larger rear radius remain. Hidden rear tubs clear
full compression, and bonnet returns support the closed panel shut line.

Glass panes remain separate during material batching for transparency sorting.
`CarGlass.gdshader` uses low face-on opacity and stronger grazing reflections.
`CarLighting` creates per-instance lens materials and directional beams in Client;
accepted movement drives brake/reverse presentation for practice and remote cars.
No authoritative lighting state or elaborate interior is required.
