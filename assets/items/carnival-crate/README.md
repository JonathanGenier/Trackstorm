# Carnival mystery pickup

Original Blender geometry and generated wood/metal textures follow the user-approved
intact three-crate concept supplied for TS-310. `source/ApprovedIntact.png` is an
authoring reference only, excluded from Godot import alongside the editable master.
No reference pixels are used in the runtime textures. No third-party asset was acquired.

Build with Blender 5.2.2 LTS:

```powershell
blender --background --python-exit-code 1 --python assets/items/carnival-crate/source/BuildCarnivalCrate.py
```

`source/CarnivalCrate.blend` retains separate planks, framed faces, raised faceted
stars, riveted folded corners, scalloped painted trim and side cross-braces.
The packed source includes original generated paint/grain/metal textures and
tangent-space wood relief. `CarnivalCrate.glb` batches rigid geometry into three
named crate groups with shared materials. Blender metres/Z-up convert to Godot
metres/Y-up. The ground-centred stack is approximately 3.09 × 2.85 × 1.77 metres;
the two lower crates support a centred upper crate. Godot generates native LODs.

`ItemSpawnPresentation` instances this asset once per existing marker, retaining
the complete stack as one visibility owner. Authored marker yaw follows the local
driving route, exposing the star faces to approaching cars. A one-time native
terrain fit seats both bottom crates and tilts the unchanged stack to the local
slope. There is no hover. The ITEM/RECHARGING billboard stays upright above the
tilted top crate. The authoritative marker and pickup sphere do not move with
this visual fit. The designated skill-jump stack stays aloft at the authored
marker height via `pickup_floating=true`; it retains its existing approach
requirements. This presentation metadata belongs to the map, not Core. Geometry
and materials are shared across markers. A shared, 256-triangle additive ribbon,
16 small ambient motes and a distance-faded local gold light provide the intact
pickup aura in Compatibility rendering; they are not collection/smash effects.

The asset has no physics bodies, collision shapes, hit areas, gameplay scripts or
independent crate authority. Only the existing confirmed item publication changes
availability. Collection hides all three crates together; the established
RECHARGING label remains. Confirmed new claim tokens start the arena-owned smash;
admission/recovery seed history without replaying old bursts.

`source/BuildCarnivalSmash.py` produces the editable `source/CarnivalSmash.blend`
and `CarnivalSmash.glb`: three long bevelled plank profiles with torn ends, folded
gold foil, a faceted five-point star, a thick rimmed/embossed coin and a bevelled
lightning bolt. The shader reuses the intact crate's original red/ivory/raw wood
textures, with painted-face vertex masks, ivory stripes and exposed torn edges. The approved
`source/ApprovedSmash.png` is an import-excluded reference only. The source/export
hashes and local reference identity are recorded in `smash-sources.json`.

Runtime creates 72 wooden fragments, 96 foil pieces, 12 stars, 12 coins, 12 bolts
and 140 spark trails in eight instance batches, plus a brief flash using the already acquired
Kenney `spark_01.png`. All geometry is cosmetic, seeded by one authoritative grant
token, with no physical debris or per-piece replication. Shared arena resources
and batch uploads bound construction cost; shader motion avoids per-piece frame
updates. Normal fade/expiry completes at 12 seconds, including while paused.
Pressure above 27 concurrent bursts retires older effects over 0.2 seconds, with
a 54-burst hard ceiling; full match teardown releases all active effects.

The short impulse keeps the default burst concentrated just beyond the intact
stack. Planks and solid gold tokens settle toward the fitted floor plane. Local
DevTools size preview (0.5–1.5×) scales all effect components around the stack centre,
captured per collection; it changes neither the intact asset nor gameplay.
Shared crate respawn timing remains the existing `spawns.cooldown_ticks` control.

Build the smash master with:

```powershell
blender --background --python-exit-code 1 --python assets/items/carnival-crate/source/BuildCarnivalSmash.py
```

`check-carnival-smash.ps1 -GodotPath <exe> -Visual` verifies whole-stack events,
repeat suppression, recovery, shader batches, pixel-measured orthographic bounds,
size preview response, rendered time samples/material close-ups in all five
environments, sustained publication pressure and paused cleanup. See the current
[pickup feature documentation](../../../docs/features/item-spawns.md) for native
driving, multiplayer, recovery contracts and their separate verification tools.

`check-carnival-pickup.ps1 -GodotPath <exe> -Visual` verifies import dimensions,
named crate groups, shared resources, absence of colliders, 120 availability
cycles, ground contact, route facing, label clearance, cleanup and rendered
production-map views at 6/16/32 m in all five
environment presets. Existing `check-pickup-drive.ps1` and `check-item-spawns.ps1`
exercise actual acquisition and native multiplayer lifecycle.
