# Infield terrain authoring

`source/InfieldTerrain.blend` is the editable production terrain. Godot consumes
`infield_terrain.glb`, including its imported static triangle collision. The
retained `InfieldGraybox.blend`, `BuildInfield.py` and `layout.json` are the approved
topology baseline. Their route coordinates and reservations are unchanged.

Regenerate with Blender 5.2.2, then import with Godot 4.7.2 .NET:

```powershell
& $BlenderPath --background --python-exit-code 1 --python assets/maps/infield/BuildTerrain.py
& $GodotPath --headless --path . --editor --import
& $GodotPath --path . --script assets/maps/oval/BuildOvalScene.gd
./check-infield.ps1 -GodotPath $GodotPath -Visual
./check-oval.ps1 -GodotPath $GodotPath
```

Regeneration overwrites the terrain blend, GLB and terrain manifests. Preserve
manual sculpting first. One unit is one metre; there is no runtime scale or
procedural terrain generation. Position compression and automatic LODs are
disabled in the tracked GLB import configuration.

The mesh subdivides all 916 original oval inner-edge segments into 220 radial
rings (201,521 vertices, 402,124 triangles). Its outside vertices exactly match
the original boundary. A twenty-eight-metre collar follows the bank's inward grade
and eases into the interior, producing shallow swales along the banked curves.
The original foundation floor is hidden and its collider is removed from the
active map, allowing negative terrain without competing floor contacts. The
immutable oval asset and road collision remain retained unchanged.

Broad side-loop hills, low outer berms, northern rises and southern valleys
shape the main routes. Both southern rhythm stretches contain three 0.65 m
rollers. The four original water footprints contain 1.8 m sculpted depressions
with smooth sides; surrounding terrain can influence absolute depth. No water plane, water physics or distinct handling profile is added. The separate material field identifies wet soil and the northeast Water bed. The three
obstacle reservations and the central tunnel envelope stay in their original
locations. The north/south tunnel crossing remains at the original datum.
The approved TS-76 follow-up raises the east/west connection onto the existing
6.35 m deck. The separate [production structural set](STRUCTURES.md) owns the
tunnel; the import hook removes the inherited graybox solids.

Both original 100 m jump corridors have a 30 m approach, a 12 m kicker reaching
4.8 m with a 0.70 terminal grade. Those takeoff faces remain unchanged. Dirt
after each lip now blends into a full-width 6.35 m tabletop joining the tunnel.
The flat section extends from |X|=10.75 to 70 m across Z=±11.25 m. Rounded
side slopes share a mirrored profile to |Z|=23.25 m along the flat section;
the post-kicker transition and outer five-metre collar
blend into the preserved surrounding terrain. The outer toe narrows smoothly
through |X|=70..83 m to keep the adjacent basin approach clear. All vertices at |X|≥83 m or
|Z|≥28.25 m remain unchanged, as do the central underpass and terrain rim.
Geometry is intended for
approximately 16 m/s approaches; the native harness checks nearby speeds as well
as lower-speed traversal of the overlapping shortcuts. Terrain shaping does not
change vehicle forces, health, suspension or steering. Native jump checks use
production 1000 HP and collision scale 5, report measured horizontal launch
speed, flight distance/time and vehicle-origin clearance above local terrain,
and require a grounded, damage-free recovery on the raised dirt tabletop.
Origin clearance includes ride height; it is not tire or underside clearance.
Controlled probes exercise terrain landing classification through both physics adapters.

Run `BuildSurfaces.py` after geometry regeneration to author the material field, UVs and material slot on the production blend. It preserves vertex positions and triangle indices and exports the same geometry. Godot supplies runtime shading and uses the same field for identity; see [surface contracts](../../../docs/features/surfaces.md). The pass reuses existing licensed detail textures and adds no acquired assets.
`terrain-sources.json` records original authorship and output SHA-256 hashes;
`terrain.json` also binds the output to the unchanged topology manifest hash.

The runtime fixture checks primary lane support and slope, negative basin
collision, all ten original routes, jump launch/landing/recovery, the connected
loop tour and two cars sharing the tunnel. `-Case <prefix>` runs selected driving
scenarios with the common geometry checks; omit it for the complete suite.
The current vehicle slope-start limitation described in [vehicles](../../../docs/features/vehicles.md)
can block recovery from a stationary uphill pose; a supported mesh does not
guarantee a successful restart. The oval fixture retains its road,
grid, containment, pickup, handling and practice checks and verifies continuous
support around all 916 terrain rim sections. Runtime evidence and limitations
belong in the Story verification report, not this authoring contract.
