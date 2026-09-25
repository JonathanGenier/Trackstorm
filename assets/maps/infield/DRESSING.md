# Production environment placement

The scriptless `scenes/maps/infield_dressing.tscn` is instanced by the active oval
as `EnvironmentDressing`. Rocks and infrastructure reuse the approved
[Blender library](../../environment/README.md). No new mesh, material, terrain,
gameplay rule, dynamic debris or environmental effect is authored here. Loose
scaled stones supply natural debris. The original conifer perimeter remains.

TS-74 attachment 10017 supplies composition and density direction, not geometry.
Ten named island zones frame the north connector, side loops, basin surroundings
and southern routes. Three larger rock landmarks distinguish the north, south
and east sectors. Exterior floodlight poles and short guardrail groups punctuate
the existing perimeter; lights are geometry only.

`BuildDressing.gd` is the offline placement source. It instances the current map,
removes old dressing from that temporary instance, and samples real terrain
collision. Its fixed seed, explicit landmark transforms, named zone extents and
per-zone scales make changes reproducible. Meshes/collision hulls remain owned by
the library blend; edit that library only under its own authoring contract.

```powershell
& $GodotPath --path . --script assets/maps/infield/BuildDressing.gd
./check-dressing.ps1 -GodotPath $GodotPath -Visual
./check-infield.ps1 -GodotPath $GodotPath -Visual
./check-oval.ps1 -GodotPath $GodotPath
```

Use a rendering backend to bake: Godot's dummy renderer discards MultiMesh
transforms. Rebuilding overwrites the dressing scene; preserve manual edits first
or express them in the bake source. `BuildOvalScene.gd` retains the dressing
instance. Neither operation rebuilds terrain or library assets.

## Placement and reuse

- Hard props protect route half-width plus four-metre shoulders and their visual
  bounding radius. Noncolliding cover protects half-width plus one metre and its
  radius. The full tabletop, side slopes, collars and tunnel approaches stay empty.
- Both 100 m jump corridors, all ten routes, basin centers and inner-straight
  transitions stay clear. Terrain rays reject low basins and steep slopes; rocks
  are slightly embedded. Existing terrain and gameplay geometry are unchanged.
- Hard props retain imported static convex collision and surface identity. Rock
  scales vary uniformly; infrastructure stays at unit scale outside usable asphalt.
- 147 rocks and 68 exterior fixtures reuse PackedScenes. 2,447 cover instances
  share four meshes in 212 spatial MultiMesh batches. Batch origins sit at their
  32 m cell centers so distance culling uses the local area. Counts are stored on
  the baked root. There are no per-instance scripts or ground-cover colliders.
  The saved scene caches each of the four cover meshes once; re-bake after
  changing library geometry or imported materials to refresh those cached meshes.
- Grass retains the library's 65 m cutoff/no shadows; scrub retains its 120 m
  cutoff. These hard cutoffs can change visibility by batch. Fades, material
  refinement, lighting effects and final optimization remain later work.

The native dressing check reloads the actual map, verifies reuse and saved batch
transforms, and probes vehicle-sized envelopes across lanes/recovery shoulders.
The infield suite drives production cars through all routes, jumps, tabletops,
basins and a two-car tunnel traversal. Its `Dressing` case also checks native
approaches into placed landmarks. Scenario resets are test setup, not proof of
free-driving recovery from every crevice. Actual results/limitations belong in
the Story verification report.

Runtime interaction is now owned by [destructible environment](../../../docs/features/destructible-environment.md). The authored scene remains the reset source. Arena-owned copies of batched plant transforms prevent one match from changing imported resources; structural fixtures and unreachable exterior trees are excluded.
