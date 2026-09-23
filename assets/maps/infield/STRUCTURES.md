# Infield structural geometry

`source/InfieldStructures.blend` owns the production junction structure. Its 53
named, individually editable pieces export to
`infield_structures.glb`. One Blender/Godot unit is one metre; mesh coordinates
are baked and the scene instance uses identity transforms.

The structure replaces the five inherited graybox tunnel solids. `ImportTerrain.gd`
removes those named mesh nodes and their child colliders during import. This keeps
the inherited graybox out of runtime. The approved follow-up reshapes only the
local dirt approaches; kicker faces and the topology manifest remain unchanged.
`ImportStructures.gd` binds the existing Concrete material and Concrete collision identity.

The four original pier centers remain at X/Z ±10 m. The 2 m piers leave 18 m
structural openings on both axes; dirt now fills the east/west ground approach.
The continuous soffit starts at 5.5 m. Foundations extend
below the flat junction. Capital blocks, perimeter beams and segmented cast deck
panels overlap the continuous slab, preventing collision cracks. Chamfers are
evaluated on export. Short tapered retaining returns and open drainage channels
stay behind the piers, outside the crossing corridors. Deck-edge parapets are
solid barriers, including for accidental airborne approaches.

The north/south route retains its ground-level underpass. The approved east/west
crossing follows dirt tabletops across the existing 6.35 m deck. Ten jump-facing
parapet segments are removed; the north/south deck edges retain their barriers.
Kickers remain natural terrain in their original positions. Rounded dirt slopes
provide access on both sides of each end. The three obstacle reservations remain
reserved for later work.

Godot imports 44 static triangle collision bodies on layer 1. The nine visible
deck panels share one Blender-authored `DeckRoad-colonly` slab, bridging their
visual expansion joints. Its top remains 6.35 m. The deck and perimeter beams
carry `landing_terrain`; other structures remain obstacles. Existing contact
normal classification keeps wall impacts distinct from landings. No
CSG, runtime construction script, handling profile or gameplay rule is added.
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

The infield fixture verifies 51 underpass-envelope rays, imported collision
classification, tabletop heights, mirrored slopes, deck joins, twelve repeated
input-driven passes across the lower and upper crossings and
four repeated native pier impacts. The complete fixture also exercises original
routes, jumps, connected travel and two-car traversal. Test setup resets at
scenario boundaries; traversal uses actual vehicle input/native physics.
Its existing terrain recovery checks can still expose the separately documented
uphill-start limitation. Historical results belong in the Story report.
