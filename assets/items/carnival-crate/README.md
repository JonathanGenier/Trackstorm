# Carnival mystery pickup

Original Blender geometry and generated wood/metal textures follow the user-approved
intact three-crate concept supplied for TS-310. `source/ApprovedIntact.png` is an
authoring reference only, excluded from Godot import alongside the editable master.
No reference pixels are used in the runtime textures. No third-party asset was acquired.

Build with Blender 5.2.2 LTS:

```powershell
blender --background --python-exit-code 1 --python assets/items/carnival-crate/source/BuildCarnivalCrate.py
```

`source/CarnivalCrate.blend` retains separate planks, framed faces, raised faceted
stars, riveted folded corners, scalloped painted trim and side cross-braces.
The packed source includes original generated paint/grain/metal textures and
tangent-space wood relief. `CarnivalCrate.glb` batches rigid geometry into three
named crate groups with shared materials. Blender metres/Z-up convert to Godot
metres/Y-up. The ground-centred stack is approximately 3.09 × 2.85 × 1.77 metres;
the two lower crates support a centred upper crate. Godot generates native LODs.

`ItemSpawnPresentation` instances this asset once per existing marker, retaining
the complete stack as one visibility owner. Marker yaw plus a fixed presentation
angle keeps the silhouette stable, with a small 2.5 cm hover oscillation. Geometry
and materials are shared across markers. A shared, 256-triangle additive ribbon,
16 small ambient motes and a distance-faded local gold light provide the intact
pickup aura in Compatibility rendering; they are not collection/smash effects.

The asset has no physics bodies, collision shapes, hit areas, gameplay scripts or
independent crate authority. Only the existing confirmed item publication changes
availability. Collection hides all three crates together; the established
RECHARGING label remains. Destruction, confetti and wooden fragments are deferred
to the separate smash implementation.

`check-carnival-pickup.ps1 -GodotPath <exe> -Visual` verifies import dimensions,
named crate groups, shared resources, absence of colliders, 120 availability
cycles, cleanup and rendered production-map views at 6/16/32 m in all five
environment presets. Existing `check-pickup-drive.ps1` and `check-item-spawns.ps1`
exercise actual acquisition and native multiplayer lifecycle.
