# Shield presentation assets

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
master and its GLB. The full authoring script invokes it before splitting the center; a
mount-only rebuild preserves the approved shield export byte for byte.
`source/FoldTombstoneCenter.py` splits the painted center, caps its cut surfaces,
adds the center hinge and exports the four-leaf shield without changing the carriage.
`source/FoldTombstoneRows.py` then splits each column at its horizontal midpoint,
caps the cut surfaces, and adds the eight top/bottom sections and their hinges.

Coordinates are metres, Blender +Y forward/+Z up, exported to Godot -Z forward/+Y
up. The source timeline demonstrates packed frame 1, horizontal-fold frame 20,
mounted frame 32 and expanded frame 60. Named rigid columns `Center_L`, `Center_R`,
`Wing_L` and `Wing_R` each contain `_Top` and `_Bottom` sections. Each wing is
parented to its corresponding center half. The center tops fold backward and wing
tops fold forward around visible horizontal pins, with a 16 mm seam through the
artwork. Opposite fold directions let the rows nest with the folded wings.
The center halves share a visible vertical axle, alternating knuckles and solid
hinge leaves, with an 18 mm seam through the original artwork. The
central panel is 3.7 m wide and 2.5 m high; each 1.45 m wing wraps forward over the
rear side when mounted and rotates 90 degrees outward to form the 6.6 m wall.
Trim and lamps extend slightly beyond those nominal panel dimensions.

The mounted center is vehicle local `(0, .25, 3.25)`, aft of the production rear
guards. Nominal road clearance is .145 m. The carriage uses the production rear
socket pair at rack local X +/- .45, Y .17, Z .30, retaining the existing ordered
trunk/rack articulation. A bolted saddle supports twin boxed telescopic arms,
hydraulic rams and a cross-braced, four-jaw shield cradle. Named rigid groups allow
the carriage to follow the shield through selection and return.

The compact folded stack appears when the deck clears, as the rack begins lifting,
and rides the rack out of the bay. After the rack rises, selection carries it aft of the
Car, pitches it upright, opens the two center halves and nested wings, unfolds all
four top sections, and draws it into rear-guard position over 1.15 seconds.
Deselection reverses that path before the rack lowers. All panel folding keeps full
constant size. The packed stack stays at 0.42 scale through the lift and initial aft travel,
with its hinge origin at rack local `(0, .25, -.20)` to center the folded geometry
over the deck while clearing the open lids. It
reaches full size behind the Car before the center hinge opens. This is
stylized inventory packing, not an exact mechanical simulation. Nested wings slide behind their
center halves. Early player use waits locally until the complete rack/unfold path
finishes, then requests one authoritative world release; the empty carriage returns
afterward. Switching away or discarding cancels the queued press. Protection remains
immediate under existing authority. No authored mesh supplies collision: Core owns a rear
panel and two side-panel boxes, and the deployed wall retains its native box.
While mounted, the armor sweep skims contacted driveable terrain; the chassis
retains its own ground collision. The low visual edge may enter a sharp slope
transition briefly. Vehicle/obstacle armor contacts and deployed-wall collision
remain active; see the [item contact contract](../../../docs/features/items.md#shield-rear-armor-and-persistent-health).

The shader reconstructs wear per identity from accepted HP. Impact flash/sparks
and short, non-colliding panel breakup (including the horizontal joints) are cosmetic. Shared meshes do not share
health or animation memory. Sparks reuse existing CC0 Kenney `spark_01`; no new
third-party runtime assets or dependencies were acquired.

`source/AuditTombstone.py` checks closed outward shield/carriage meshes, expanded
bounds and 61 world-release samples against the conservative production Car envelope. It checks
the authored frame-32 reference pose; runtime checks cover integration and articulation.
Use `check-tombstone-presentation.ps1 -GodotPath <exe> -Visual` and the applicable
item/vehicle checks. Actual results and limitations belong in
`docs/verification/ts-219.md`.
