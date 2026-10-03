# Tombstone presentation assets

`source/Tombstone.blend` is the editable Blender 5.2 master. It retains the linked,
unmodified production `TrackstormCar.blend` for fit reference. The approved design
is [Carnage Circus](reference/concepts-v4/carnage-circus.png): a full-height Dozer /
Ironclad shield, crimson and ivory stripes, painted eyes and grin, steel braces,
brass stars and caged amber lamps. Earlier sketches are design history. Generated
reference images are not runtime textures; the authored geometry and graphics are
original project work.

`source/BuildTombstone.py` reproduces the master and two GLB exports. Run Blender
with `--background --python-exit-code 1 --python <absolute-script-path>`. Rebuilding
overwrites the generated master/exports; preserve manual refinements first.
`Tombstone.glb` contains the shield only; `TombstoneRack.glb` contains its carriage.
The production Car is never re-exported. `sources.json` records source/export hashes
and the unchanged Car source hash.

Coordinates are metres, Blender +Y forward/+Z up, exported to Godot -Z forward/+Y
up. The source timeline demonstrates folded frame 1 and expanded frame 32. Named
rigid parts `Center`, `Wing_L` and `Wing_R` allow client-driven presentation. The
central panel is 3.7 m wide and 2.5 m high; each 1.45 m wing wraps forward over the
rear side when mounted and rotates 90 degrees outward to form the 6.6 m wall.
Trim and lamps extend slightly beyond those nominal panel dimensions.

The mounted center is vehicle local `(0, .25, 3.25)`, aft of the production rear
guards. Nominal road clearance is .145 m. The carriage uses the production rear
socket pair at rack local X +/- .45, Y .17, Z .30, retaining the existing ordered
trunk/rack articulation. It appears near full rack deployment; protection remains
immediate on selection. No authored mesh supplies collision: Core owns a rear
panel and two side-panel boxes, and the deployed wall retains its native box.

The shader reconstructs wear per identity from accepted HP. Impact flash/sparks
and short, non-colliding panel breakup are cosmetic. Shared meshes do not share
health or animation memory. Sparks reuse existing CC0 Kenney `spark_01`; no new
third-party runtime assets or dependencies were acquired.

`source/AuditTombstone.py` checks closed outward meshes, expanded bounds and 61
fold/release samples against the conservative production Car envelope. It checks
the authored neutral pose; runtime checks cover integration and articulation.
Use `check-tombstone-presentation.ps1 -GodotPath <exe> -Visual` and the applicable
item/vehicle checks. Actual results and limitations belong in
`docs/verification/ts-219.md`.
