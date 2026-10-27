# Salvo catapult

`source/SalvoCatapult.blend` is the editable Blender 5.2 master. It contains named
rigid joints, original grain/patina images, and a 240-frame articulation study:
nested, extended, winched, loaded, 45-degree stop, reset, side/rear aim and stow.
`SalvoCatapult.glb` is the game export, batched by rigid parent without collapsing
articulation. `source/RefineCatapult.py` opens and refines the retained master,
preserving the spoon, magazine, winch, materials and named runtime interfaces.
It replaces the named platform/support/cable components and authors the coil morph.
`source/PolishWinch.py` then refines only the rope, its guides/attachment and drum,
retaining the approved spring, plate, turret and ammunition. `source/PolishCircus.py`
applies the original red enamel, bold white stripes and selected gold stars to the
spoon exterior, fixed turret/support housings and magazine flanges. Sliding metal,
spring, joints, brown rope and ammunition retain their contrasting materials; the
bowl interior retains wood. `source/StabilizeSupport.py` locks the lower support
to turret yaw, adds fixed red braces/gold locking bolts, and rebakes the spring
between the stationary base and moving spoon. For a complete regeneration, run
`source/BuildCatapult.py`, `source/RefineCatapult.py`, `source/PolishWinch.py`,
`source/PolishCircus.py`, then `source/StabilizeSupport.py`.
Each saves the master/export; preserve manual edits first.

The three original TS-210 images supplied by the user define the wooden spoon,
steel reinforcement, forward bearing, underslung rack winch and flat magazine.
Their pixels are not redistributed. No third-party geometry or textures were added.
See `sources.json` for provenance and hashes.

Run Blender with `--background --python-exit-code 1 --python` and the absolute path
to the selected authoring script. Import the export using the repository's
`import-godot.ps1`. All helper dimensions are metres in Godot coordinates; the
authoring helper converts to Blender +Z up / +Y forward. Source is excluded from
Godot import. Runtime poses use the exported rigid pivots, not skeletal skinning.
`python source/AuditCatapult.py` checks the exported rigid hierarchy, actual compact
vertex envelope, normals/UVs and the 60,000-triangle / 88-surface budget. Authored
livery and fixed braces retain a compact budget; the current export has 45,252
triangles, 81 surfaces and 10 materials. It is a read-only audit and does not replace runtime
articulation checks.

## Mechanical contract

- `FixedMount` attaches to the existing Car `WeaponRack`; no rack is copied.
- `PlatformLift` raises captured telescoping posts by 0.28 m. Its fixed guides
  remain overlapped by `TurretSlide`, which moves forward 1.07 m after rack lift.
  A crosshead and stationary pedestal support the slew bearing. `TurretYaw` rotates the hinge,
  support, spring, spoon and ammunition as one assembly, with full yaw travel.
- `SpoonHinge`, `ArmMiddle` and `ArmTip` retain overlapping telescopic members.
  The cocked spoon slopes down five degrees; release stops at 45 degrees with a
  short buffer recoil. `HingeCarriage` lifts the spoon pivot within overlapping
  cheeks. The straight rearward plate is fixed to turret yaw at `PlateHinge`
  (a retained joint name, no longer a pitch hinge). Its telescoping extension seats
  before cocking starts and retracts only during stow. Neither section pitches
  with the spoon. The spring foot remains fixed during cock/release/recoil/reset;
  the coil changes pitch and the rigid seats pivot about their captive anchors.
- `WinchHousing` lives inside/below the rack. The thick brown ship rope rises from
  the drum through its travelling guide, follows the lifting/sliding platform into
  an axial tension swivel, then passes outside the spring through `CableSpoonGuide`
  along the spoon underside to a visible spliced `CableEye` around the terminal pin
  on the turret side of the spring, at the beginning of `ArmTip`. The pin is 0.32 m
  toward the turret from `SpringTop`; the eye approaches it from another 0.07 m
  toward the turret. This is the left circle in the supplied `attachement.png`.
  No rope terminal remains at the bowl.
  Seven authored rope spans have a 66 mm diameter and normalized mesh coordinates.
  Runtime repeats the original three-ply texture by physical length, paying it
  along the spans while the spoon eye remains fixed. The coaxial tension coupling
  separates fixed and rotating sections without wrapping rope around the turret.
- `WinchDrum` carries ten bold brown windings, crimson flanges and asymmetric brass
  rotation markers. Its angle derives from total routed rope length and the 0.18 m
  winding radius; the 4 m reference sets its cosmetic rest phase. `WinchLevelWind`
  traverses the authored 52 mm winding pitch and keeps the line on the drum tangent.
  Yaw alone does not wind the drum. Deployment, release and rewind do. This is
  reconstructable presentation, not a rope-force or changing spool-volume simulation.
- `SpringTop` and `SpringFoot` are both downstream of the yaw bearing. The
  six-turn coil uses a Blender-authored pitch morph; wire gauge remains constant.
  Separate rigid seats and an internal guide connect both ends to actual saddles.
  The support belongs to `PlateHinge`, and the upper saddle belongs to `ArmTip`,
  independently of the magazine. The audit checks mesh/morph data as well as names.
- `Magazine` is directly on the outer spoon handle. Five crosswise horizontal
  pyrotechnics sit in one flat row. A captive escapement feeds the nearest remaining
  round to `BowlSocket`; brass retainers constrain the stored and loaded rounds.
  No round is a rigid body or world-gravity simulation.

`SalvoCatapult` presents the authoritative five-shot controller's accepted counts
and releases through `Bowl`, `IsReady`, `Cycling` and `ObserveRounds`. One loaded
round has a looping white fuse spark with no gameplay timer. Core owns launch
scheduling, collisions and the thirty-second flight lifetime.

`SalvoFirework` extracts the authored `Round_0` rigid subtree from this same export
for flight; magazine, bowl and airborne rounds therefore share geometry and
materials. The crimson paper cylinder, ivory bands and brass fuse end remain
original project work. `SalvoFlightVisual` adds independent tumbling and the same
bright fuse. `SalvoSmoke.gdshader` is original project code using the existing CC0
Kenney `smoke_01` texture in one bounded world-space segment pool; the fuse uses
the existing Kenney `spark_01`. No additional third-party content is acquired.

`SalvoExplosion` and `SalvoExplosionSmoke.gdshader` are original project code for
three token-seeded colored spark/flash bursts inside twenty fading smoke billboards.
They reuse those same CC0 `spark_01` and `smoke_01` textures. Explosion smoke lasts
twenty seconds, with arena-owned bounded cleanup; no new asset is acquired.

`check-salvo-catapult.ps1 -GodotPath <editor> -Visual` exercises production Car,
the shared rack, two native UDP peers, aim, captive feed, release/reset, switching,
airborne poses and reconstruction. It checks rope diameter/texture pitch, winding
phase, tangent attachment and guide travel, and captures a buffered 30-frame native
winch cycle and a second side-view support cycle with timestamps, plate/foot poses
and spring length. It also checks that the base seats before cocking begins.
Captures/logs go to `.godot/ts175`.
