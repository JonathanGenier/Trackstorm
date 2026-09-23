# Infield layout graybox

This is the retained topology baseline. The active map now consumes the
[Blender terrain](TERRAIN.md), which preserves this layout and replaces its flat
visual/collision floor. Instructions below regenerate the baseline only.

`source/InfieldGraybox.blend` is the editable Blender source; the game instances
`infield_graybox.glb` beneath the scriptless production oval. One unit is one metre.
The original oval meshes, measurements, collision and vehicle scale are unchanged.

## Authoring and reference

Jira TS-74 attachment 10017, **Map Concept 0.2.0 Scale.png**, supplies layout
direction only. The reference was viewed in Jira. Its central tunnel, branching
side loops, lateral shortcuts, north/south axis and water islands inform this
layout; none of its dimension labels are used. All coordinates are selected in
the measured production infield for the 4.810 × 2.662 m vehicle.

Run Blender 5.2.2:

```powershell
& $BlenderPath --background --python-exit-code 1 --python assets/maps/infield/BuildInfield.py
& $GodotPath --headless --path . --editor --import
& $GodotPath --path . --script assets/maps/oval/BuildOvalScene.gd
./check-infield.ps1 -GodotPath $GodotPath -Visual
```

Regeneration overwrites the blend, GLB, route manifest and checksums. Preserve
manual Blender edits first. `source/.gdignore` prevents automatic blend import.
The GLB import retains precision and disables generated LODs. Godot's `-col`
suffix imports the five tunnel solids with static triangle collision. Route
ribbons deliberately reuse the continuous original infield floor: adding another
coplanar floor collider would produce competing support contacts. Editable route
ribbons remain hidden authoring guides in the blend. Blender rasterizes their
flat colors into `layout_palette.png`, applied to one boundary-matched visual
floor 5 cm above collision. This avoids overlapping-surface flicker. The palette
has no acquired texture or terrain detail; it is a topology diagram. The import
hook uses unlit placeholder shading and disables placeholder shadows. Neither
the palette nor the visible floor changes handling or represents final terrain.

## Reservations

Ten named routes provide two side loops, a north connector, two lateral shortcuts,
a north/south spine and four diagonal oval entries. Main lanes are 12 m wide,
shortcuts 14 m, and entries/spine 16 m, with 4 m berm/transition shoulders on each
side. Three open junction areas provide passing and route choice. There are no
maze walls. Two south-loop stretches reserve rhythm terrain. Four blue footprints
reserve water holes; three gray footprints reserve obstacle areas. These remain
driveable floor until their later implementation.

Both lateral jump corridors reserve 100 × 12 m: 30 m approach, 12 m kicker,
13 m flight footprint, 22 m dirt landing and 23 m recovery. They remain flat;
these allocations are design space, not validated future jump trajectories.
Final speeds, launch angles, elevation and landing profiles require terrain work.

The central open-sided tunnel envelope has 18 m between piers and a 5.5 m ceiling.
Its lateral opening preserves the current junction; the eventual earth bridge,
top route and vertical separation are not implemented. The top must not be
interpreted as an already reachable elevated route.

Twenty-metre-deep transition reservations run along both inner straights. Entry
lanes connect through these areas to the existing bank. No fence, new retaining
wall or step consumes that space. Later terrain work must preserve the oval's
authoritative inner edge and use these corridors to blend dirt/grass grades.

`layout.json` stores sampled route centerlines and dimensions for inspection and
native verification. Blender remains the production visual authority;
no runtime code generates the course. `sources.json` records original authorship
and output hashes. No external asset or texture is acquired.
