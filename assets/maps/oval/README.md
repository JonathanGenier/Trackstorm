# Oval foundation authoring

`source/OvalFoundation.blend` is the editable production source. The unchanged
Higgsfield master is retained next to it. `.gdignore` excludes these authoring
files from Godot import/export; the game consumes the committed GLB files.

Use Blender 5.2.2 and Godot 4.7.2 .NET (the versions used for verification):

```powershell
& $BlenderPath --background --python-exit-code 1 --python assets/maps/oval/BuildOval.py
& $GodotPath --headless --path . --editor --import
& $GodotPath --path . --script assets/maps/oval/BuildOvalScene.gd
./check-oval.ps1 -GodotPath $GodotPath -Visual
```

`BuildOval.py` deliberately rebuilds from the immutable master. It overwrites
`source/OvalFoundation.blend`, both GLBs and `measurements.json`. Preserve later
manual Blender edits before rerunning it. For subsequent manual authoring, edit
the production blend and export only `Track`, `Infield`, and `GridMarkings` to
`oval_foundation.glb` with selection-only, modifiers applied, Y-up, metre units
and no animations. Export the reference car separately if it changes. Update
measurements and rerun the scene bake/checks after geometry changes.

The retained map import configuration disables position compression and automatic
LODs: either could change the source geometry or cause visual/collision mismatch.
Do not discard that tracked `.import` file. The scene baker uses the imported
surfaces to write two static concave collision resources and the reusable map
scene. It is offline tooling; no runtime script constructs map geometry or collision.

## Source treatment

Every original `TrackSurface_18m` vertex is preserved at its original metre
coordinate. The master has 916 ordered inner/outer sections. Its downward face
winding is reversed, and each section is split into six strips across the road
by linear interpolation of those actual vertices. This reduces diagonal faceting
on twisting quads without inventing a bank curve. The road has 10,992 triangles;
collision uses that same modest mesh rather than disconnected boxes or convex
hulls that would fill the oval. The master solidify modifier is excluded: the
driving surface is a continuous, upward-facing sheet.

The infield uses the exact flat inner boundary, with 916 planar triangles to its
center. Road and infield meet at y=0 with no overlapping floor or vertical step.
The original rectangular infield slab, barrier, lights, camera, label and arrows
are not production foundation content. Neutral materials distinguish road,
infield and grid; no texture dependency is added.

The original grid overlapped a banking transition. Its eight 6 × 3 m outlines
are reauthored in Blender on the flat straight: four rows at x=0,-8,-16,-24 m,
two lanes at Godot z=96,86 m, facing +X. Outline outer bounds are exactly 6 × 3 m;
paint is 0.1 m wide and 0.012 m above the road with no collision. Stable markers
use the existing `PlayerSpawns/player-01` through `player-08` convention. Their
origins are 0.85 m above the surface for the existing vehicle to settle. The
separate reference car is 4.81 m long and appears only in the validation fixture.

## Measurement interpretation

`measurements.json` records source section coordinates converted to Godot's
Y-up coordinates `(x, z, -y)`, the source checksum and measured dimensions.

| Measurement | Result |
| --- | --- |
| Surface width | 17.999988–18.000012 m |
| Straights between tangent centers | 214 m each |
| Plan centerline turn radius | 90.999993–91.000007 m |
| Plan centerline polygonal lap | 999.766840 m (analytic 999.769863 m) |
| Elevated surface-centerline lap | 1000.279695 m |
| Horizontal road footprint | 410.741608 × 200 m |
| Full bank | 35.000001° |
| Outside-edge rise over inner edge | 10.324376 m |

The 999.77 m reference is a **plan** lap: `2 × 214 + 2π × 91`. The longer
surface-centerline path includes the banking elevation changes. Similarly,
414 × 200 m describes an unbanked 18 m wide oval. At full bank the horizontal
width is `18 cos(35°)`, reducing its long-axis footprint. Expanding the supplied
banking to 414 m would violate either the 18 m surface width or 91 m radius.

All four transitions retain the master's sampled profile. The sampled intervals
bracketing flat-to-full-bank (or the reverse) span 149.6064 m in plan, consistent
with the 150 m reference at the approximately 1.0–1.2 m source section spacing.
No analytic replacement curve or concept-art reconstruction is used.

[Current map architecture](../../../docs/features/oval-map.md)

The scene baker also instances the separate [infield layout graybox](../infield/README.md).
Its authoring source, route reservations, tunnel and verification are independent
of this immutable oval foundation; rebuilding the foundation does not regenerate it.

## Combat environment authoring

Run Blender 5.2.2 with `--background --python-exit-code 1 --python assets/maps/oval/BuildEnvironment.py` to regenerate the original conifer variants, `source/OvalEnvironment.blend`, `conifers.glb`, and grass maps. This script does not modify the foundation blend, GLB or measurements. Grass maps are a deterministic original 1024-pixel seamless 2 m patch, combined with a restrained 16 m isotropic detail layer (the separate 64 m macro layer remains on asphalt), not an acquired photograph. Blender mesh geometry is exported Y-up in metres with applied coordinates and no animation. Godot consumes the GLB, never the blend.

Run the normal Godot editor import, then `BuildOvalScene.gd` as above. It invokes `BuildOvalContent.gd` to bake the fixed outer collision, concrete strip, exterior ground, MultiMesh forest batches and pickup markers into the scriptless production scene. The road geometry and road collision remain unchanged. Outer containment uses `BuildContainment.gd` and `ContainmentCollision.tres`: one continuous closed triangle surface at all 916 measured sections, without buried convex end caps. Run `Godot --headless --path . --script assets/maps/oval/BuildContainment.gd` to regenerate only that resource from measurements. The regular scene baker calls the same generator. Asphalt and concrete reuse the repository's existing licensed Poly Haven maps. `environment-sources.json` records original-art provenance and checksums. Preserve grass import settings for mipmaps/VRAM compression.

The spawn targets use only the red/green annotations in TS-100 attachment 10018, `Map Concept 0.2.0 weapon spawn position.png`, visually inspected in Jira. Bottom is Godot +Z and the grid faces +X. The red rows sit before the grid, on the lower-right turn, upper-right turn, upper-left turn and lower-left turn. Green singles sit on the lower straight, upper straight, upper-right outer line, upper-left outer line and lower-left outer line. Nearest source sections preserve placement while honoring TS-71 geometry; marker metadata records the source section and surface-lane distance.


The scene bake requires a rendering backend (omit `--headless`): Godot's dummy renderer discards MultiMesh transforms. The baker rejects that mode rather than saving an empty forest. Headless imports and runtime collision checks remain supported.

The three environment meshes provide mature fir, open-crowned pine and young spruce silhouettes. The scene baker mixes them in irregular stands with 3.5 m minimum center spacing, varied depth and height, preserving the 12 m boundary setback and fixed 864-instance/48-batch budget.
