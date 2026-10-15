# Wrench repair arm

`source/RepairArm.blend` is the editable Blender 5.2 mechanical master;
`RepairArm.glb` is its metre-scale Godot export. `source/BuildRepairArm.py`
rebuilds this original asset only. Run Blender with `--background
--python-exit-code 1 --python <absolute script path>` from the repository.
No Car master, rack geometry, or other item is rewritten.

The approved direction is TS-292 attachment 10154 (`Arm 7.png`) and attachment
10153 (`back red mount.png`). Their pixels are not included in the asset.
Geometry, UVs, seven packed patina maps, red/ivory stripes, raised yellow stars,
brass pivots, hose clamps, rubber conduits and impact-drill details are original
project authoring. No new third-party dependency or license is introduced.
The rear concept guides the low swivel mount only; the existing hydraulic rack
and the deferred under-rack dressing remain independently owned.

The rigid rig preserves `Mount`, `Shoulder`, `Elbow`, `Wrist`, and `Chuck` nodes.
`Shoulder -> Elbow -> Wrist -> Chuck` is an actual transform hierarchy. The
upper and lower links are 0.70 m and 1.25 m; joint-local Blender +Z becomes
Godot +Y. Client IK rotates the links without scaling them. The source keeps
individual editable details; export joins only meshes sharing a joint/material.
Modifiers are baked on the export copy after saving the editable source.

Four feet sit on the existing rack sockets at X +/-0.45 m, Z +/-0.30 m.
The arm attaches at the existing `WeaponRack` origin, folds transversely inside
the rear compartment, and unfolds only after the rack reaches full height.
See [Wrench presentation](../../../docs/features/items-wrench.md) for runtime
ownership and verification. `sources.json` records reproducible output hashes.
