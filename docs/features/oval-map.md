# Reusable banked oval map

`scenes/maps/oval_foundation.tscn` is the active map for normal gameplay and practice.
It has no runtime script, camera, lighting, player controllers, match state or
network session ownership. The existing [combat arena](arena.md) is selectable as Old Map in multiplayer. Later map work can instance this scene and add content
without replacing its geometry or folding unrelated systems into the map root.

```text
OvalFoundation (Node3D, identity)
├── Geometry (instance of assets/maps/oval/oval_foundation.glb)
│   ├── Track
│   ├── Infield (retained hidden foundation)
│   └── GridMarkings
├── Collision
│   └── Track (StaticBody3D / Shape)
├── PlayerSpawns
│   └── player-01 … player-08 (Marker3D)
├── InfieldTerrain (Blender terrain collision)
├── InfieldStructures (Blender junction structure and obstacle collision)
├── ItemSpawns (five triple rows and five singles)
└── MapContent (concrete perimeter, upper collision, exterior ground and forest)
```

One Blender unit and one Godot world unit are one metre. Blender exports Y-up;
the map root, imported mesh nodes and collision bodies retain identity transforms.
The oval lies on Godot X/Z, its long axis is X, and racing from the grid starts
toward +X. The plane center is the map origin. All inner-edge vertices lie at
y=0; the bank rises outward and the infield has positive and negative grades.

## Geometry and collision ownership

Blender owns the production meshes. The unchanged project-supplied master,
editable production blend, source manifest, export/bake commands and measurement
interpretations live in [asset authoring notes](../../assets/maps/oval/README.md).
In particular, road width is measured **on the surface**, and the approximate
999.77 m lap is measured **in plan**. The geometry preserves the actual master
banking rather than approximating it from art.

The road's 10,992 triangles form one continuous static concave collision shape;
the active infield uses the separate Blender terrain's 402,124-triangle imported
static shape. The retained foundation floor is hidden and has no active collider.
No runtime collision generation or solidified road undersides are added. The offline content bake adds the outer barrier and containment described below. Grid paint and the separate verification car have no map collision.
Collision layer/mask 1 matches [vehicle queries](vehicles.md); unmarked static bodies retain the legacy Concrete handling profile. The independent [material identity system](surfaces.md) adds no simulation rules.

`PlayerSpawns` follows the existing arena's stable `Marker3D` naming convention.
Each marker has `slot_length_m=6` and `slot_width_m=3` metadata, and its -Z axis
points along the racing direction. These are reusable authored poses, not live
player identities. `ActiveMap` loads this scene and reads the actual marker
positions and yaw values in stable name order before gameplay authority is created. It adds 0.2 m to the original marker height for the rescaled vehicle, placing the origin at 1.05 m before suspension settles at 0.9 m.
Both `VehicleArena` practice and `NetworkVehicleArena` supply that validated
`ArenaConfiguration` to the existing simulation. Initial admission, slot reuse,
practice reset and death/respawn use the same eight grid transforms.
`VehicleNetworkDriver` retains the contract when restoring migrated authority;
resume checkpoints retain existing vehicle poses without resetting the map slots.

The oval contains 20 scene-authored item markers: five transverse rows with pickups at 3, 9 and 15 m across the 18 m surface, plus five singles. Locations follow the Jira placement reference relative to the grid, projected onto actual master sections; its legacy dimensions are not used. The existing network arena registers, observes, distributes and replicates these markers through the existing pickup system. Optional old-map prop snapshots remain absent. Local rigid-body practice retains its existing driving-only role; pickup acquisition and inventory remain owned by hosted gameplay. Old Map retains its own eight pickups.

`ActiveMap.ScenePath` identifies New Map and the practice default. Multiplayer selection belongs to `LobbySnapshot.Map`. Application/menu entry,
vehicle configuration, cameras, HUD, audio/music, networking and player lifecycle
retain their existing owners outside the scriptless map scene. The joined Lobby offers exactly Old Map and New Map through authoritative session state; the [Match Loader](match-entry.md) consumes that selection.

The [production vehicle](vehicles.md) is 4.81 m long at unit runtime scale. The
separate Blender reference vehicle remains a verification-only comparison. The [asphalt handling baseline](vehicles.md) owns driving behavior;
infield handling retains that same Concrete profile; material identity is independent.

## Infield terrain and preserved topology

The [Blender-authored terrain](../../assets/maps/infield/TERRAIN.md) preserves
connected dirt-course loops, a north connector, lateral shortcuts, an open
central tunnel junction and six oval access points. Main lanes are 12 m wide;
shortcuts are 14 m and entries/spine 16 m. Side-loop hills and berms, northern
rises, valleys, paired rhythm stretches and four negative water basins shape
the course. Two 100 m jump corridors integrate approaches, kickers, recoverable
tabletop approaches connected to the central deck. Each preserved kicker rises
4.8 m over 12 m. The intended 14–18 m/s approach range launches toward the
raised dirt tabletop; low-speed approaches can continue onto the bridge.
The three obstacle reservations and open-sided tunnel remain in place. The
[production structural set](../../assets/maps/infield/STRUCTURES.md) replaces
the graybox tunnel with chamfered concrete piers, foundations, capitals, beams,
a segmented deck over a continuous soffit, deck barriers, short retaining wings
and open drainage channels. Its north/south underpass retains an 18 m opening
and minimum 5.5 m soffit. The approved east/west crossing now passes across the
unchanged 6.35 m deck. Jump-facing parapets are removed; the other deck edges
remain guarded. Full-width dirt tabletops meet both ends, with mirrored rounded
side slopes and localized collars joining the existing terrain. Visible deck
panels share a continuous Blender-authored collision slab across their joints.

A twenty-eight-metre collar follows the original bank's inward grade at all 916 rim
sections and eases into the terrain. The original oval road and collision stay
unchanged. The old flat infield collider is replaced so it cannot fill the basins
or compete with wheel support. The shared material field blends dirt into grass shoulders and identifies wet soil, rock and the designated Water basin. Water behavior and environment dressing remain later work.

`check-infield.ps1 -GodotPath <path> -Visual` checks imported route support,
tunnel clearance and production-vehicle driving along all ten routes and both
jump corridors, plus a continuous loop-to-loop tour and two-car tunnel traversal.
`-Case Structure` isolates twelve repeated crossings, four native pier impacts,
51 underpass-envelope rays and structure/terrain joins. `-Case Tabletop` checks
slow kicker approaches, six offset deck crossings and four side climbs.
Each individual route is initialized separately, then driven entirely with
logical input and native physics. Jump runs measure real airborne travel, dirt
landing location and recovery at nearby approach speeds. This is repeatable
automated coverage, not proof of human control feel. Rendered views and measurements are
written to `.godot/infield-checks/`.

## Verification

`check-oval.ps1 -GodotPath <Godot .NET executable>` loads the committed map and
checks imported coordinates, transforms, road dimensions, collision equivalence,
22,900 raycasts over every road section and closure, adjacent normal continuity,
terrain rim continuity, all eight grid footprints and reference vehicle scale.
It also runs the existing `VehicleBody` and Core simulation through three high-speed laps using test-only steering input, plus low-speed bank descent/start, drift recovery, excessive-input spin and infield crossing. Practice and network collision adapters cross the bank-to-infield crease at multiple speeds and on a diagonal, with explicit wheel-support, rebound and settling bounds. An isolated test runway/crest also checks straight powered acceleration, coast-down, ordinary turns and 12 cm bump absorption. The fixture does not alter production tuning
or artificially move the driving body around the lap. It also starts ordinary
practice, checks all eight settled vehicles against the authored slots, resets
them back to the grid and verifies existing music playback.

`-Visual` additionally renders an overview, bank view and grid with the 4.81 m
reference. Evidence is written beneath `.godot/oval-checks/`. This establishes
repeatable high-speed drivability with one automated vehicle; it does not establish human control feel or multiplayer racing balance. Normal menu/lobby, separate-process networking,
death/respawn, reconnect and migration fixtures verify the active map contract,
20-marker pickup state and absent old-map prop state and preserved global systems. The separate-process driving
route turns into the infield so its replication checks do not depend on old walls.

[Feature index](README.md)

## Outer boundary and environment

`BuildOvalContent.gd`, called by the existing offline scene baker, follows every one of the 916 measured outer sections. The visible concrete strip is 1.3 m above the local rim and 1.2 m thick outward. 229 solid convex collision prisms span four master sections each, offset 5 cm outward to keep their chords clear of the usable road, and overlap by 5 cm at adjoining section ends, extend four metres outward, begin two metres below the rim and end at world y=45 m. This bounds ordinary driving and blast launches; arbitrary teleports and unbounded externally injected forces are outside that contract. Upper collision has no visible mesh. No inner containment exists and the infield remains accessible.

The exterior ground annulus starts at the outer rim and extends 700 m outward. It has no gameplay collision. 864 conifers occupy 48 spatial MultiMesh batches, using original Blender-authored mature fir, open-crowned pine and young spruce silhouettes. Deterministic irregular stands mix all three variants across depth, with varied size, aspect and orientation. Tree centers retain at least 12 m setback and 3.5 m mutual spacing; foliage stays outside the boundary. No per-tree runtime scripts or physics are added. Source and regeneration commands are in the oval asset notes.

Asphalt reuses the acquired Poly Haven Asphalt 04 maps, with a neutral tint and 2 m world-triplanar tiling. The foundation infield and exterior use original seamless 2 m grass albedo/normal maps with mipmaps and high roughness. The sculpted infield uses the shared dirt/grass/wet-soil/rock/water material field; the exterior remains textured grass. Surface identities do not alter legacy handling profiles.

The existing practice and network arena owners select `Daylight.tres` for the oval, retaining their single sun and WorldEnvironment. Old Map keeps its existing lighting. The oval resource supplies a blue procedural sky, cool ambient fill and filmic tonemapping without glare effects or competing map-owned lighting.

The oval runtime harness additionally checks all boundary seams at four heights, high-speed and airborne impacts through both production adapters, and 36 m/s driving approaches to every pickup through the existing Core pickup authority. `check-item-spawns.ps1 -Oval` runs the existing eight-peer UDP contention/cooldown/occupied-slot checks against the 20-marker map; its simultaneous distribution stage exercises the first eight markers, while the oval harness covers all 20 individual approaches.

The asphalt retains its separate seamless 64 m multiply layer. Grass combines two-metre blade detail with a restrained 16 m isotropic variation tile: its small, low-contrast variations avoid broad light/dark bands at driving height. Both UV sets use world triplanar mapping and mipmaps.

Driveable road and infield collision bodies carry the persistent landing_terrain group for [landing damage classification](vehicles.md#terrain-landing-recovery). The scene baker and terrain import hook retain this metadata. The reachable tunnel deck and edge beams also carry this group; remaining structural and outer-containment colliders remain obstacles. The existing contact-normal filter still treats side impacts as obstacles.
