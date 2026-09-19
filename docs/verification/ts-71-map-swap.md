# TS-71 active oval integration verification

Verified 2026-09-19 on `ts-71-pg`, continuing the existing foundation commit
`fd9468b`. Current Jira TS-71, the retained Higgsfield master, repository routing,
feature documentation, foundation implementation and application/session loading
were inspected before implementation. The user's approved pickup clarification
was added to Jira: keep the item system, with no placed pickups until later map work.

## Integrated behavior

- Active scene: `res://scenes/maps/oval_foundation.tscn`.
- `ActiveMap.ScenePath` is the single selection used by ordinary `VehicleArena`
  practice and `NetworkVehicleArena`. Existing main/menu and `DevelopmentSession`
  entry paths remain intact.
- The loader reads `PlayerSpawns/player-01` through `player-08` from the existing
  scene in stable name order. Their positions and yaw feed a validated plain Core
  `ArenaConfiguration` before host authority allocates any vehicles. Coordinates
  are not duplicated into a second hardcoded oval spawn table.
- Initial admission, replacement slots, fresh active admission, practice reset and
  timed respawns use this configuration. Resume preserves existing vehicle state;
  migration checkpoint construction, decoding and restored authority all receive
  the same locally loaded map contract.
- Normal gameplay never selects `PrototypeArena.Configuration` for its spawns.
  Old positions remain only in explicitly selected historical regression fixtures.
- No barrels, old obstacles, decorations, asset placements or unrelated arena
  content were added to the oval scene. Its scene, GLB, collisions, Blender source
  and eight grid transforms are unchanged in this continuation.
- `scenes/arena/prototype_arena.tscn`, its implementation and assets remain present.
  Explicit old-map fixtures preserve content/prop/pickup regression coverage.
- The oval registers an empty pickup layout with the existing item authority.
  Inventory, grants, Wrench/Missile use, item replication and presentation survive;
  there are no placed pickups. Prop observations/checkpoints are absent rather than
  fabricated from old barrels.
- Menu flow, music/audio, vehicle model and tuning, native vehicle adapters,
  camera/HUD, match rules, networking, player identity and lifecycle retain their
  existing owners. No networking wire format or provider was replaced.

## Foundation measurements and import

The [foundation report](ts-71.md) and [authoring notes](../../assets/maps/oval/README.md)
record source treatment and exact measurements. This continuation re-opened the
retained master in Blender and reran the imported-map geometry/collision/driving
fixture without rebuilding or replacing the implementation.

| Requirement | Verified result |
| --- | --- |
| Units | Blender scale 1; identity Godot transforms; one world unit per metre |
| Lap / straight / turn radius | 999.766840 m plan lap; 214 m straights; 91 m radius |
| Width | 18 m measured along the banked surface |
| Banking | Preserved master samples; 35 degrees; outside rise 10.324376 m |
| Transitions | Four source-sampled brackets of 149.6064 m, consistent with 150 m reference |
| Footprint | 410.741608 × 200 m after banking's horizontal contraction |
| Infield | Flat y=0, sharing the inner rim |
| Grid / reference | Eight 6 × 3 m slots; separate imported 4.81 m reference vehicle |
| Collision | 10,992 road triangles and 916 infield triangles; 22,900 road raycasts |
| Native driving | Full lap, 0.709 m maximum centerline deviation; no sustained support loss |

The supplied approximate 414 m length is the unbanked envelope. It is not used to
stretch the master banking or invalidate the specified surface width/radius.
Editable production source, immutable master and Godot GLB/import configuration
remain committed. The master SHA-256 remains
`49af73b8d400e25682636e6c361c7432c4c8b5c946f66b69050733babc848eba`.

## Current successful checks

All entries below are **VERIFIED** by execution, not inferred from compilation.
Godot version: 4.7.2 .NET; Windows; rendered checks use OpenGL compatibility.

| Check | Result |
| --- | --- |
| `check.ps1`, rerun after main integration | Formatting, warning-free Debug/Release builds; 443 Core and 311 non-native Client/transport tests in each configuration |
| Main synchronization and version rules | Current main `728b23b` merged; canonical 0.1.1 and Windows 0.1.1.0; required product/company metadata retained |
| `check-oval.ps1 -NoBuild -Visual` | 133 assertions, rendered source/scale views, complete native lap, eight practice spawns and eight reset placements, existing music playing |
| `check-menu.ps1 -Visual` | 113 assertions; normal gameplay on oval, solo Waiting, navigation/settings, leave and Quit |
| `check-lobby.ps1 -NoBuild` | Eight native peers, repeated matches, slot reuse, interrupted fresh admission, active admission, capacity and teardown |
| `check-death-respawn.ps1 -NoBuild -Impaired` | Four missile/collision cycles across eight peers; exact new-grid respawns, HP/items/physics/VFX reset |
| `check-match.ps1 -NoBuild` | Six eight-peer combat cycles; scores, winner and lifecycle agree on oval |
| `check-migration.ps1 -NoBuild -Players 3` | Successive lobby/gameplay authority changes; map-aware checkpoint restore, retained bodies, inventory and configuration |
| `check-reconnect.ps1 -NoBuild` | Three native resyncs, including 125 seconds offline; retained identity/body, inventory, empty pickup layout, scores and fresh generation |
| `check-network-vehicles.ps1 -NoBuild -Players 2 -Latency 30 -Jitter 5 -Loss 2 -Visual` | Both processes pass; grounded oval driving, 0.105648 m client correction p99; no large corrections |
| `check-audio.ps1 -NoBuild` | 53 assertions plus committed asset checksum checks |
| `check-vehicle.ps1` | 126 assertions at each of 30 and 144 render FPS; fixed-step/surface replays agree within 0.02 |
| `check-items.ps1 -NoBuild` | Eight-peer retained item/combat fixture passes |
| `check-item-spawns.ps1 -NoBuild` | Eight-peer retained pickup fixture passes, including contention/cooldown/distribution |
| Main project smoke | 120 headless frames, no warnings/errors |

The death/reconnect harnesses explicitly own a test-only wall collider for their
existing lethal-impact scenario, outside the map node. Normal gameplay does not
instantiate it. Separate-process driving now turns inward from the grid instead
of depending on the old arena's perimeter walls; it verifies initial grid placement
and final grounded support without loosening the existing correction threshold.

[Oval assertions](ts-71-map-swap/oval-evidence.txt) ·
[Normal game menu on oval](ts-71-map-swap/game-menu.png) ·
[Rendered network driving](ts-71-map-swap/network-driving.png) ·
[Network measurements and control](ts-71-map-swap/network-metrics.json)

## Failed stress check and verification limits

- **VERIFIED failure:** eight separate local Godot processes with 30 ms outbound
  delay, 5 ms jitter and 2% loss exceeded the existing correction p99 <3 m gate.
  The final oval run had client p99 values 1.91–4.37 m; all eight spawned from the
  oval grid and finished grounded. The same eight-process old-map control also
  failed, with client p99 3.45–5.99 m. No threshold was relaxed and these runs are
  not counted as passes. Earlier test routes drove off the intentionally open edge;
  those were corrected in the fixture, not by adding map barriers.
- **INFERRED:** the old-map control does not support attributing that stress failure
  specifically to the map swap. It does not prove the cause is solely hardware or
  establish eight-process performance acceptance. Multi-machine performance needs
  separate validation; no unrelated network tuning/refactor was added here.
- **UNVERIFIED:** real EOS multi-machine/WAN interruption and indefinite session
  soak. Native migration/reconnect use documented trusted identity seams.
- Audio playback state, routing and event behavior were tested; subjective speaker
  output and human controller/driving feel were not personally evaluated.
- As previously recorded, the local master matches the Jira attachment's filename
  and 1,178,873-byte size. Direct remote retrieval returned HTTP 403; remote/local
  byte equality remains unverified despite the stable local hash and matching
  measured geometry.
- Foundation edges remain intentionally open. There is no added containment,
  out-of-bounds recovery, dressing, vehicle rescaling or handling rebalance.

No old-map content was migrated to address a test failure. Feature documentation
and integration links reflect the active map; the earlier foundation-only report
remains historical evidence rather than a statement of current map selection.
