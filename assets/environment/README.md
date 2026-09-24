# Modular environment library

`source/EnvironmentLibrary.blend` contains sixteen asset-marked collections. Each
collection has a ground-centred identity root and individually editable components.
Toggle collection visibility to edit one asset at the origin; only BoulderTall is
visible initially. Add this directory as a Blender Asset Library to browse/instance
the collections. The Godot deliverables are the individual `models/*.glb` scenes:
drag an asset into a scene repeatedly, retaining shared mesh/material resources.

One unit is one metre, Blender +Z becomes Godot +Y. Linear modules extend along
local X. Their origins are centered at ground level, not at a bounding-box center.
Apply placement rotation to the scene root; keep scale at 1 for infrastructure.
Rocks can overlap and penetrate terrain slightly to conceal irregular bases.

## Inventory and concept fit

The approved TS-74 attachment 10017 supplies rocky, scrub-covered dirt-course
islands, low vegetation and restrained perimeter infrastructure. Existing infield
routes, obstacle reservations and structural drains determine practical use; concept
dimension labels do not determine scale. `library.json` records actual bounds,
triangle counts, material names, collision counts, purpose and checksums.

| Assets | Role / module contract |
| --- | --- |
| BoulderTall, BoulderLow, RockSlab | Three distinct outcrop proportions, approximately 1.6–3.8 m wide |
| RockCluster | Three independently collidable stones; roughly 4 m cluster |
| RockLedge | Short fractured face for rocky slopes; overlap ends and embed back/base, not a terrain replacement |
| ScrubLow, ScrubTall | Sparse branched leafy scrub, approximately 0.9 / 1.35 m tall |
| GrassClump, DryGrassClump | Small green/dry opaque blade tufts |
| FirMature, PineOpen, SpruceYoung | The three existing oval conifer meshes extracted from the retained original blend; same forest material treatment |
| Guardrail4m | Four-metre repeat along X; posts at ±1 m give 2 m spacing across joints |
| Barrier3m | Three-metre cast concrete jersey barrier with lifting sockets |
| LightPole9m | Approximately 9 m twin downward flood fixture; geometry only |
| DrainChannel2m | Two-metre open U-channel along X; 0.7 m clear width, three independent collision solids |

Tunnel/deck/piers remain owned by the existing [structural source](../maps/infield/STRUCTURES.md).
No duplicate tunnel, grandstand, building, branded sign or invented debris category
is added. Loose rocks cover the visible natural rubble need. Production distribution,
rock composition and fixture placement are owned by the separate
[map dressing source](../maps/infield/DRESSING.md); this library does not place them.

## Materials, collision and distance

All visible components have packed smart-projected UV islands and semantic material
slots. Rocks use original generated 512px mineral albedo/normal maps; concrete uses
the existing licensed Concrete textures, with world-triplanar metre tiling. Organic and metal slots retain
Blender PBR colors/roughness and exported vertex variation. The import hook matches
the conifer darkening already used by the oval forest baker; leaves are two-sided
opaque geometry with no alpha sorting or texture dependencies.

Rocks combine asymmetrical fracture planes with small coherent erosion and mineral
variation. Scrub uses branching stems and folded, alternately oriented leaves;
grass uses seven irregular subtufts with curved, tapered blades. The same sixteen
assets remain reusable; this refinement does not add production placements.

`-convcolonly` meshes are explicit Blender sources, converted to simple static
convex shapes by Godot. Rocks use individual stone hulls; the ledge uses one hull
per stratum. The drain remains open because each lip and invert has its own hull.
Barriers and guardrails use simplified envelopes (rail gaps deliberately block
vehicles). Tree collision covers only the lower trunk, not foliage; shrubs and
grass have none. Pole collision covers its base and mast, not the elevated lamps.
Collision layer/mask 1 matches existing vehicles. No runtime-generated collision,
dynamic debris, per-instance scripts, destruction or gameplay rules are introduced.

Committed `.glb.import` settings enable Godot's screen-space mesh LOD generation.
The engine retains full source collision independently of rendered LOD. Small meshes
may retain their base topology when simplification cannot safely reduce them. Grass
stops rendering at 65 m and casts no shadow; scrub stops at 120 m. Compatibility
rendering uses a hard distance cutoff: blend placement into matching ground cover,
and avoid isolated skyline tufts. Trees and hard silhouettes have no distance cutoff.
For mass foliage placement use spatial MultiMesh batches as the existing forest does;
individual collidable instances are intended for sparse reachable props. Final
placement budgets and map-wide performance require the dressed map.

## Build and verification

```powershell
& $BlenderPath --background --python-exit-code 1 --python assets/environment/BuildLibrary.py
./assets/environment/ConfigureImports.ps1
& $GodotPath --headless --path . --editor --import
& $BlenderPath --background --python-exit-code 1 --python assets/environment/AuditLibrary.py
./check-environment.ps1 -GodotPath $GodotPath -Visual
```

The builder recreates the blend, exports and manifest. **Preserve manual edits
before rebuilding.** For manual export, work from a copy of the edited collection:
join copies of the visible components into one mesh, retain the named collision
source objects, select the identity parent plus visual/collision children and export
selection-only GLB, Y-up, modifiers applied, active vertex colors, no animations.
Keep the original editable components in the blend. Update the manifest measurements
and hashes and repeat source/native checks after manual edits. Do not export catalogue
offsets or change collision suffixes. The `source/.gdignore` excludes Blender files
from automatic Godot import/export.

The native fixture checks bounds/orientation, identity transforms, UV/color/material
channels, shared resources, collision types/counts, open drainage and repeated rigid
body impacts at two rotations. It renders the gallery and a temporary actual-map
context without modifying the production map. These impacts use a 900 kg sphere
proxy; they do not establish production-car handling or dressed-map balance.

## Human inspection

Run the visual command above from the repository root with the Godot console
executable. It automatically cycles through fixed views, writes PNGs under
`.godot/environment-checks/`, reports its checks and exits; it is not an interactive
driving scene. Open `gallery.png`, `rock-detail.png`, `ledge-detail.png`,
`scrub-detail.png`, `grass-detail.png` and `map-context.png` to inspect the result.

For interactive inspection, open this project in the Godot editor, create a temporary
3D scene with a Node3D root, and drag any `assets/environment/models/*.glb` from the
FileSystem dock into it. Frame the selected instance in the 3D viewport and use the
editor preview sun/environment if needed. Duplicate the scene instance, rotate the
copy, and inspect it from multiple angles. Keep this temporary inspection scene
separate from the production map. This editor workflow is guidance, not a claim
that manual GUI testing was performed.

All geometry is original project art, with the conifer derivation and original
source checksum recorded in `library.json`. Concrete maps are reused under their
existing [provenance/license record](../../THIRD_PARTY.md#prototype-arena-assets).
No new third-party assets or reference-image pixels are distributed.
