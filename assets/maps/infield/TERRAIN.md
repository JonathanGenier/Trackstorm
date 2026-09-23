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
with smooth sides; surrounding terrain can influence absolute depth. No water
surface, water physics or distinct handling identifier is added. The three
obstacle reservations and the central tunnel envelope stay in their original
locations. Both lower tunnel crossings remain open at the original datum.
An elevated bridge route and additional structures are not added.

Both original 100 m jump corridors have a 30 m approach, a 12 m kicker reaching
4.8 m with a 0.70 terminal grade, a recoverable tabletop easing to 3.2 m beneath
the 13 m flight reservation, a 30 m descending dirt landing and a 15 m recovery.
The landing ends at corridor metre 85, before the unchanged tunnel junction.
Geometry is intended for
approximately 16 m/s approaches; the native harness checks nearby speeds as well
as lower-speed traversal of the overlapping shortcuts. Terrain shaping does not
change vehicle forces, health, suspension or steering. Native jump checks use
production 1000 HP and collision scale 5, report measured horizontal launch
speed, flight distance/time and vehicle-origin clearance above local terrain,
and require a grounded, damage-free recovery on the descending dirt zone.
Origin clearance includes ride height; it is not tire or underside clearance.
Controlled probes exercise both landing zones through both physics adapters.

Original vertex colors blend dirt lanes into grass shoulders over seven metres;
normal lighting and mesh normals communicate elevation. This is terrain-stage
surface readability, without acquired textures or later environment dressing.
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
