# Reusable banked oval map

`scenes/maps/oval_foundation.tscn` is the active map for normal gameplay and practice.
It has no runtime script, camera, lighting, player controllers, match state or
network session ownership. The existing [combat arena](arena.md) remains available
only as an explicit verification fixture. Later map work can instance this scene and add content
without replacing its geometry or folding unrelated systems into the map root.

```text
OvalFoundation (Node3D, identity)
├── Geometry (instance of assets/maps/oval/oval_foundation.glb)
│   ├── Track
│   ├── Infield
│   └── GridMarkings
├── Collision
│   ├── Track (StaticBody3D / Shape)
│   └── Infield (StaticBody3D / Shape)
├── PlayerSpawns
│   └── player-01 … player-08 (Marker3D)
└── MapContent (extension point)
```

One Blender unit and one Godot world unit are one metre. Blender exports Y-up;
the map root, imported mesh nodes and collision bodies retain identity transforms.
The oval lies on Godot X/Z, its long axis is X, and racing from the grid starts
toward +X. The plane center is the map origin. All inner-edge vertices and the
flat infield lie at y=0; the bank rises outward.

## Geometry and collision ownership

Blender owns the production meshes. The unchanged project-supplied master,
editable production blend, source manifest, export/bake commands and measurement
interpretations live in [asset authoring notes](../../assets/maps/oval/README.md).
In particular, road width is measured **on the surface**, and the approximate
999.77 m lap is measured **in plan**. The geometry preserves the actual master
banking rather than approximating it from art.

The road's 10,992 triangles form one continuous static concave collision shape;
the 916-triangle flat infield uses another. No runtime collision generation,
per-section bodies, solidified undersides, barriers or hidden containment walls
are added. Grid paint and the separate verification car have no map collision.
Collision layer/mask 1 matches [vehicle queries](vehicles.md); unmarked static
bodies resolve to the existing Concrete handling identifier. No new surface or
Core simulation rule is introduced.

`PlayerSpawns` follows the existing arena's stable `Marker3D` naming convention.
Each marker has `slot_length_m=6` and `slot_width_m=3` metadata, and its -Z axis
points along the racing direction. These are reusable authored poses, not live
player identities. `ActiveMap` loads this scene and reads the actual marker
positions and yaw values in stable name order before gameplay authority is created.
Both `VehicleArena` practice and `NetworkVehicleArena` supply that validated
`ArenaConfiguration` to the existing simulation. Initial admission, slot reuse,
practice reset and death/respawn use the same eight grid transforms.
`VehicleNetworkDriver` retains the contract when restoring migrated authority;
resume checkpoints retain existing vehicle poses without resetting the map slots.

The oval contains no old arena props, barrels, obstacles, decorations or item
markers. Its item configuration is empty; the existing item authority, inventory,
combat, replication and presentation remain available, with no placed pickups.
Pickup placement belongs to later map work. Optional prop snapshots remain absent.
The old map scene and its assets are retained, not instantiated by normal entry.

Map selection is centralized in `ActiveMap.ScenePath`. Application/menu entry,
vehicle configuration, cameras, HUD, audio/music, networking and player lifecycle
retain their existing owners outside the scriptless map scene. There is no map
selection UI or new map negotiation protocol; session peers use the same build.

The existing vehicle remains at its current gameplay scale. A separate 4.81 m
Blender reference vehicle is exported for in-engine scale checks, not substituted
for the [production vehicle](vehicles.md). Vehicle rescaling and handling tuning,
infield gameplay, terrain, barriers and environment dressing remain later work.
Open edges are intentional: this is a foundation, not a contained arena.

## Verification

`check-oval.ps1 -GodotPath <Godot .NET executable>` loads the committed map and
checks imported coordinates, transforms, road dimensions, collision equivalence,
22,900 raycasts over every road section and closure, adjacent normal continuity,
flat infield/rim coverage, all eight grid footprints and reference vehicle scale.
It also runs the existing `VehicleBody` and Core simulation through a complete
lap using test-only steering input. The fixture does not alter production tuning
or artificially move the driving body around the lap. It also starts ordinary
practice, checks all eight settled vehicles against the authored slots, resets
them back to the grid and verifies existing music playback.

`-Visual` additionally renders an overview, bank view and grid with the 4.81 m
reference. Evidence is written beneath `.godot/oval-checks/`. This establishes
foundation drivability with one automated vehicle, not racing balance, human
control feel or high-speed tuning. Normal menu/lobby, separate-process networking,
death/respawn, reconnect and migration fixtures verify the active map contract,
empty pickup/prop state and preserved global systems. The separate-process driving
route turns into the infield so its replication checks do not depend on old walls.

[Feature index](README.md)
