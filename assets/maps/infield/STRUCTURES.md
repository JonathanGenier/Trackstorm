# Infield structural geometry

`source/InfieldStructures.blend` owns the production junction structure. Its 62
named, individually editable pieces and bevel modifiers export to
`infield_structures.glb`. One Blender/Godot unit is one metre; mesh coordinates
are baked and the scene instance uses identity transforms.

The structure replaces the five inherited graybox tunnel solids. `ImportTerrain.gd`
removes those named mesh nodes and their child colliders during import. This keeps
the terrain Blender source, exported terrain triangles, jump profiles and topology
manifest unchanged. `ImportStructures.gd` supplies neutral rough shading only;
final material identity is separate work.

The four original pier centers remain at X/Z ±10 m. The 2 m piers leave 18 m
openings on both axes; the continuous soffit starts at 5.5 m. Foundations extend
below the flat junction. Capital blocks, perimeter beams and segmented cast deck
panels overlap the continuous slab, preventing collision cracks. Chamfers are
evaluated on export. Short tapered retaining returns and open drainage channels
stay behind the piers, outside the crossing corridors. Deck-edge parapets are
solid barriers, including for accidental airborne approaches.

Both existing ground-level routes stay open. The concept's elevated crossing is
not a reachable route in the approved terrain: adding approaches would change
the preserved jump recovery/junction topology. The structural deck therefore
does not claim to implement an elevated driving route. The two existing jump
ramps remain natural terrain; no constructed replacement ramps or new routes
are introduced. The three obstacle reservations remain reserved for later work.

Each exported `-col` mesh receives static triangle collision through Godot's
importer, on layer 1. Structures are obstacles, without `landing_terrain`
classification; the existing vehicle adapters retain normal crash damage. No
CSG, runtime construction script, handling identifier or gameplay rule is added.
The map remains a scriptless composition of imported assets. The scene baker
instances this same structural set.

```powershell
& $BlenderPath --background --python-exit-code 1 --python assets/maps/infield/BuildStructures.py
& $GodotPath --headless --path . --editor --import
./check-infield.ps1 -GodotPath $GodotPath -Visual -Case Structure
```

Regeneration overwrites the blend/GLB and manifest; preserve manual edits first.
The source directory is excluded from automatic Godot import. Retain the tracked
GLB import options (no position compression or automatic LODs).
`structures-sources.json` records original authorship and source/export hashes.
No third-party asset, texture or material was acquired.

The infield fixture verifies 102 crossing-envelope rays, imported obstacle
classification, ground joins, twelve input-driven passes across both axes and
four repeated native pier impacts. The complete fixture also exercises original
routes, jumps, connected travel and two-car traversal. Test setup resets at
scenario boundaries; traversal uses actual vehicle input/native physics.
Its existing terrain recovery checks can still expose the separately documented
uphill-start limitation. Historical results belong in the Story report.
