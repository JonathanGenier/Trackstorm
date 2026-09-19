# Static gameplay vehicle

Trackstorm's existing salvage combat car is preserved: the Kenney Car Kit 3.1 hatchback conversion, armor, guards, welds, windshield protection and exhaust stacks. No replacement or licensed production-car design is introduced. Existing CC0 sources and textures remain recorded in `sources.json`.

## Blender authoring

`source/LegacyVehicle.glb` preserves the complete previous Godot-authored design exported from the parent revision. `RescaleVehicle.py`, run with Blender 5.2 in background mode, imports that design, uniformly scales its measured 3.505 m overall length to 4.81 m, applies transforms to every mesh and saves `source/WastelandVehicle.blend` and `WastelandVehicle.glb`. The production blend is editable; source files are excluded from automatic Godot import by `.gdignore`.

One Blender/Godot unit is one metre. Scale is 1.3723252515; the origin receives a +0.108659 m vertical adjustment after scaling. Forward remains Godot -Z. Production root and mesh transforms have unit scale. Measurements are recorded in `measurements.json`:

- Overall length × width × height: 4.810 × 2.662311 × 1.856070 m.
- Wheelbase: 2.601105 m; tire-center track: 1.633067 m.
- Vertical tire radius: 0.485803 m (the original slight fore/aft tire ellipticity is preserved).
- Tire bottom: -0.9 m relative to the body origin. Default level-road equilibrium puts it at road height; lowest body clearance is approximately 0.243 m.

After exporting/importing the GLB, run `godot --headless --path . --script assets/vehicles/BuildVehicle.gd`. This script only binds the original Godot materials in a thin inherited scene. It never constructs or rescales production geometry. Existing Body, Tire and Armor triplanar resources remain in use; Trim preserves the original graphite color. The per-instance identification material remains controlled by `VehicleVisual`.

## Runtime definition

`Core.Vehicles.VehicleDimensions` records the shared spatial contract. Both native practice and network prediction use the same unscaled sprung box envelope, matching the armor/bumper footprint and including the roof. Wheels remain static visual geometry supported by four rays at the measured tire centers; there are no solid wheel colliders or articulation. The box intentionally approximates the detailed silhouette and does not reproduce individual armor pieces.

The settled origin is 0.9 m above level ground. Suspension extension adds the existing gravity/spring equilibrium compression (0.0654 m); spring/damper tuning is unchanged. Mass, propulsion, braking, speed caps, friction and steering tuning are unchanged. Wheelbase, load-height and collision/inertia geometry change only to match scale. Chase distance and height increase by the same scale factor. Spawn clearance uses a conservative 5.6 m circle to accommodate arbitrary headings; existing oval grid spacing is sufficient. ActiveMap adds 0.2 m to its original spawn marker heights, so the new tires initially clear the road.
