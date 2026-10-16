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
reservoir and socket feet belong to Mount. Existing rack lifting remains the only
lift. No under-rack decorative assembly is included.
