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
`source/BuildTombstoneMount.py` rebuilds only the reinforced carriage in the existing
master and its GLB. The full authoring script invokes it as its final step; a
mount-only rebuild preserves the approved shield export byte for byte.

Coordinates are metres, Blender +Y forward/+Z up, exported to Godot -Z forward/+Y
up. The source timeline demonstrates folded frame 1 and expanded frame 32. Named
rigid parts `Center`, `Wing_L` and `Wing_R` allow client-driven presentation. The
central panel is 3.7 m wide and 2.5 m high; each 1.45 m wing wraps forward over the
rear side when mounted and rotates 90 degrees outward to form the 6.6 m wall.
Trim and lamps extend slightly beyond those nominal panel dimensions.

The mounted center is vehicle local `(0, .25, 3.25)`, aft of the production rear
guards. Nominal road clearance is .145 m. The carriage uses the production rear
socket pair at rack local X +/- .45, Y .17, Z .30, retaining the existing ordered
trunk/rack articulation. A bolted saddle supports twin boxed telescopic arms,
hydraulic rams and a cross-braced, four-jaw shield cradle. Named rigid groups allow
the carriage to follow the shield through selection and return.

After the rack rises, selection raises the compact horizontal shield aft of the
Car, tips it upright, unfolds its nested wings and draws it into rear-guard position
over 0.8 seconds. Deselection reverses that path before the rack lowers. The compact
inventory pose uses a stylized 0.28 scale, consistent with rack payload presentation;
it is not a mechanically exact packing of the full-size central plate. Nested wings
slide behind the center plate. Early use preserves the displayed pose, scale and
wing fold at world release; the empty carriage returns afterward. Protection and
use remain immediate under existing authority. No authored mesh supplies collision: Core owns a rear
panel and two side-panel boxes, and the deployed wall retains its native box.

The shader reconstructs wear per identity from accepted HP. Impact flash/sparks
and short, non-colliding panel breakup are cosmetic. Shared meshes do not share
health or animation memory. Sparks reuse existing CC0 Kenney `spark_01`; no new
third-party runtime assets or dependencies were acquired.

`source/AuditTombstone.py` checks closed outward shield/carriage meshes, expanded
bounds and 61 world-release samples against the conservative production Car envelope. It checks
the authored neutral pose; runtime checks cover integration and articulation.
Use `check-tombstone-presentation.ps1 -GodotPath <exe> -Visual` and the applicable
item/vehicle checks. Actual results and limitations belong in
`docs/verification/ts-219.md`.
