# Reusable banked oval map

`scenes/maps/oval_foundation.tscn` is the standalone foundation for the new map.
It has no runtime script, camera, lighting, player controllers, match state or
network session ownership. The existing [combat arena](arena.md) remains the
practice/network default. Later map work can instance this scene and add content
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
player identities. The foundation deliberately does not instantiate the combat
arena's eight-item configuration or register itself with multiplayer admission.

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
or artificially move the driving body around the lap.

`-Visual` additionally renders an overview, bank view and grid with the 4.81 m
reference. Evidence is written beneath `.godot/oval-checks/`. This establishes
foundation drivability with one automated vehicle, not racing balance, human
control feel, high-speed tuning or multiplayer behavior on the new map.

[Feature index](README.md)
