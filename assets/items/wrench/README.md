# Wrench repair arm

Original Blender geometry and deterministic UV patina follow TS-292's approved
side/rear concepts. Reference pixels are not redistributed. See `sources.json`.

Build with Blender 5.2.2:

```powershell
blender --background --python-exit-code 1 --python assets/items/wrench/source/BuildRepairArm.py
```

`source/RepairArm.blend` retains individually editable parts and named rigid pivots.
`RepairArm.glb` batches only within each joint/material. Shoulder -> Elbow -> Slide
-> Reach -> Wrist -> Chuck supports a 0.70 m upper link and a 1.25-3.25 m telescopic
forearm. Slide and Reach each translate 0-1 m; collapsed offsets are 0.05 m.
No mesh scaling is used for motion. Joint-local Blender +Z maps to Godot +Y.

The Client owns vehicle-local IK and independent hood contact requests. Battery,
reservoir and socket feet belong to fixed parents. `Services` contains the
underslung battery, tank, support shelf, hangers and supply lines to the turntable.
The battery casing is 0.896 x 0.42 x 0.406 m; the tank shell is 0.84 m long and
0.405 m in diameter. Following the existing Oil presentation policy, Services
is hidden while stowed and revealed only once the rack reaches Y=0.75 m.
These full-size components do not physically fit below the lowered rack.
Their transforms and hose endpoints remain fixed throughout visible deployment;
no second lift or runtime scale animation is used. Additional decorative
under-rack dressing is deferred.

Hydraulic hose radii and matching wraps/clamps are 12% thicker than the original
assembly. Routes, endpoints, fittings, battery, reservoir and arm dimensions are
unchanged. Rebuild both the editable master and runtime GLB from the same script.
