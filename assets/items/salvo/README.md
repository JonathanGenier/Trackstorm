# Salvo catapult

`source/SalvoCatapult.blend` is the editable Blender 5.2 master. It contains named
rigid joints, original grain/patina images, and a 240-frame articulation study:
nested, extended, winched, loaded, 45-degree stop, reset, side/rear aim and stow.
`SalvoCatapult.glb` is the game export, batched by rigid parent without collapsing
articulation. `source/BuildCatapult.py` reproducibly creates this original asset;
running it overwrites this master/export. Preserve manual edits before rebuilding.

The three original TS-210 images supplied by the user define the wooden spoon,
steel reinforcement, forward bearing, underslung rack winch and flat magazine.
Their pixels are not redistributed. No third-party geometry or textures were added.
See `sources.json` for provenance and hashes.

Run Blender with `--background --python-exit-code 1 --python` and the absolute path
to `source/BuildCatapult.py`. Import the export using the repository's
`import-godot.ps1`. All helper dimensions are metres in Godot coordinates; the
authoring helper converts to Blender +Z up / +Y forward. Source is excluded from
Godot import. Runtime poses use the exported rigid pivots, not skeletal skinning.
`python source/AuditCatapult.py` checks the exported rigid hierarchy, actual compact
vertex envelope, normals/UVs and the 60,000-triangle / 64-surface budget. It is a
read-only audit and does not replace runtime articulation checks.

## Mechanical contract

- `FixedMount` attaches to the existing Car `WeaponRack`; no rack is copied.
- `TurretSlide` moves forward 1.07 m after rack lift. `TurretYaw` rotates the hinge,
  support, spring, spoon and ammunition as one assembly, with full yaw travel.
- `SpoonHinge`, `ArmMiddle` and `ArmTip` retain overlapping telescopic members.
  The cocked spoon slopes down five degrees; release stops at 45 degrees with a
  short buffer recoil. A supporting telescopic beam stays under the compression spring.
- `WinchHousing` lives below the rack. `WinchDrum` turns independently. The fixed
  cable runs through the bearing-axis fairlead before connecting to `CableEye`
  beneath the spoon, avoiding wrap around the fixed winch at arbitrary yaw.
- `SpringTop` and `SpringFoot` are both downstream of the yaw bearing. The
  unit-length authored coil stretches between them in local space.
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
airborne poses and reconstruction. Captures/logs go to `.godot/ts175`.
