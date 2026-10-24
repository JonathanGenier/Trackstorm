# Salvo catapult

`source/SalvoCatapult.blend` is the editable Blender 5.2 master. It contains named
rigid joints, original grain/patina images, and a 240-frame articulation study:
nested, extended, winched, loaded, 45-degree stop, reset, side/rear aim and stow.
`SalvoCatapult.glb` is the game export, batched by rigid parent without collapsing
articulation. `source/RefineCatapult.py` opens and refines the retained master,
preserving the spoon, magazine, winch, materials and named runtime interfaces.
It replaces the named platform/support/cable components and authors the coil morph.
`source/PolishWinch.py` then refines only the rope, its guides/attachment and drum,
retaining the approved spring, plate, turret and ammunition. For a complete
regeneration, run `source/BuildCatapult.py`, `source/RefineCatapult.py`, then
`source/PolishWinch.py`. Each saves the master/export; preserve manual edits first.

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
vertex envelope, normals/UVs and the 60,000-triangle / 72-surface budget. The rope
material and extra moving guide add surfaces while the simplified winding reduces
total geometry: the current export has 44,660 triangles and 71 surfaces. It is a
read-only audit and does not replace runtime articulation checks.

## Mechanical contract

- `FixedMount` attaches to the existing Car `WeaponRack`; no rack is copied.
- `PlatformLift` raises captured telescoping posts by 0.28 m. Its fixed guides
  remain overlapped by `TurretSlide`, which moves forward 1.07 m after rack lift.
  A crosshead and stationary pedestal support the slew bearing. `TurretYaw` rotates the hinge,
  support, spring, spoon and ammunition as one assembly, with full yaw travel.
- `SpoonHinge`, `ArmMiddle` and `ArmTip` retain overlapping telescopic members.
  The cocked spoon slopes down five degrees; release stops at 45 degrees with a
  short buffer recoil. `HingeCarriage` lifts the spoon pivot within overlapping
  cheeks. The straight rearward plate telescopes and pivots at `PlateHinge` as the
  spring unloads, keeping the spring within useful travel while remaining attached
  to the rotating clevis.
- `WinchHousing` lives inside/below the rack. The thick brown ship rope rises from
  the drum through its travelling guide, follows the lifting/sliding platform into
  an axial tension swivel, then passes outside the spring through `CableSpoonGuide`
  along the spoon underside to a visible spliced `CableEye` around the rear pin.
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

`SalvoCatapult` presents accepted counts and releases. Its `Bowl`, `IsReady`,
`Cycling` and `ObserveRounds` interface is available to the later firing-system
integration. It does not schedule a five-shot volley, ignite a fuse, spawn a
projectile or control impacts. Existing single-shot Salvo gameplay remains active.

`check-salvo-catapult.ps1 -GodotPath <editor> -Visual` exercises production Car,
the shared rack, two native UDP peers, aim, captive feed, release/reset, switching,
airborne poses and reconstruction. It checks rope diameter/texture pitch, winding
phase, tangent attachment and guide travel, and captures a buffered 30-frame native
winch cycle with actual timestamps/poses. Captures/logs go to `.godot/ts175`.
